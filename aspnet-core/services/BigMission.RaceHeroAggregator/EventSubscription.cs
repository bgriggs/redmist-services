﻿using BigMission.Cache.Models;
using BigMission.Database;
using BigMission.Database.Models;
using BigMission.RaceHeroSdk;
using BigMission.RaceHeroSdk.Models;
using BigMission.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using NLog;
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

        private readonly List<CarSubscription> subscriberCars = new();
        private readonly Dictionary<int, RaceEventSetting> settings = new();
        private IRaceHeroClient RhClient { get; set; }
        private IDateTimeHelper DateTime { get; }

        private const int FUEL_STATS_MAX_LEN = 200;
        private readonly ConnectionMultiplexer cacheMuxer;


        private FlagStatus flagStatus;
        private readonly bool logRHToFile;
        private readonly bool readTestFiles;

        private Event lastEvent;
        private enum EventStates { WaitingForStart, Started }
        private EventStates state;
        /// <summary>
        /// Latest Car status
        /// </summary>
        private readonly Dictionary<string, Racer> racerStatus = new();
        private readonly RaceHeroEventStatus currentEventStatus = new();

        private readonly TimeSpan waitForStartInterval;
        private DateTime lastCheckWaitForStart = System.DateTime.MinValue;
        private readonly TimeSpan eventPollInterval;
        private DateTime lastEventPoll = System.DateTime.MinValue;

        public EventSubscription(ILogger logger, IConfiguration config, ConnectionMultiplexer cacheMuxer, IRaceHeroClient raceHeroClient, IDateTimeHelper dateTime)
        {
            Logger = logger;
            Config = config;
            this.cacheMuxer = cacheMuxer;
            RhClient = raceHeroClient;
            DateTime = dateTime;
            waitForStartInterval = TimeSpan.FromMilliseconds(int.Parse(Config["WaitForStartTimer"]));
            eventPollInterval = TimeSpan.FromMilliseconds(int.Parse(Config["EventPollTimer"]));
            logRHToFile = bool.Parse(Config["LogRHToFile"]);
            readTestFiles = bool.Parse(Config["ReadTestFiles"]);
        }


        public async Task UpdateEvent()
        {
            var sw = Stopwatch.StartNew();

            var waitForStartDiff = DateTime.UtcNow - lastCheckWaitForStart;
            var pollEventDiff = DateTime.UtcNow - lastEventPoll;

            if (state == EventStates.WaitingForStart && waitForStartDiff >= waitForStartInterval)
            {
                await CheckWaitForStart();
                lastCheckWaitForStart = DateTime.UtcNow;
            }
            else if (state == EventStates.Started && pollEventDiff >= eventPollInterval)
            {
                await PollLeaderboard();
                lastEventPoll = DateTime.UtcNow;
            }

            Logger.Debug($"Updated event in {sw.ElapsedMilliseconds}ms state={state}");
        }


        #region Subscription Settings

        public async Task UpdateSetting(RaceEventSetting[] setting)
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
            var newCars = await LoadCars(newCarIds);
            var oldCarIds = existingCarIds.Except(carSubIds).ToArray();

            foreach (var c in newCars)
            {
                var cs = new CarSubscription(Config, cacheMuxer, DateTime) { CarId = c.Id, CarNumber = c.Number };
                subscriberCars.Add(cs);
                await cs.InitializeChannels();
            }

            // Delete old car subscriptions
            subscriberCars.RemoveAll(c => oldCarIds.Contains(c.CarId));
        }

        private async Task<Car[]> LoadCars(int[] carIds)
        {
            using var db = new RedMist(Config["ConnectionString"]);
            return await db.Cars
                         .Where(s => !s.IsDeleted && carIds.Contains(s.Id))
                         .ToArrayAsync();
        }

        #endregion

        /// <summary>
        /// Checks on status of the event waiting for it to start.
        /// </summary>
        private async Task CheckWaitForStart()
        {
            try
            {
                var eventId = EventId;
                Logger.Debug($"Checking for event {eventId} to start");
                var evt = await RhClient.GetEvent(eventId);
                await LogEventPoll(evt);
                await PublishEventStatus(currentEventStatus, evt, null);

                lastEvent = evt;
                var isLive = lastEvent.IsLive;
                //isEnded = lastEvent.EndedAt
                var isEnded = false;

                // When the event starts, transition to poll for leaderboard data
                if (isLive)
                {
                    lastFlagChange = DateTime.Now;
                    Logger.Info($"Event {eventId} is live, starting to poll for race status");

                    if (int.TryParse(EventId, out int eid))
                    {
                        flagStatus = new FlagStatus(eid, Logger, Config, cacheMuxer, DateTime);
                    }

                    // Start polling for race status
                    //var interval = int.Parse(Config["EventPollTimer"]);
                    //pollLeaderboardTimer = new Timer(PollLeaderboard, null, 0, interval);

                    //StopEventPolling();
                    state = EventStates.Started;
                }
                // Check for ended
                else if (isEnded)
                {
                    Logger.Info($"Event {eventId} has ended, terminating subscription polling, waiting for event to restart.");
                    state = EventStates.WaitingForStart;
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error polling event");
            }
        }

        DateTime lastFlagChange;
        RaceHeroClient.Flag? overrideFlag;
        DateTime lastPitStop;
        int lastPitLap = 0;

        private async Task PollLeaderboard()
        {
            try
            {
                var sw = Stopwatch.StartNew();
                Logger.Trace($"Polling leaderboard for event {EventId}");
                var leaderboard = await RhClient.GetLeaderboard(EventId);
                await PublishEventStatus(currentEventStatus, null, leaderboard);
                await LogLeaderboardPoll(EventId, leaderboard);

                // Stop polling when the event is over
                if (leaderboard == null || leaderboard.Racers == null)
                {
                    Logger.Info($"Event {EventId} has ended");
                    await flagStatus?.EndEvent();
                    state = EventStates.WaitingForStart;
                }
                else // Process lap updates
                {
                    var cf = leaderboard.CurrentFlag;

                    // Test yellow flags
                    var flag = RaceHeroClient.ParseFlag(cf);
                    if ((DateTime.Now - lastFlagChange) > TimeSpan.FromMinutes(1))
                    {
                        if (overrideFlag == null || overrideFlag == RaceHeroClient.Flag.Green)
                        {
                            overrideFlag = RaceHeroClient.Flag.Yellow;
                            lastFlagChange = DateTime.Now;
                        }
                        else if(overrideFlag == RaceHeroClient.Flag.Yellow)
                        {
                            overrideFlag = RaceHeroClient.Flag.Green;
                            lastFlagChange = DateTime.Now;
                        }
                    }

                    await flagStatus?.ProcessFlagStatus(overrideFlag ?? flag, leaderboard.RunId);

                    var logs = new List<Racer>();
                    foreach (var newRacer in leaderboard.Racers)
                    {
                        if (racerStatus.TryGetValue(newRacer.RacerNumber, out var racer))
                        {
                            // Test code for pit stops
                            if (newRacer.RacerNumber.Contains("777"))
                            {
                                if ((DateTime.Now - lastPitStop) > TimeSpan.FromMinutes(4))
                                {
                                    lastPitLap = newRacer.CurrentLap;
                                    lastPitStop = DateTime.Now;
                                }
                                newRacer.LastPitLap = lastPitLap;
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
                    Logger.Trace($"Processing subscriber car lap changes");

                    // Update car data with full current field
                    subscriberCars.ForEach(async c => { await c.ProcessUpdate(latestStatusCopy); });
                    Logger.Trace($"latestStatusCopy {sw.ElapsedMilliseconds}ms");

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

                        await CacheToFuelStatistics(carRaceLaps);
                        Logger.Trace($"CacheToFuelStatistics {sw.ElapsedMilliseconds}ms");

                        if (!readTestFiles)
                        {
                            await LogLapChanges(carRaceLaps);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error polling leaderboard");
            }
        }

        private async Task LogLapChanges(List<CarRaceLap> laps)
        {
            Logger.Trace($"Logging leaderboard laps count={laps.Count}");
            using var db = new RedMist(Config["ConnectionString"]);
            db.CarRaceLaps.AddRange(laps);
            await db.SaveChangesAsync();
        }

        /// <summary>
        /// Publish lap data for the fuel statistics service to consume.
        /// </summary>
        /// <param name="laps"></param>
        private async Task CacheToFuelStatistics(List<CarRaceLap> laps)
        {
            Logger.Trace($"Caching leaderboard for fuel statistics laps count={laps.Count}");
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

        private async Task LogEventPoll(Event @event)
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

        private async Task LogLeaderboardPoll(string eventId, Leaderboard leaderboard)
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

        private async Task PublishEventStatus(RaceHeroEventStatus status, Event rhEvent, Leaderboard leaderboard)
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
