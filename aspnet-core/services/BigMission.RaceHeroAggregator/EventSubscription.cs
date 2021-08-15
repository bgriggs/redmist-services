using BigMission.Cache.Models;
using BigMission.CommandTools;
using BigMission.EntityFrameworkCore;
using BigMission.RaceHeroSdk;
using BigMission.RaceHeroSdk.Models;
using BigMission.RaceManagement;
using BigMission.Teams;
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
using System.Threading;
using System.Threading.Tasks;

namespace BigMission.RaceHeroAggregator
{
    class EventSubscription : IDisposable, IAsyncDisposable
    {
        private ILogger Logger { get; }
        private IConfiguration Config { get; }
        private bool disposed;
        public string EventId
        {
            get { return settings.Values.FirstOrDefault()?.RaceHeroEventId; }
        }

        private readonly List<CarSubscription> subscriberCars = new List<CarSubscription>();
        private readonly Dictionary<int, RaceEventSettings> settings = new Dictionary<int, RaceEventSettings>();
        private IRaceHeroClient RhClient { get; set; }

        private Timer waitForStartTimer;
        private readonly object waitForStartLock = new object();
        private Event lastEvent;
        private readonly object lastEventLock = new object();

        private Timer pollLeaderboardTimer;
        private readonly object pollLeaderboardLock = new object();

        /// <summary>
        /// Latest Car status
        /// </summary>
        private readonly Dictionary<string, Racer> racerStatus = new Dictionary<string, Racer>();

        private const int FUEL_STATS_MAX_LEN = 200;
        private readonly ConnectionMultiplexer cacheMuxer;
        private readonly ChannelData channelData;
        private FlagStatus flagStatus;
        private readonly bool logRHToFile;
        private readonly bool readTestFiles;


        public EventSubscription(ILogger logger, IConfiguration config, ConnectionMultiplexer cacheMuxer, IRaceHeroClient raceHeroClient)
        {
            Logger = logger;
            Config = config;
            this.cacheMuxer = cacheMuxer;
            RhClient = raceHeroClient;
            channelData = new ChannelData(Config["ServiceId"], Config["KafkaConnectionString"], Config["KafkaDataTopic"]);
            logRHToFile = bool.Parse(Config["LogRHToFile"]);
            readTestFiles = bool.Parse(Config["ReadTestFiles"]);
        }


        public void Start()
        {
            var intrval = int.Parse(Config["WaitForStartTimer"]);
            waitForStartTimer = new Timer(CheckWaitForStart, null, 0, intrval);
        }

        #region Subscription Settings

        public void UpdateSetting(RaceEventSettings[] setting)
        {
            var carSubIds = new List<int>();
            lock (settings)
            {
                settings.Clear();
                setting.ForEach(s =>
                {
                    settings[s.Id] = s;
                    carSubIds.AddRange(s.GetCarIds());
                });
            }
            carSubIds = carSubIds.Distinct().ToList();

            int[] existingCarIds;
            lock (subscriberCars)
            {
                existingCarIds = subscriberCars.Select(c => c.CarId).ToArray();
            }
            var newCarIds = carSubIds.Except(existingCarIds).ToArray();
            var newCars = LoadCars(newCarIds);
            var oldCarIds = existingCarIds.Except(carSubIds).ToArray();

            lock (subscriberCars)
            {
                newCars.ForEach(c =>
                {
                    var cs = new CarSubscription(Logger, Config, channelData) { CarId = c.Id, CarNumber = c.Number };
                    subscriberCars.Add(cs);
                    cs.InitializeChannels();
                });

                // Delete old car subscriptions
                subscriberCars.RemoveAll(c => oldCarIds.Contains(c.CarId));
            }
        }

        private Car[] LoadCars(int[] carIds)
        {
            var cf = new BigMissionDbContextFactory();
            using var db = cf.CreateDbContext(new[] { Config["ConnectionString"] });
            return db.Cars
                .Where(s => !s.IsDeleted && carIds.Contains(s.Id))
                .ToArray();
        }

        #endregion

        /// <summary>
        /// Checks on status of the event waiting for it to start.
        /// </summary>
        /// <param name="obj"></param>
        private void CheckWaitForStart(object obj)
        {
            if (Monitor.TryEnter(waitForStartLock))
            {
                try
                {
                    var eventId = EventId;
                    Logger.Debug($"Checking for event {eventId} to start");
                    var evtTask = RhClient.GetEvent(eventId);
                    evtTask.Wait();
                    LogEventPoll(evtTask.Result);

                    bool isLive, isEnded;
                    lock (lastEventLock)
                    {
                        lastEvent = evtTask.Result;
                        isLive = lastEvent.IsLive;
                        //isEnded = lastEvent.EndedAt
                        isEnded = false;
                    }

                    // When the event starts, transition to poll for leaderboard data
                    if (isLive)
                    {
                        Logger.Info($"Event {eventId} is live, starting to poll for race status");

                        if (int.TryParse(EventId, out int eid))
                        {
                            flagStatus = new FlagStatus(eid, Logger, Config, cacheMuxer);
                        }

                        // Start polling for race status
                        var interval = int.Parse(Config["EventPollTimer"]);
                        pollLeaderboardTimer = new Timer(PollLeaderboard, null, 0, interval);

                        StopEventPolling();
                    }
                    // Check for ended
                    else if (isEnded)
                    {
                        Logger.Info($"Event {eventId} has ended, terminating subscription polling, waiting for event to restart.");
                        Start();
                    }
                }
                catch (Exception ex)
                {
                    Logger.Error(ex, "Error polling event");
                }
                finally
                {
                    Monitor.Exit(waitForStartLock);
                }
            }
            else
            {
                Logger.Info("Skipping CheckWaitForStart");
            }
        }

        private void StopEventPolling()
        {
            // Stop polling for the event
            try
            {
                waitForStartTimer?.Dispose();
                waitForStartTimer = null;
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error ending wait for event timer");
            }
        }

        private void PollLeaderboard(object obj)
        {
            if (Monitor.TryEnter(pollLeaderboardLock))
            {
                try
                {
                    var sw = Stopwatch.StartNew();
                    var eventId = EventId;
                    Logger.Trace($"Polling leaderboard for event {eventId}");
                    var lbTask = RhClient.GetLeaderboard(eventId);
                    lbTask.Wait();
                    
                    var leaderboard = lbTask.Result;
                    LogLeaderboardPoll(eventId, leaderboard);

                    // Stop polling when the event is over
                    if (leaderboard == null || leaderboard.Racers == null)
                    {
                        Logger.Info($"Event {eventId} has ended");
                        flagStatus?.EndEvent().Wait();

                        try
                        {
                            pollLeaderboardTimer.Dispose();
                        }
                        catch (Exception ex)
                        {
                            Logger.Error(ex, "Error ending poll for race data timer");
                        }
                        finally
                        {
                            Logger.Info("Restart event check for possible event restart.");
                            Start();
                        }
                    }
                    else // Process lap updates
                    {
                        var cf = leaderboard.CurrentFlag;
                        var flag = RaceHeroClient.ParseFlag(cf);
                        flagStatus?.ProcessFlagStatus(flag, leaderboard.RunId).Wait();

                        var logs = new List<Racer>();
                        Racer[] latestStatusCopy = null;
                        lock (racerStatus)
                        {
                            foreach (var newRacer in leaderboard.Racers)
                            {
                                if (racerStatus.TryGetValue(newRacer.RacerNumber, out var racer))
                                {
                                    // Process changes
                                    if (racer.CurrentLap != newRacer.CurrentLap)
                                    {
                                        // Log each new lap
                                        logs.Add(newRacer);
                                    }
                                }
                                racerStatus[newRacer.RacerNumber] = newRacer;
                            }

                            latestStatusCopy = racerStatus.Values.ToArray();
                        }

                        if (latestStatusCopy != null)
                        {
                            Logger.Trace($"Processing subscriber car lap changes");

                            // Update car data with full current field
                            lock (subscriberCars)
                            {
                                subscriberCars.ForEach(c => { c.ProcessUpdate(latestStatusCopy); });
                            }

                            Logger.Trace($"latestStatusCopy {sw.ElapsedMilliseconds}ms");
                            sw = Stopwatch.StartNew();
                        }

                        if (logs.Any())
                        {
                            var eid = int.Parse(EventId);
                            var now = DateTime.UtcNow;
                            var carRaceLaps = new List<CarRaceLap>();
                            foreach (var l in logs)
                            {
                                var log = new CarRaceLap
                                {
                                    EventId = eid,
                                    RunId = leaderboard.RunId,
                                    CarNumber = l.RacerNumber,
                                    Timestamp = now,
                                    CurrentLap = l.CurrentLap,
                                    ClassName = l.RacerClassName,
                                    LastLapTimeSeconds = l.LastLapTimeSeconds,
                                    PositionInRun = l.PositionInRun,
                                    LastPitLap = l.LastPitLap,
                                    PitStops = l.PitStops,
                                    Flag = (byte)flag
                                };
                                carRaceLaps.Add(log);
                            }

                            CacheToFuelStatistics(carRaceLaps);
                            Logger.Trace($"CacheToFuelStatistics {sw.ElapsedMilliseconds}ms");

                            if (!readTestFiles)
                            {
                                LogLapChanges(carRaceLaps);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Logger.Error(ex, "Error polling leaderboard");
                }
                finally
                {
                    Monitor.Exit(pollLeaderboardLock);
                }
            }
            else
            {
                Logger.Info("Skipping RunPollLeaderboard");
            }
        }

        private void LogLapChanges(List<CarRaceLap> laps)
        {
            Logger.Trace($"Logging leaderboard laps count={laps.Count}");
            var cf = new BigMissionDbContextFactory();
            using var db = cf.CreateDbContext(new[] { Config["ConnectionString"] });
            db.CarRacerLaps.AddRange(laps);
            db.SaveChanges();
        }

        /// <summary>
        /// Publish lap data for the fuel statistics service to consume.
        /// </summary>
        /// <param name="laps"></param>
        private void CacheToFuelStatistics(List<CarRaceLap> laps)
        {
            Logger.Trace($"Caching leaderboard for fuel statistics laps count={laps.Count}");
            var cache = cacheMuxer.GetDatabase();
            var eventKey = string.Format(Consts.LAPS_FUEL_STAT, EventId);
            foreach (var lap in laps)
            {
                var lapJson = JsonConvert.SerializeObject(lap);

                // Use the head of the list as the newest value
                var len = cache.ListLeftPush(eventKey, lapJson);
                if (len > FUEL_STATS_MAX_LEN)
                {
                    cache.ListTrim(eventKey, 0, FUEL_STATS_MAX_LEN - 1, flags: CommandFlags.FireAndForget);
                }
            }
        }

        private void LogEventPoll(Event @event)
        {
            if (logRHToFile)
            {
                const string DIR_PREFIX = "Event-{0}";
                var dir = string.Format(DIR_PREFIX, @event.Id);
                if (!Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                var evtStr = JsonConvert.SerializeObject(@event);
                File.WriteAllText($"{dir}\\evt-{DateTime.UtcNow.ToFileTimeUtc()}.json", evtStr);
            }
        }

        private void LogLeaderboardPoll(string eventId, Leaderboard leaderboard)
        {
            if (logRHToFile)
            {
                const string DIR_PREFIX = "Leaderboard-{0}-{1}";
                var dir = string.Format(DIR_PREFIX, eventId, leaderboard.RunId);
                if (!Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                var lbStr = JsonConvert.SerializeObject(leaderboard);
                File.WriteAllText($"{dir}\\lb-{DateTime.UtcNow.ToFileTimeUtc()}.json", lbStr);
            }
        }

        #region Dispose

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposed)
                return;

            if (disposing)
            {
                StopEventPolling();

                try
                {
                    pollLeaderboardTimer?.Dispose();
                }
                catch (Exception ex)
                {
                    Logger.Error(ex, "Error ending poll for race data timer");
                }
            }

            disposed = true;
        }

        public virtual ValueTask DisposeAsync()
        {
            try
            {
                Dispose();
                channelData.DisposeAsync();
                return default;
            }
            catch (Exception exception)
            {
                return new ValueTask(Task.FromException(exception));
            }
        }

        #endregion
    }
}
