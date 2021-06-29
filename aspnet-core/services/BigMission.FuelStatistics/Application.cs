using Azure.Messaging.EventHubs.Consumer;
using BigMission.Cache.Models;
using BigMission.CommandTools;
using BigMission.DeviceApp.Shared;
using BigMission.EntityFrameworkCore;
using BigMission.RaceManagement;
using BigMission.ServiceData;
using BigMission.ServiceStatusTools;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using NLog;
using NUglify.Helpers;
using StackExchange.Redis;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

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
        private Timer eventSubscriptionTimer;
        private readonly object eventSubscriptionCheckLock = new object();
        private readonly ManualResetEvent serviceBlock = new ManualResetEvent(false);
        private readonly ConnectionMultiplexer cacheMuxer;
        private readonly Dictionary<int, Event> eventSubscriptions = new Dictionary<int, Event>();
        private Timer lapCheckTimer;
        private readonly EventHubHelpers ehReader;

        public Application(IConfiguration config, ILogger logger, ServiceTracking serviceTracking)
        {
            Config = config;
            Logger = logger;
            ServiceTracking = serviceTracking;
            cacheMuxer = ConnectionMultiplexer.Connect(Config["RedisConn"]);
            ehReader = new EventHubHelpers(logger);
        }


        public void Run()
        {
            ServiceTracking.Update(ServiceState.STARTING, string.Empty);

            // Load event subscriptions
            eventSubscriptionTimer = new Timer(RunSubscriptionCheck, null, 0, int.Parse(Config["EventSubscriptionCheckMs"]));

            // Process changes from stream and cache them here is the service
            var partitionFilter = EventHubHelpers.GetPartitionFilter(Config["PartitionFilter"]);
            Task receiveStatus = ehReader.ReadEventHubPartitionsAsync(Config["KafkaConnectionString"], Config["KafkaDataTopic"], Config["KafkaConsumerGroup"],
                partitionFilter, EventPosition.Latest, ReceivedEventCallback);

            //InitializeEvents();

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

        private void RunSubscriptionCheck(object obj)
        {
            if (Monitor.TryEnter(eventSubscriptionCheckLock))
            {
                try
                {
                    var eventSettings = LoadEventSettings();
                    Logger.Info($"Loaded {eventSettings.Length} event subscriptions.");
                    var settingEventGrps = eventSettings.GroupBy(s => s.RaceHeroEventId);
                    var eventIds = eventSettings.Select(s => int.Parse(s.RaceHeroEventId)).Distinct();
                    var cache = cacheMuxer.GetDatabase();

                    lock (eventSubscriptions)
                    {
                        foreach (var settings in eventSettings)
                        {
                            var eventId = int.Parse(settings.RaceHeroEventId);
                            if (!eventSubscriptions.TryGetValue(eventId, out Event e))
                            {
                                e = new Event(settings, cacheMuxer, Config["ConnectionString"], Logger);
                                e.Initialize();
                                eventSubscriptions[eventId] = e;

                                // Clear event in cache
                                var hashKey = string.Format(Consts.FUEL_STAT, e.RhEventId);
                                var ehash = cache.HashGetAll(hashKey);
                                foreach (var ckey in ehash)
                                {
                                    cache.HashDelete(hashKey, ckey.Name.ToString());
                                }
                            }
                        }

                        // Remove deleted
                        var expiredEvents = eventSubscriptions.Keys.Except(eventIds);
                        expiredEvents.ForEach(e =>
                        {
                            Logger.Info($"Removing event subscription {e}");
                            if (eventSubscriptions.Remove(e, out Event sub))
                            {
                                try
                                {
                                    sub.Dispose();
                                }
                                catch (Exception ex)
                                {
                                    Logger.Error(ex, $"Error stopping event subscription {sub.RhEventId}.");
                                }
                            }
                        });
                    }
                }
                catch (Exception ex)
                {
                    Logger.Error(ex, "Error polling subscriptions");
                }
                finally
                {
                    Monitor.Exit(eventSubscriptionCheckLock);
                }
            }
            else
            {
                Logger.Info("Skipping RunSubscriptionCheck");
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
                    foreach (var evt in eventSubscriptions)
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
                        PositionInRun = racer.PositionInRun,
                        Flag = racer.Flag
                    };
                    laps.Add(lap);
                }

            } while (lap != null);

            return laps.ToArray();
        }

        private void ReceivedEventCallback(PartitionEvent receivedEvent)
        {
            try
            {
                var sw = Stopwatch.StartNew();
                if (!eventSubscriptions.Any())
                {
                    return;
                }
                if (receivedEvent.Data.Properties.Count > 0 && receivedEvent.Data.Properties.ContainsKey("ChannelDataSetDto"))
                {
                    if (receivedEvent.Data.Properties["Type"].ToString() != "ChannelDataSetDto")
                        return;
                }

                var json = Encoding.UTF8.GetString(receivedEvent.Data.Body.ToArray());
                var chDataSet = JsonConvert.DeserializeObject<ChannelDataSetDto>(json);

                if (chDataSet.Data == null)
                {
                    chDataSet.Data = new ChannelStatusDto[] { };
                }

                Event[] events;
                lock (eventSubscriptions)
                {
                    events = eventSubscriptions.Values.ToArray();
                }

                Parallel.ForEach(events, evt => 
                {
                    evt.UpdateTelemetry(chDataSet);
                });

                Logger.Trace($"Processed car status in {sw.ElapsedMilliseconds}ms");
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Unable to process event from event hub partition");
            }
        }

        #region Testing

        //private void LoadFullTestLaps()
        //{
        //    var lines = File.ReadAllLines("TestLaps.csv");
        //    var st = new Stack<Lap>();
        //    for (int i = 1; i < lines.Length; i++)
        //    {
        //        var lap = ParseTestLap(lines[i]);
        //        st.Push(lap);
        //        if (!eventSubscriptions.TryGetValue(lap.EventId, out _))
        //        {
        //            Event evt = new Event(lap.EventId.ToString(), cacheMuxer, Config["ConnectionString"]);
        //            eventSubscriptions[lap.EventId] = evt;
        //        }
        //    }

        //    var eventLaps = st.GroupBy(l => l.EventId);
        //    foreach (var el in eventLaps)
        //    {
        //        foreach (var lap in el)
        //        {
        //            eventSubscriptions[el.Key].UpdateLap(lap);
        //            Logger.Trace($"Updating lap for {lap.CarNumber}");
        //            Thread.Sleep(100);
        //        }
        //    }
        //}

        //private static Lap ParseTestLap(string line)
        //{
        //    var values = line.Split('\t');
        //    var l = new Lap();
        //    l.EventId = int.Parse(values[0]);
        //    l.CarNumber = values[1];
        //    l.Timestamp = DateTime.Parse(values[2]);
        //    l.ClassName = values[3];
        //    l.PositionInRun = int.Parse(values[4]);
        //    l.CurrentLap = int.Parse(values[5]);
        //    l.LastLapTimeSeconds = double.Parse(values[6]);
        //    l.LastPitLap = int.Parse(values[7]);
        //    l.PitStops = int.Parse(values[8]);

        //    return l;
        //}

        #endregion
    }
}
