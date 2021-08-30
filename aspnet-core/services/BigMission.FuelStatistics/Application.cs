using BigMission.Cache;
using BigMission.DeviceApp.Shared;
using BigMission.RaceManagement;
using BigMission.ServiceData;
using BigMission.ServiceStatusTools;
using BigMission.TestHelpers;
using Microsoft.Extensions.Configuration;
using NLog;
using NUglify.Helpers;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
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
        private readonly ManualResetEvent serviceBlock = new ManualResetEvent(false);
        private readonly Dictionary<int, Event> eventSubscriptions = new Dictionary<int, Event>();
        private readonly SemaphoreSlim eventSubscriptionLock = new SemaphoreSlim(1, 1);
        private readonly SemaphoreSlim lapCheckLock = new SemaphoreSlim(1, 1);
        private readonly IDataContext dataContext;
        private readonly ITimerHelper eventSubTimer;
        private readonly ITimerHelper lapCheckTimer;
        private readonly IFuelRangeContext fuelRangeContext;
        private readonly ITelemetryConsumer telemetryConsumer;

        public Application(IConfiguration config, ILogger logger, ServiceTracking serviceTracking, IDataContext dataContext, 
            ITimerHelper eventSubTimer, ITimerHelper lapCheckTimer, IFuelRangeContext fuelRangeContext, ITelemetryConsumer telemetryConsumer)
        {
            Config = config;
            Logger = logger;
            ServiceTracking = serviceTracking;
            this.dataContext = dataContext;
            this.eventSubTimer = eventSubTimer;
            this.lapCheckTimer = lapCheckTimer;
            this.fuelRangeContext = fuelRangeContext;
            this.telemetryConsumer = telemetryConsumer;
        }


        public void Run()
        {
            ServiceTracking.Update(ServiceState.STARTING, string.Empty);

            // Load event subscriptions
            eventSubTimer.Create(RunSubscriptionCheck, null, TimeSpan.Zero, TimeSpan.FromMilliseconds(int.Parse(Config["EventSubscriptionCheckMs"])));

            // Process changes from stream and cache them here is the service
            telemetryConsumer.ReceiveData = ReceivedEventCallback;
            telemetryConsumer.Connect();

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
            lapCheckTimer.Create(CheckForEventLaps, null, TimeSpan.FromMilliseconds(250), TimeSpan.FromMilliseconds(500));

            // Start updating service status
            ServiceTracking.Start();
            Logger.Info("Started");
            serviceBlock.WaitOne();
        }

        private async void RunSubscriptionCheck(object obj)
        {
            if (await eventSubscriptionLock.WaitAsync(50))
            {
                try
                {
                    var eventSettings = await LoadEventSettings();
                    Logger.Info($"Loaded {eventSettings.Length} event subscriptions.");
                    var settingEventGrps = eventSettings.GroupBy(s => s.RaceHeroEventId);
                    var eventIds = eventSettings.Select(s => int.Parse(s.RaceHeroEventId)).Distinct();

                    foreach (var settings in eventSettings)
                    {
                        var eventId = int.Parse(settings.RaceHeroEventId);
                        if (!eventSubscriptions.TryGetValue(eventId, out Event e))
                        {
                            e = new Event(settings, Logger, new DateTimeHelper(), dataContext, fuelRangeContext, new TimerHelper());
                            await e.Initialize();
                            eventSubscriptions[eventId] = e;

                            // Clear event in cache
                            await dataContext.ClearCachedEvent(e.RhEventId);
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
                catch (Exception ex)
                {
                    Logger.Error(ex, "Error polling subscriptions");
                }
                finally
                {
                    eventSubscriptionLock.Release();
                }
            }
            else
            {
                Logger.Info("Skipping RunSubscriptionCheck");
            }
        }

        private async Task<RaceEventSettings[]> LoadEventSettings()
        {
            try
            {
                var events = await dataContext.GetEventSettings();

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

        private async void CheckForEventLaps(object o)
        {
            if (await lapCheckLock.WaitAsync(25))
            {
                try
                {
                    var sw = Stopwatch.StartNew();
                    foreach (var evt in eventSubscriptions)
                    {
                        try
                        {
                            var laps = await dataContext.PopEventLaps(evt.Key);
                            Logger.Debug($"Loaded {laps.Count} laps for event {evt.Key}");
                            await evt.Value.UpdateLap(laps);
                        }
                        catch (Exception ex)
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
                    lapCheckLock.Release();
                }
            }
            else
            {
                Logger.Debug("Skipping lap processing");
            }
        }


        private void ReceivedEventCallback(ChannelDataSetDto chDataSet)
        {
            try
            {
                if (!eventSubscriptions.Any())
                {
                    return;
                }

                Event[] events;
                eventSubscriptionLock.Wait();
                try 
                {
                    events = eventSubscriptions.Values.ToArray();
                }
                finally
                {
                    eventSubscriptionLock.Release();
                }

                Parallel.ForEach(events, evt =>
                {
                    evt.UpdateTelemetry(chDataSet);
                });
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Unable to process telemetry data");
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
