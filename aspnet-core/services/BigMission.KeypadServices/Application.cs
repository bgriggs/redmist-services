using BigMission.Cache.Models;
using BigMission.DeviceApp.Shared;
using BigMission.ServiceData;
using BigMission.ServiceStatusTools;
using BigMission.TestHelpers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Newtonsoft.Json;
using NLog;
using StackExchange.Redis;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace BigMission.KeypadServices
{
    class Application : BackgroundService
    {
        private IConfiguration Config { get; }
        private ILogger Logger { get; }
        private ServiceTracking ServiceTracking { get; }
        public IDateTimeHelper DateTime { get; }

        private readonly ConnectionMultiplexer cacheMuxer;


        public Application(IConfiguration config, ILogger logger, ServiceTracking serviceTracking, IDateTimeHelper dateTimeHelper)
        {
            Config = config;
            Logger = logger;
            ServiceTracking = serviceTracking;
            DateTime = dateTimeHelper;
            cacheMuxer = ConnectionMultiplexer.Connect(Config["RedisConn"]);
        }


        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            ServiceTracking.Update(ServiceState.STARTING, string.Empty);

            var sub = cacheMuxer.GetSubscriber();
            await sub.SubscribeAsync(Consts.CAR_KEYPAD_SUB, async (channel, message) =>
            {
                await HandleKeypadStatus(message);
            });

            Logger.Info("Started");
        }

        private async Task HandleKeypadStatus(RedisValue value)
        {
            var sw = Stopwatch.StartNew();
            var keypadStatus = JsonConvert.DeserializeObject<KeypadStatusDto>(value);

            Logger.Trace($"Received keypad status: {keypadStatus.DeviceAppId} Count={keypadStatus.LedStates.Count}");

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

            Logger.Trace($"Cached new keypad status for device: {keypadStatus.DeviceAppId}");
            Logger.Trace($"Processed status in {sw.ElapsedMilliseconds}ms");
        }
    }
}
