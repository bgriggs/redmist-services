using BigMission.Cache.Models;
using BigMission.CommandTools;
using BigMission.CommandTools.Models;
using BigMission.EntityFrameworkCore;
using BigMission.RaceHeroSdk.Models;
using BigMission.RaceManagement;
using BigMission.ServiceData;
using BigMission.ServiceStatusTools;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using NLog;
using StackExchange.Redis;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;

namespace BigMission.FuelStatistics
{
    /// <summary>
    /// Processes application status from the in car apps. (not channel status)
    /// </summary>
    class Application
    {
        private IConfiguration Config { get; }
        private ILogger Logger { get; }
        private ServiceTracking ServiceTracking { get; }
        private readonly EventHubHelpers ehReader;
        private readonly ManualResetEvent serviceBlock = new ManualResetEvent(false);
        private readonly ConnectionMultiplexer cacheMuxer;
        private readonly Dictionary<int, Event> events = new Dictionary<int, Event>();
        private ConfigurationCommands configurationChanges;
        private static readonly string[] configChanges = new[]
        {
            ConfigurationCommandTypes.EVENT_SUBSCRIPTION_MODIFIED,
        };
        private Timer lapCheckTimer;


        public Application(IConfiguration config, ILogger logger, ServiceTracking serviceTracking)
        {
            Config = config;
            Logger = logger;
            ServiceTracking = serviceTracking;
            ehReader = new EventHubHelpers(logger);
            cacheMuxer = ConnectionMultiplexer.Connect(Config["RedisConn"]);
        }


        public void Run()
        {
            ServiceTracking.Update(ServiceState.STARTING, string.Empty);

            if (configurationChanges == null)
            {
                var group = "config-" + Config["ServiceId"];
                configurationChanges = new ConfigurationCommands(Config["KafkaConnectionString"], group, Config["KafkaConfigurationTopic"], Logger);
                configurationChanges.Subscribe(configChanges, ProcessConfigurationChange);
            }

            InitializeEvents();

            //LoadFullTestLaps();
            //var c = events[28141].Cars["134"];

            //foreach (var p in c.Pits)
            //{
            //    Console.WriteLine($"Time={p.EstPitStopSecs} TS={p.EndPitTime} RefLap={p.RefLapTimeSecs}");
            //    Console.WriteLine($"\t{p.Comments}");
            //    foreach (var l in ((PitStop)p).Laps)
            //    {
            //        Console.WriteLine($"\tLap={l.Value.CurrentLap} TS={l.Value.Timestamp} LastPitLap={l.Value.LastPitLap} LapTime={l.Value.LastLapTimeSeconds}");
            //    }
            //}


            // Start timer to read redis list of laps
            lapCheckTimer = new Timer(CheckForEventLaps, null, 250, 500);

            // Start updating service status
            ServiceTracking.Start();
            Logger.Info("Started");
            serviceBlock.WaitOne();
        }

        private void InitializeEvents()
        {
            var eventSettings = LoadEventSettings();
            foreach (var settings in eventSettings)
            {
                if (!events.TryGetValue(settings.Id, out Event e))
                {
                    e = new Event(settings.Id, cacheMuxer, Config["ConnectionString"]);
                    e.Initialize();
                    events[settings.Id] = e;
                }
            }

            var removedEventIds = events.Keys.Where(i => !eventSettings.Select(s => s.Id).Contains(i));
            foreach (var id in removedEventIds)
            {
                events[id].Dispose();
                events.Remove(id);
            }
        }

        private RaceEventSettings[] LoadEventSettings()
        {
            try
            {
                var cf = new BigMissionDbContextFactory();
                using var db = cf.CreateDbContext(new[] { Config["ConnectionString"] });

                var events = db.RaceEventSettings
                    .Where(s => !s.IsDeleted && s.IsEnabled)
                    .ToArray();

                // Filter by subscription time
                var activeSubscriptions = new List<RaceEventSettings>();

                foreach (var evt in events)
                {
                    // Get the local time zone info if available
                    TimeZoneInfo tz = null;
                    if (!string.IsNullOrWhiteSpace(evt.EventTimeZoneId))
                    {
                        try
                        {
                            tz = TimeZoneInfo.FindSystemTimeZoneById(evt.EventTimeZoneId);
                        }
                        catch { }
                    }

                    // Attempt to use local time, otherwise use UTC
                    var now = DateTime.UtcNow;
                    if (tz != null)
                    {
                        now = TimeZoneInfo.ConvertTimeFromUtc(now, tz);
                    }

                    // The end date should go to the end of the day the that the user specified
                    var end = new DateTime(evt.EventEnd.Year, evt.EventEnd.Month, evt.EventEnd.Day, 23, 59, 59);
                    if (evt.EventStart <= now && end >= now)
                    {
                        activeSubscriptions.Add(evt);
                    }
                }

                return activeSubscriptions.ToArray();
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Unable to load events");
            }
            return new RaceEventSettings[0];
        }

        private void CheckForEventLaps(object o)
        {
            if (Monitor.TryEnter(lapCheckTimer))
            {
                try
                {
                    var sw = Stopwatch.StartNew();

                    var cache = cacheMuxer.GetDatabase();
                    foreach (var evt in events)
                    {
                        try
                        {
                            var laps = PopEventLaps(evt.Key, cache);
                            evt.Value.UpdateLap(laps);
                        }
                        catch(Exception ex)
                        {
                            Logger.Error(ex, $"Error processing event laps for event={evt.Key}");
                        }
                    }

                    Logger.Debug($"Processed lap updates in {sw.ElapsedMilliseconds}ms");
                }
                catch (Exception ex)
                {
                    Logger.Error(ex, "Error checking for laps to process");
                }
                finally
                {
                    Monitor.Exit(lapCheckTimer);
                }
            }
            else
            {
                Logger.Debug("Skipping lap processing");
            }
        }

        private Lap[] PopEventLaps(int eventId, IDatabase cache)
        {
            var laps = new List<Lap>();
            var key = string.Format(Consts.LAPS_FUEL_STAT, eventId);
            Lap lap;
            do
            {
                lap = null;
                var lapJson = cache.ListRightPop(key);
                if (!string.IsNullOrEmpty(lapJson))
                {
                    var racer = JsonConvert.DeserializeObject<CarRaceLap>(lapJson);
                    lap = new Lap
                    {
                        EventId = eventId,
                        Timestamp = racer.Timestamp,
                        CarNumber = racer.CarNumber,
                        ClassName = racer.ClassName,
                        CurrentLap = racer.CurrentLap,
                        LastLapTimeSeconds = racer.LastLapTimeSeconds,
                        LastPitLap = racer.LastPitLap,
                        PitStops = racer.PitStops,
                        PositionInRun = racer.PositionInRun
                    };
                    laps.Add(lap);
                }

            } while (lap != null);

            return laps.ToArray();
        }

        /// <summary>
        /// When a change to event subscriptions is received invalidate and reload.
        /// </summary>
        /// <param name="command"></param>
        private void ProcessConfigurationChange(KeyValuePair<string, string> command)
        {
            if (command.Key == ConfigurationCommandTypes.EVENT_SUBSCRIPTION_MODIFIED)
            {
                InitializeEvents();
            }
        }

        private void LoadFullTestLaps()
        {
            var lines = File.ReadAllLines("TestLaps.csv");
            var st = new Stack<Lap>();
            for (int i = 1; i < lines.Length; i++)
            {
                var lap = ParseTestLap(lines[i]);
                st.Push(lap);
                if (!events.TryGetValue(lap.EventId, out _))
                {
                    Event evt = new Event(lap.EventId, cacheMuxer, Config["ConnectionString"]);
                    events[lap.EventId] = evt;
                }
            }

            var eventLaps = st.GroupBy(l => l.EventId);
            foreach (var el in eventLaps)
            {
                events[el.Key].UpdateLap(el.ToArray());
            }
        }

        private static Lap ParseTestLap(string line)
        {
            var values = line.Split('\t');
            var l = new Lap();
            l.EventId = int.Parse(values[0]);
            l.CarNumber = values[1];
            l.Timestamp = DateTime.Parse(values[2]);
            l.ClassName = values[3];
            l.PositionInRun = int.Parse(values[4]);
            l.CurrentLap = int.Parse(values[5]);
            l.LastLapTimeSeconds = double.Parse(values[6]);
            l.LastPitLap = int.Parse(values[7]);
            l.PitStops = int.Parse(values[8]);

            return l;
        }
    }
}
