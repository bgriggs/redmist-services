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
        public IDateTimeHelper DateTime { get; }

        public int[] EventIds
        {
            get { return eventSubscriptions.Keys.ToArray(); }
        }

        private readonly TimeSpan subCheckInterval;
        private readonly TimeSpan commitInterval;
        private readonly IDataContext dataContext;
        private readonly IFuelRangeContext fuelRangeContext;
        private readonly IFlagContext flagContext;
        private readonly Dictionary<int, Event> eventSubscriptions = new();


        public EventService(IConfiguration configuration, ILogger logger, IDataContext dataContext, IFuelRangeContext fuelRangeContext, 
            IFlagContext flagContext, IDateTimeHelper dateTime)
        {
            Logger = logger;
            this.dataContext = dataContext;
            this.fuelRangeContext = fuelRangeContext;
            this.flagContext = flagContext;
            DateTime = dateTime;
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
                    if ((DateTime.UtcNow - lastSubCheck) >= subCheckInterval)
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
                                e = new Event(settings, Logger, DateTime, dataContext, fuelRangeContext, flagContext);
                                await e.Initialize();
                                eventSubscriptions[eventId] = e;
                                
                                // Clear event in cache since an event can have multiple runs
                                await dataContext.ClearCachedEvent(e.RhEventId);
                                await fuelRangeContext.ClearCachedTeamStints(settings.TenantId);
                            }
                        }

                        // Remove deleted
                        var expiredEvents = eventSubscriptions.Keys.Except(eventIds);
                        foreach(var ee in expiredEvents)
                        {
                            Logger.Info($"Removing event subscription {ee}");
                            eventSubscriptions.Remove(ee);
                        }

                        lastSubCheck = DateTime.UtcNow;
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
            return Array.Empty<RaceEventSetting>();
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
                    Logger.Error(ex, "Error processing stint override.");
                }
            });

            await Task.WhenAll(eventTasks);
        }
    }
}
