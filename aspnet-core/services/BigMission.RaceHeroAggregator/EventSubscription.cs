using BigMission.EntityFrameworkCore;
using BigMission.RaceHeroSdk;
using BigMission.RaceHeroSdk.Models;
using BigMission.RaceManagement;
using BigMission.Teams;
using Microsoft.Extensions.Configuration;
using NLog;
using NUglify.Helpers;
using System;
using System.Collections.Generic;
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

        private List<CarSubscription> subscriberCars = new List<CarSubscription>();


        private readonly Dictionary<int, RaceEventSettings> settings = new Dictionary<int, RaceEventSettings>();
        private RaceHeroClient RhClient { get; set; }

        private Timer waitForStartTimer;
        private readonly object waitForStartLock = new object();
        private Event lastEvent;
        private readonly object lastEventLock = new object();

        private Timer pollLeaderboardTimer;
        private readonly object pollLeaderboardLock = new object();

        /// <summary>
        /// Latest Car status
        /// </summary>
        private Dictionary<string, Racer> racerStatus = new Dictionary<string, Racer>();


        public EventSubscription(ILogger logger, IConfiguration config)
        {
            Logger = logger;
            Config = config;
            RhClient = new RaceHeroClient(Config["RaceHeroUrl"], Config["RaceHeroApiKey"]);
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
                    var cs = new CarSubscription(Logger, Config) { CarId = c.Id, CarNumber = c.Number };
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
                    var eventId = EventId;
                    Logger.Trace($"Polling leaderboard for event {eventId}");
                    var lbTask = RhClient.GetLeaderboard(eventId);
                    lbTask.Wait();
                    var leaderboard = lbTask.Result;

                    // Stop polling when the event is over
                    if (leaderboard == null || leaderboard.Racers == null)
                    {
                        Logger.Info($"Event {eventId} has ended");
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
                    else
                    {
                        var logs = new List<Racer>();
                        Racer[] latestStatusCopy = null;
                        lock (racerStatus)
                        {
                            foreach (var newRacer in leaderboard.Racers)
                            {
                                if (racerStatus.TryGetValue(newRacer.RacerNumber, out Racer racer))
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

                            //// When there was a change, for which there will be logs, 
                            //// copy off the new status for updating the cars
                            //if (logs.Any())
                            //{
                            latestStatusCopy = racerStatus.Values.ToArray();
                            ////}e
                        }

                        if (latestStatusCopy != null)
                        {
                            Logger.Trace($"Processing subscriber car lap changes");
                            // Update car data with full current field
                            lock (subscriberCars)
                            {
                                subscriberCars.ForEach(c => { c.ProcessUpdate(latestStatusCopy); });
                            }
                        }

                        if (logs.Any())
                        {
                            LogLapChanges(logs.ToArray());
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

        private void LogLapChanges(Racer[] laps)
        {
            Logger.Trace($"Logging leaderboard laps count={laps.Length}");
            var eventId = int.Parse(EventId);
            var now = DateTime.UtcNow;

            var logs = new List<CarRaceLap>();
            foreach (var l in laps)
            {
                var log = new CarRaceLap
                {
                    EventId = eventId,
                    CarNumber = l.RacerNumber,
                    Timestamp = now,
                    CurrentLap = l.CurrentLap,
                    ClassName = l.RacerClassName,
                    LastLapTimeSeconds = l.LastLapTimeSeconds,
                    PositionInRun = l.PositionInRun,
                    LastPitLap = l.LastPitLap,
                    PitStops = l.PitStops
                };
                logs.Add(log);
            }
            var cf = new BigMissionDbContextFactory();
            using var db = cf.CreateDbContext(new[] { Config["ConnectionString"] });
            db.CarRacerLaps.AddRange(logs);
            db.SaveChanges();
        }


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
                return default;
            }
            catch (Exception exception)
            {
                return new ValueTask(Task.FromException(exception));
            }
        }
    }
}
