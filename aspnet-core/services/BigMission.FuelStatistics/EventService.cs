using BigMission.Cache;
using BigMission.Cache.FuelRange;
using BigMission.DeviceApp.Shared;
using BigMission.FuelStatistics.FuelRange;
using BigMission.RaceManagement;
using BigMission.TestHelpers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using NLog;
using NUglify.Helpers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace BigMission.FuelStatistics
{
    public class EventService : BackgroundService, ILapConsumer, ICarTelemetryConsumer, IStintOverrideConsumer
    {
        private ILogger Logger { get; set; }

        public int[] EventIds
        {
            get { return eventSubscriptions.Keys.ToArray(); }
        }

        private readonly TimeSpan subCheckInterval;
        private readonly TimeSpan commitInterval;
        private readonly IDataContext dataContext;
        private readonly IFuelRangeContext fuelRangeContext;
        private readonly IDateTimeHelper dateTimeHelper;
        private readonly Dictionary<int, Event> eventSubscriptions = new Dictionary<int, Event>();


        public EventService(IConfiguration configuration, ILogger logger, IDataContext dataContext, IFuelRangeContext fuelRangeContext, IDateTimeHelper dateTimeHelper)
        {
            Logger = logger;
            this.dataContext = dataContext;
            this.fuelRangeContext = fuelRangeContext;
            this.dateTimeHelper = dateTimeHelper;
            subCheckInterval = TimeSpan.FromMilliseconds(int.Parse(configuration["EventSubscriptionCheckMs"]));
            commitInterval = TimeSpan.FromMilliseconds(int.Parse(configuration["EventCommitMs"]));
        }


        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            DateTime lastSubCheck = default;
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    if ((dateTimeHelper.UtcNow - lastSubCheck) >= subCheckInterval)
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

                        lastSubCheck = dateTimeHelper.UtcNow;
                    }

                    // Process event stint changes
                    var commitTasks = eventSubscriptions.Values.Select(async (e) =>
                    {
                        await e.CommitFuelRangeStintUpdates();
                    });

                    await Task.WhenAll(commitTasks);
                }
                catch (Exception ex)
                {
                    Logger.Error(ex, "Error polling subscriptions");
                }

                await Task.Delay(commitInterval);
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

        public async Task UpdateLaps(int eventId, List<Lap> laps)
        {
            if (eventSubscriptions.TryGetValue(eventId, out Event evt))
            {
                await evt.UpdateLap(laps);
            }
        }

        public async Task UpdateTelemetry(ChannelDataSetDto telem)
        {
            var eventTasks = eventSubscriptions.Values.Select(async (evt) =>
            {
                try
                {
                    await evt.UpdateTelemetry(telem);
                }
                catch (Exception ex)
                {
                    Logger.Error(ex, "Error processing car telemetry.");
                }
            });

            await Task.WhenAll(eventTasks);
        }

        public async Task ProcessStintOverride(FuelRangeUpdate stint)
        {
            var eventTasks = eventSubscriptions.Values.Select(async (evt) =>
            {
                try
                {
                    await evt.OverrideStint(stint);
                }
                catch (Exception ex)
                {
                    Logger.Error(ex, "Error processing car telemetry.");
                }
            });

            await Task.WhenAll(eventTasks);
        }
    }
}
