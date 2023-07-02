using BigMission.Cache.Models;
using BigMission.Database;
using BigMission.Database.Models;
using BigMission.RaceHeroSdk;
using BigMission.RaceHeroSdk.Models;
using BigMission.RaceHeroSdk.Status;
using BigMission.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using StackExchange.Redis;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace BigMission.RaceHeroAggregator
{
    class EventSubscription
    {
        private ILogger Logger { get; }
        private IConfiguration Config { get; }
        public string EventId
        {
            get { return settings.Values.FirstOrDefault()?.RaceHeroEventId; }
        }

        private EventPollRequest eventPoll;
        private readonly List<CarSubscription> subscriberCars = new();
        private readonly Dictionary<int, RaceEventSetting> settings = new();
        private IRaceHeroClient RhClient { get; set; }
        private IDateTimeHelper DateTime { get; }

        private const int FUEL_STATS_MAX_LEN = 200;
        private readonly ILoggerFactory loggerFactory;
        private readonly IConnectionMultiplexer cacheMuxer;


        private FlagStatus flagStatus;
        private readonly bool logRHToFile;
        private readonly bool readTestFiles;

        private enum EventStates { WaitingForStart, Started }
        /// <summary>
        /// Latest Car status
        /// </summary>
        private readonly Dictionary<string, Racer> racerStatus = new();
        private readonly RaceHeroEventStatus currentEventStatus = new();

        private readonly TimeSpan waitForStartInterval;
        private readonly TimeSpan eventPollInterval;
        private TimeSpan pollInterval;
        private DateTime lastPoll = System.DateTime.MinValue;

        #region Simulation
        private ISimulateSettingsService simulateSettingsService;
        private readonly IDbContextFactory<RedMist> dbFactory;
        private DateTime lastFlagChange;
        private RaceHeroClient.Flag? overrideFlag;
        private DateTime lastPitStop;
        private int lastPitLap = 0;
        #endregion


        public EventSubscription(ILoggerFactory loggerFactory, IConfiguration config, IConnectionMultiplexer cacheMuxer, IRaceHeroClient raceHeroClient,
            IDateTimeHelper dateTime, ISimulateSettingsService simulateSettingsService, IDbContextFactory<RedMist> dbFactory)
        {
            Logger = loggerFactory.CreateLogger(GetType().Name);
            this.loggerFactory = loggerFactory;
            Config = config;
            this.cacheMuxer = cacheMuxer;
            RhClient = raceHeroClient;
            DateTime = dateTime;
            this.simulateSettingsService = simulateSettingsService;
            this.dbFactory = dbFactory;
            waitForStartInterval = TimeSpan.FromMilliseconds(int.Parse(Config["WAITFORSTARTTIMER"]));
            eventPollInterval = TimeSpan.FromMilliseconds(int.Parse(Config["EVENTPOLLTIMER"]));
            logRHToFile = bool.Parse(Config["LogRHToFile"]);
            readTestFiles = bool.Parse(Config["ReadTestFiles"]);
        }


        public async Task UpdateEventAsync()
        {
            var sw = Stopwatch.StartNew();
            if (EventId == null) { return; }
            if (eventPoll == null)
            {
                eventPoll = new EventPollRequest(EventId, loggerFactory, RhClient);
            }

            var pollDiff = DateTime.UtcNow - lastPoll;
            if (pollDiff >= pollInterval)
            {
                var pollResponse = await eventPoll.PollEventAsync();

                await LogEventPollAsync(pollResponse.evt);
                await PublishEventStatusAsync(currentEventStatus, pollResponse.evt, pollResponse.leaderboard);

                if (pollResponse.state == RaceHeroSdk.Status.EventStates.WaitingForStart)
                {
                    flagStatus = null;
                }
                else if (pollResponse.state == RaceHeroSdk.Status.EventStates.Started && pollResponse.leaderboard != null)
                {
                    if (flagStatus == null && int.TryParse(EventId, out int eid))
                    {
                        lastFlagChange = DateTime.Now;
                        flagStatus = new FlagStatus(eid, loggerFactory, cacheMuxer, DateTime, dbFactory);
                    }

                    await ProcessLeaderboardAsync(pollResponse.leaderboard);
                }

                // Adjust poll rate
                if (pollResponse.state == RaceHeroSdk.Status.EventStates.WaitingForStart)
                {
                    pollInterval = waitForStartInterval;
                }
                else if (pollResponse.state == RaceHeroSdk.Status.EventStates.Started)
                {
                    pollInterval = eventPollInterval;
                }
                else
                {
                    throw new NotImplementedException();
                }

                lastPoll = DateTime.UtcNow;
            }

            Logger.LogDebug($"Updated event in {sw.ElapsedMilliseconds}ms");
        }


        #region Subscription Settings

        public async Task UpdateSettingAsync(RaceEventSetting[] setting)
        {
            var carSubIds = new List<int>();

            settings.Clear();
            foreach (var s in setting)
            {
                settings[s.Id] = s;
                carSubIds.AddRange(s.GetCarIds());
            }

            carSubIds = carSubIds.Distinct().ToList();

            var existingCarIds = subscriberCars.Select(c => c.CarId).ToArray();
            var newCarIds = carSubIds.Except(existingCarIds).ToArray();
            var newCars = await LoadCarsAsync(newCarIds);
            var oldCarIds = existingCarIds.Except(carSubIds).ToArray();

            foreach (var c in newCars)
            {
                var cs = new CarSubscription(cacheMuxer, DateTime, dbFactory) { CarId = c.Id, CarNumber = c.Number };
                subscriberCars.Add(cs);
                await cs.InitializeChannels();
            }

            // Delete old car subscriptions
            subscriberCars.RemoveAll(c => oldCarIds.Contains(c.CarId));
        }

        private async Task<Car[]> LoadCarsAsync(int[] carIds)
        {
            using var db = await dbFactory.CreateDbContextAsync();
            return await db.Cars
                         .Where(s => !s.IsDeleted && carIds.Contains(s.Id))
                         .ToArrayAsync();
        }

        #endregion


        private async Task ProcessLeaderboardAsync(Leaderboard leaderboard)
        {
            try
            {
                var sw = Stopwatch.StartNew();
                await LogLeaderboardPollAsync(EventId, leaderboard);

                // Stop polling when the event is over
                if (leaderboard == null || leaderboard.Racers == null)
                {
                    Logger.LogInformation($"Event {EventId} has ended");
                    await flagStatus?.EndEvent();
                }
                else // Process lap updates
                {
                    var cf = leaderboard.CurrentFlag;
                    var flag = RaceHeroClient.ParseFlag(cf);

                    // Simulate yellow flags
                    if (simulateSettingsService.Settings.YellowFlags)
                    {
                        if ((DateTime.Now - lastFlagChange) > TimeSpan.FromMinutes(1))
                        {
                            if (overrideFlag == null || overrideFlag == RaceHeroClient.Flag.Green)
                            {
                                overrideFlag = RaceHeroClient.Flag.Yellow;
                                lastFlagChange = DateTime.Now;
                            }
                            else if (overrideFlag == RaceHeroClient.Flag.Yellow)
                            {
                                overrideFlag = RaceHeroClient.Flag.Green;
                                lastFlagChange = DateTime.Now;
                            }
                        }
                    }

                    await flagStatus?.ProcessFlagStatus(overrideFlag ?? flag, leaderboard.RunId);

                    var logs = new List<Racer>();
                    foreach (var newRacer in leaderboard.Racers)
                    {
                        if (racerStatus.TryGetValue(newRacer.RacerNumber, out var racer))
                        {
                            // Simulate code for pit stops
                            if (simulateSettingsService.Settings.PitStops)
                            {
                                if (newRacer.RacerNumber.Contains("777"))
                                {
                                    if ((DateTime.Now - lastPitStop) > TimeSpan.FromMinutes(4))
                                    {
                                        lastPitLap = newRacer.CurrentLap;
                                        lastPitStop = DateTime.Now;
                                    }
                                    newRacer.LastPitLap = lastPitLap;
                                }
                            }

                            // Process changes
                            if (racer.CurrentLap != newRacer.CurrentLap)
                            {
                                // Log each new lap
                                logs.Add(newRacer);
                            }
                        }
                        racerStatus[newRacer.RacerNumber] = newRacer;
                    }

                    var latestStatusCopy = racerStatus.Values.ToArray();
                    Logger.LogTrace($"Processing subscriber car lap changes");

                    // Update car data with full current field
                    subscriberCars.ForEach(async c => { await c.ProcessUpdate(latestStatusCopy); });
                    //Logger.Trace($"latestStatusCopy {sw.ElapsedMilliseconds}ms");

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

                        await CacheToFuelStatisticsAsync(carRaceLaps);
                        Logger.LogTrace($"CacheToFuelStatistics {sw.ElapsedMilliseconds}ms");

                        if (!readTestFiles)
                        {
                            await LogLapChangesAsync(carRaceLaps);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Error polling leaderboard");
            }
        }

        private async Task LogLapChangesAsync(List<CarRaceLap> laps)
        {
            Logger.LogTrace($"Logging leaderboard laps count={laps.Count}");
            using var db = await dbFactory.CreateDbContextAsync();
            db.CarRaceLaps.AddRange(laps);
            await db.SaveChangesAsync();
        }

        /// <summary>
        /// Publish lap data for the fuel statistics service to consume.
        /// </summary>
        /// <param name="laps"></param>
        private async Task CacheToFuelStatisticsAsync(List<CarRaceLap> laps)
        {
            Logger.LogTrace($"Caching leaderboard for fuel statistics laps count={laps.Count}");
            var cache = cacheMuxer.GetDatabase();
            var eventKey = string.Format(Consts.LAPS_FUEL_STAT, EventId);
            foreach (var lap in laps)
            {
                var lapJson = JsonConvert.SerializeObject(lap);

                // Use the head of the list as the newest value
                var len = await cache.ListLeftPushAsync(eventKey, lapJson);
                if (len > FUEL_STATS_MAX_LEN)
                {
                    await cache.ListTrimAsync(eventKey, 0, FUEL_STATS_MAX_LEN - 1, flags: CommandFlags.FireAndForget);
                }
            }
        }

        private async Task LogEventPollAsync(Event @event)
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
                await File.WriteAllTextAsync($"{dir}\\evt-{DateTime.UtcNow.ToFileTimeUtc()}.json", evtStr);
            }
        }

        private async Task LogLeaderboardPollAsync(string eventId, Leaderboard leaderboard)
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
                await File.WriteAllTextAsync($"{dir}\\lb-{DateTime.UtcNow.ToFileTimeUtc()}.json", lbStr);
            }
        }

        private async Task PublishEventStatusAsync(RaceHeroEventStatus status, Event rhEvent, Leaderboard leaderboard)
        {
            if (rhEvent != null)
            {
                status.Id = rhEvent.Id;
                status.Name = rhEvent.Name;
                status.StartedAt = rhEvent.StartedAt;
                status.EndedAt = rhEvent.EndedAt;
                status.IsLive = rhEvent.IsLive;
                status.Notes = rhEvent.Notes;
                status.Timezone = rhEvent.Timezone;
                status.Meta = rhEvent.Meta;
                status.EventUrl = rhEvent.EventUrl;
                status.CreatedAt = rhEvent.CreatedAt;
                status.UpdatedAt = rhEvent.UpdatedAt;
            }
            if (leaderboard != null)
            {
                status.RunId = leaderboard.RunId;
                status.RunType = leaderboard.RunType;
                status.CurrentLap = leaderboard.CurrentLap;
                status.CurrentFlag = leaderboard.CurrentFlag;
                status.LapsRemaining = leaderboard.LapsRemaining;
                status.TimeRemaining = leaderboard.TimeRemaining;
                status.CurrentTime = leaderboard.CurrentTime;
            }

            if (status.Id > 0)
            {
                var statusJson = JsonConvert.SerializeObject(status);
                var cache = cacheMuxer.GetDatabase();
                var key = string.Format(Consts.EVENT_STATUS, status.Id);
                await cache.StringSetAsync(key, statusJson);
            }
        }
    }
}
