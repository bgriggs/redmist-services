﻿using BigMission.Cache.Models;
using BigMission.DeviceApp.Shared;
using BigMission.ServiceStatusTools;
using BigMission.TestHelpers;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using StackExchange.Redis;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace BigMission.KeypadServices
{
    class Application : BackgroundService
    {
        private ILogger Logger { get; }
        public IDateTimeHelper DateTime { get; }

        private readonly IConnectionMultiplexer cacheMuxer;
        private readonly StartupHealthCheck startup;

        public Application(IConnectionMultiplexer cacheMuxer, ILoggerFactory loggerFactory, StartupHealthCheck startup, IDateTimeHelper dateTimeHelper)
        {
            this.cacheMuxer = cacheMuxer;
            this.startup = startup;
            Logger = loggerFactory.CreateLogger(GetType().Name);
            DateTime = dateTimeHelper;
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

            var sub = cacheMuxer.GetSubscriber();
            await sub.SubscribeAsync(Consts.CAR_KEYPAD_SUB, async (channel, message) =>
            {
                await HandleKeypadStatus(message);
            });

            Logger.LogInformation("Started");
            startup.SetStarted();
        }

        private async Task HandleKeypadStatus(RedisValue value)
        {
            var sw = Stopwatch.StartNew();
            var keypadStatus = JsonConvert.DeserializeObject<KeypadStatusDto>(value);

            Logger.LogTrace($"Received keypad status: {keypadStatus.DeviceAppId} Count={keypadStatus.LedStates.Count}");

            // Reset time to server time to prevent timeouts when data is being updated.
            keypadStatus.Timestamp = DateTime.UtcNow;

            var db = cacheMuxer.GetDatabase();

            var key = string.Format(Consts.KEYPAD_STATUS, keypadStatus.DeviceAppId);
            var buttonEntries = new List<HashEntry>();
            foreach (var bStatus in keypadStatus.LedStates)
            {
                var ledjson = JsonConvert.SerializeObject(bStatus);
                var h = new HashEntry(bStatus.ButtonNumber, ledjson);
                buttonEntries.Add(h);
            }

            await db.HashSetAsync(key, buttonEntries.ToArray());

            Logger.LogTrace($"Cached new keypad status for device: {keypadStatus.DeviceAppId}");
            Logger.LogTrace($"Processed status in {sw.ElapsedMilliseconds}ms");
        }
    }
}