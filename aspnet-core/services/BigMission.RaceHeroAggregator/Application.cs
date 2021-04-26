﻿using BigMission.EntityFrameworkCore;
using BigMission.RaceManagement;
using BigMission.ServiceData;
using BigMission.ServiceStatusTools;
using Microsoft.Extensions.Configuration;
using NLog;
using NUglify.Helpers;
using StackExchange.Redis;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Twilio.Rest.Api.V2010.Account;

namespace BigMission.RaceHeroAggregator
{
    /// <summary>
    /// Connect to race hero API and get event and race status for events that users have subscribed to.
    /// </summary>
    class Application
    {
        private ILogger Logger { get; }
        private IConfiguration Config { get; }
        private ServiceTracking ServiceTracking { get; }

        private readonly Dictionary<string, EventSubscription> eventSubscriptions = new Dictionary<string, EventSubscription>();

        private Timer eventSubscriptionTimer;
        private readonly object eventSubscriptionCheckLock = new object();
        private readonly ManualResetEvent serviceBlock = new ManualResetEvent(false);
        private readonly ConnectionMultiplexer cacheMuxer;


        public Application(ILogger logger, IConfiguration config, ServiceTracking serviceTracking)
        {
            Logger = logger;
            Config = config;
            ServiceTracking = serviceTracking;
            cacheMuxer = ConnectionMultiplexer.Connect(Config["RedisConn"]);
        }


        public void Run()
        {
            ServiceTracking.Update(ServiceState.STARTING, string.Empty);

            // Load event subscriptions
            eventSubscriptionTimer = new Timer(RunSubscriptionCheck, null, 0, int.Parse(Config["EventSubscriptionCheckMs"]));

            //string eventId = "3134";
            ////var client = new RestClient("https://api.racehero.io/v1/");
            ////client.Authenticator = new HttpBasicAuthenticator(Config["RaceHeroApiKey"], "");

            //var task = RhClient.GetEvent(eventId);
            //task.Wait();
            //Console.WriteLine($"{task.Result.Name} IsLive={task.Result.IsLive}");

            //var lt = RhClient.GetLeaderboard(eventId);
            //lt.Wait();
            //Console.WriteLine($"{lt.Result.Name}");

            // Start updating service status
            ServiceTracking.Start();
            Logger.Info("Started");
            serviceBlock.WaitOne();
        }

        #region Event Subscription Management

        // Keep track of valid and active subscriptions to race hero events.  We do not want to 
        // unconditionally poll the events for the users because the API will cut us off.  The
        // user have to time box the events in single day or two chuncks.  Once we have that, we
        // can check for Live status to start getting car and driver data.

        private void RunSubscriptionCheck(object obj)
        {
            if (Monitor.TryEnter(eventSubscriptionCheckLock))
            {
                try
                {
                    var settings = LoadEventSettings();
                    Logger.Info($"Loaded {settings.Length} event subscriptions.");
                    var settingEventGrps = settings.GroupBy(s => s.RaceHeroEventId);
                    var eventIds = settings.Select(s => s.RaceHeroEventId).Distinct();

                    lock (eventSubscriptions)
                    {
                        // Add and update
                        foreach (var settingGrg in settingEventGrps)
                        {
                            if (!eventSubscriptions.TryGetValue(settingGrg.Key, out EventSubscription subscription))
                            {
                                subscription = new EventSubscription(Logger, Config, cacheMuxer);
                                eventSubscriptions[settingGrg.Key] = subscription;
                                subscription.Start();
                                Logger.Info($"Adding event subscription for event ID '{settingGrg.Key}' and starting polling.");
                            }
                            subscription.UpdateSetting(settingGrg.ToArray());
                        }

                        // Remove deleted
                        var expiredEvents = eventSubscriptions.Keys.Except(eventIds);
                        expiredEvents.ForEach(e => 
                        {
                            Logger.Info($"Removing event subscription {e}");
                            if (eventSubscriptions.Remove(e, out EventSubscription sub))
                            {
                                try
                                {
                                    sub.Dispose();
                                }
                                catch (Exception ex)
                                {
                                    Logger.Error(ex, $"Error stopping event subscription {sub.EventId}.");
                                }
                            }
                        });
                    }
                }
                catch (Exception ex)
                {
                    Logger.Error(ex, "Error polling subscriptions");
                }
                finally
                {
                    Monitor.Exit(eventSubscriptionCheckLock);
                }
            }
            else
            {
                Logger.Info("Skipping RunSubscriptionCheck");
            }
        }

        private RaceEventSettings[] LoadEventSettings()
        {
            try
            {
                var cf = new BigMissionDbContextFactory();
                using var db = cf.CreateDbContext(new[] { Config["ConnectionString"] });
                
                var events = db.RaceEventSettings
                    .Where(s => !s.IsDeleted && s.IsEnabled)
                    .ToArray();

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
                Logger.Error(ex, "Unable to save logs");
            }
            return new RaceEventSettings[0];
        }

        #endregion
    }
}
