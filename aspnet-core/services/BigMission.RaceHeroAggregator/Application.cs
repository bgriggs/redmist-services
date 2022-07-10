using BigMission.Cache.Models;
using BigMission.Database;
using BigMission.Database.Helpers;
using BigMission.Database.Models;
using BigMission.RaceHeroSdk;
using BigMission.RaceHeroTestHelpers;
using BigMission.ServiceStatusTools;
using BigMission.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using NLog;
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
        private ServiceTracking ServiceTracking { get; }
        private IDateTimeHelper DateTime { get; }
        private IRaceHeroClient RhClient { get; set; }

        private readonly Dictionary<string, EventSubscription> eventSubscriptions = new();
        private readonly ConnectionMultiplexer cacheMuxer;
        private readonly ISimulateSettingsService simulateSettingsService;


        public Application(ILogger logger, IConfiguration config, ServiceTracking serviceTracking, IDateTimeHelper dateTime, ISimulateSettingsService simulateSettingsService)
        {
            Logger = logger;
            Config = config;
            ServiceTracking = serviceTracking;
            DateTime = dateTime;
            this.simulateSettingsService = simulateSettingsService;

            cacheMuxer = ConnectionMultiplexer.Connect(Config["RedisConn"]);
            var readTestFiles = bool.Parse(Config["ReadTestFiles"]);

            if (readTestFiles)
            {
                RhClient = new FileRHClient(Config["EventTestFilesDir"], Config["LeaderboardTestFilesDir"]);
            }
            else
            {
                RhClient = new RaceHeroClient(Config["RaceHeroUrl"], Config["RaceHeroApiKey"]);
            }
        }


        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            ServiceTracking.Update(ServiceState.STARTING, string.Empty);
            var eventSubInterval = TimeSpan.FromMilliseconds(int.Parse(Config["EventSubscriptionCheckMs"]));
            var waitForStartInterval = TimeSpan.FromMilliseconds(int.Parse(Config["WaitForStartTimer"]));
            var eventPollInterval = TimeSpan.FromMilliseconds(int.Parse(Config["EventPollTimer"]));
            var minInterval = new[] { eventSubInterval, waitForStartInterval, eventPollInterval }.Min();

            var lastEventSubCheck = System.DateTime.MinValue;

            while (!stoppingToken.IsCancellationRequested)
            {
                var eventSubCheckDiff = DateTime.UtcNow - lastEventSubCheck;
                if (eventSubCheckDiff >= eventSubInterval)
                {
                    await RunSubscriptionCheck();
                    lastEventSubCheck = DateTime.UtcNow;
                }

                var updateEventTasks = new List<Task>();
                foreach(var eventSub in eventSubscriptions.Values)
                {
                    var et = eventSub.UpdateEvent();
                    updateEventTasks.Add(et);
                }

                await Task.WhenAll(updateEventTasks);
                await Task.Delay(minInterval, stoppingToken);
            }
        }

        //public void Run()
        //{
        //    // Load event subscriptions
        //    eventSubscriptionTimer = new Timer(RunSubscriptionCheck, null, 0, int.Parse(Config["EventSubscriptionCheckMs"]));

        //    //string eventId = "3134";
        //    ////var client = new RestClient("https://api.racehero.io/v1/");
        //    ////client.Authenticator = new HttpBasicAuthenticator(Config["RaceHeroApiKey"], "");

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
        // user have to time box the events in single day or two chuncks.  Once we have that, we
        // can check for Live status to start getting car and driver data.
        private async Task RunSubscriptionCheck()
        {
            try
            {
                var settings = await RaceEventSettings.LoadCurrentEventSettings(Config["ConnectionString"], DateTime.UtcNow);
                Logger.Info($"Loaded {settings.Length} event subscriptions.");
                var settingEventGrps = settings.GroupBy(s => s.RaceHeroEventId);
                var eventIds = settings.Select(s => s.RaceHeroEventId).Distinct();

                // Add and update
                foreach (var settingGrg in settingEventGrps)
                {
                    if (!eventSubscriptions.TryGetValue(settingGrg.Key, out EventSubscription subscription))
                    {
                        subscription = new EventSubscription(Logger, Config, cacheMuxer, RhClient, DateTime, simulateSettingsService);
                        eventSubscriptions[settingGrg.Key] = subscription;
                        Logger.Info($"Adding event subscription for event ID '{settingGrg.Key}' and starting polling.");
                    }
                    await subscription.UpdateSetting(settingGrg.ToArray());
                }

                // Remove deleted
                var expiredEvents = eventSubscriptions.Keys.Except(eventIds);
                foreach (var e in expiredEvents)
                {
                    Logger.Info($"Removing event subscription {e}");
                    eventSubscriptions.Remove(e, out EventSubscription sub);
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error polling subscriptions");
            }
        }

        #endregion
    }
}