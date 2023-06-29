using BigMission.Database;
using BigMission.Database.Helpers;
using BigMission.RaceHeroSdk;
using BigMission.RaceHeroTestHelpers;
using BigMission.ServiceStatusTools;
using BigMission.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace BigMission.RaceHeroAggregator
{
    /// <summary>
    /// Connect to race hero API and get event and race status for events that users have subscribed to.
    /// </summary>
    class Application : BackgroundService
    {
        private ILogger Logger { get; }
        private IConfiguration Config { get; }
        private IDateTimeHelper DateTime { get; }
        private IRaceHeroClient RhClient { get; set; }

        private readonly Dictionary<string, EventSubscription> eventSubscriptions = new();
        private readonly ILoggerFactory loggerFactory;
        private readonly IConnectionMultiplexer cacheMuxer;
        private readonly ISimulateSettingsService simulateSettingsService;
        private readonly StartupHealthCheck startup;
        private readonly IDbContextFactory<RedMist> dbFactory;

        public Application(ILoggerFactory loggerFactory, IConfiguration config, IConnectionMultiplexer cacheMuxer, IDateTimeHelper dateTime,
            ISimulateSettingsService simulateSettingsService, StartupHealthCheck startup, IDbContextFactory<RedMist> dbFactory)
        {
            Logger = loggerFactory.CreateLogger(GetType().Name);
            this.loggerFactory = loggerFactory;
            Config = config;
            DateTime = dateTime;
            this.simulateSettingsService = simulateSettingsService;
            this.startup = startup;
            this.dbFactory = dbFactory;
            this.cacheMuxer = cacheMuxer;
            var readTestFiles = bool.Parse(Config["ReadTestFiles"]);

            if (readTestFiles)
            {
                RhClient = new FileRHClient(Config["EventTestFilesDir"], Config["LeaderboardTestFilesDir"]);
            }
            else
            {
                RhClient = new RaceHeroClient(Config["RACEHEROURL"], Config["RACEHEROAPIKEY"]);
            }
        }


        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            Logger.LogInformation("Waiting for dependencies...");
            while (!stoppingToken.IsCancellationRequested)
            {
                if (await startup.CheckDependencies())
                    break;
                await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
            }
            startup.Start();

            var eventSubInterval = TimeSpan.FromMilliseconds(int.Parse(Config["EVENTSUBSCRIPTIONCHECKMS"]));
            var waitForStartInterval = TimeSpan.FromMilliseconds(int.Parse(Config["WAITFORSTARTTIMER"]));
            var eventPollInterval = TimeSpan.FromMilliseconds(int.Parse(Config["EVENTPOLLTIMER"]));
            var minInterval = new[] { eventSubInterval, waitForStartInterval, eventPollInterval }.Min();

            var lastEventSubCheck = System.DateTime.MinValue;
            startup.SetStarted();

            while (!stoppingToken.IsCancellationRequested)
            {
                var eventSubCheckDiff = DateTime.UtcNow - lastEventSubCheck;
                if (eventSubCheckDiff >= eventSubInterval)
                {
                    await RunSubscriptionCheck(stoppingToken);
                    lastEventSubCheck = DateTime.UtcNow;
                }

                var updateEventTasks = new List<Task>();
                foreach(var eventSub in eventSubscriptions.Values)
                {
                    var et = eventSub.UpdateEventAsync();
                    updateEventTasks.Add(et);
                }

                await Task.WhenAll(updateEventTasks);
                await Task.Delay(minInterval, stoppingToken);
            }
        }

        //public void Run()
        //{
        //    // Load event subscriptions
        //    eventSubscriptionTimer = new Timer(RunSubscriptionCheck, null, 0, int.Parse(Config["EVENTSUBSCRIPTIONCHECKMS"]));

        //    //string eventId = "3134";
        //    ////var client = new RestClient("https://api.racehero.io/v1/");
        //    ////client.Authenticator = new HttpBasicAuthenticator(Config["RACEHEROAPIKEY"], "");

        //    //var task = RhClient.GetEvent(eventId);
        //    //task.Wait();
        //    //Console.WriteLine($"{task.Result.Name} IsLive={task.Result.IsLive}");

        //    //var lt = RhClient.GetLeaderboard(eventId);
        //    //lt.Wait();
        //    //Console.WriteLine($"{lt.Result.Name}");

        //    // Start updating service status
        //    ServiceTracking.Start();
        //    Logger.Info("Started");
        //    serviceBlock.WaitOne();
        //}

        #region Event Subscription Management

        // Keep track of valid and active subscriptions to race hero events.  We do not want to 
        // unconditionally poll the events for the users because the API will cut us off.  The
        // user have to time box the events in single day or two chunks.  Once we have that, we
        // can check for Live status to start getting car and driver data.
        private async Task RunSubscriptionCheck(CancellationToken stoppingToken)
        {
            try
            {
                var settings = await RaceEventSettings.LoadCurrentEventSettings(dbFactory, DateTime.UtcNow, stoppingToken);
                Logger.LogInformation($"Loaded {settings.Length} event subscriptions.");
                var settingEventGrps = settings.GroupBy(s => s.RaceHeroEventId);
                var eventIds = settings.Select(s => s.RaceHeroEventId).Distinct();

                // Add and update
                foreach (var settingGrg in settingEventGrps)
                {
                    if (!eventSubscriptions.TryGetValue(settingGrg.Key, out EventSubscription subscription))
                    {
                        subscription = new EventSubscription(loggerFactory, Config, cacheMuxer, RhClient, DateTime, simulateSettingsService, dbFactory);
                        eventSubscriptions[settingGrg.Key] = subscription;
                        Logger.LogInformation($"Adding event subscription for event ID '{settingGrg.Key}' and starting polling.");
                    }
                    await subscription.UpdateSettingAsync(settingGrg.ToArray());
                }

                // Remove deleted
                var expiredEvents = eventSubscriptions.Keys.Except(eventIds);
                foreach (var e in expiredEvents)
                {
                    Logger.LogInformation($"Removing event subscription {e}");
                    eventSubscriptions.Remove(e, out EventSubscription sub);
                }
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Error polling subscriptions");
            }
        }

        #endregion
    }
}