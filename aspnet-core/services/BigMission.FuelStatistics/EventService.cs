using BigMission.Cache.Models.Flags;
using BigMission.Cache.Models.FuelRange;
using BigMission.Database.Models;
using BigMission.DeviceApp.Shared;
using BigMission.FuelStatistics.FuelRange;
using BigMission.TestHelpers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using NLog;
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
        private readonly IFlagContext flagContext;
        private readonly Dictionary<int, Event> eventSubscriptions = new Dictionary<int, Event>();


        public EventService(IConfiguration configuration, ILogger logger, IDataContext dataContext, IFuelRangeContext fuelRangeContext, 
            IDateTimeHelper dateTimeHelper, IFlagContext flagContext)
        {
            Logger = logger;
            this.dataContext = dataContext;
            this.fuelRangeContext = fuelRangeContext;
            this.dateTimeHelper = dateTimeHelper;
            this.flagContext = flagContext;
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
                                e = new Event(settings, Logger, new DateTimeHelper(), dataContext, fuelRangeContext, flagContext);
                                await e.Initialize();
                                eventSubscriptions[eventId] = e;

                                // Clear event in cache
                                await dataContext.ClearCachedEvent(e.RhEventId);
                            }
                        }

                        // Remove deleted
                        var expiredEvents = eventSubscriptions.Keys.Except(eventIds);
                        foreach(var ee in expiredEvents)
                        {
                            Logger.Info($"Removing event subscription {ee}");
                            eventSubscriptions.Remove(ee);
                        }

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

                await Task.Delay(commitInterval, stoppingToken);
            }
        }

        private async Task<RaceEventSetting[]> LoadEventSettings()
        {
            try
            {
                var events = await dataContext.GetEventSettings();

                // Filter by subscription time
                var activeSubscriptions = new List<RaceEventSetting>();
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
            return new RaceEventSetting[0];
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

        public async Task ProcessStintOverride(RangeUpdate stint)
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
