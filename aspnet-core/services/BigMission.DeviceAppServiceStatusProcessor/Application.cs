using BigMission.Cache;
using BigMission.Cache.Models;
using BigMission.CommandTools;
using BigMission.CommandTools.Models;
using BigMission.EntityFrameworkCore;
using BigMission.ServiceData;
using BigMission.ServiceStatusTools;
using BigMission.TestHelpers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Newtonsoft.Json;
using NLog;
using StackExchange.Redis;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace BigMission.DeviceAppServiceStatusProcessor
{
    /// <summary>
    /// Processes application status from the in car apps. (not channel status)
    /// </summary>
    class Application : BackgroundService
    {
        private IConfiguration Config { get; }
        private ILogger Logger { get; }
        private ServiceTracking ServiceTracking { get; }
        private IDateTimeHelper DateTime { get; }
        private AppCommands Commands { get; }
        private readonly ConnectionMultiplexer cacheMuxer;
        private readonly DeviceAppContext deviceAppContext;


        public Application(IConfiguration config, ILogger logger, ServiceTracking serviceTracking, IDateTimeHelper dateTime)
        {
            Config = config;
            Logger = logger;
            ServiceTracking = serviceTracking;
            DateTime = dateTime;
            var serviceId = new Guid(Config["ServiceId"]);
            Commands = new AppCommands(serviceId, Config["ApiKey"], Config["ApiUrl"], logger);
            cacheMuxer = ConnectionMultiplexer.Connect(Config["RedisConn"]);
            deviceAppContext = new DeviceAppContext(cacheMuxer);
        }


        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            ServiceTracking.Update(ServiceState.STARTING, string.Empty);

            var cf = new BigMissionDbContextFactory();
            using var db = cf.CreateDbContext(new[] { Config["ConnectionString"] });
            deviceAppContext.WarmUpDeviceConfigIds(db);

            var sub = cacheMuxer.GetSubscriber();
            await sub.SubscribeAsync(Consts.HEARTBEAT_CH, async (channel, message) =>
            {
                await HandleHeartbeat(message);
            });

            Logger.Info("Started");
        }

        private async Task HandleHeartbeat(RedisValue value)
        {
            var heartbeatData = JsonConvert.DeserializeObject<DeviceApp.Shared.DeviceAppHeartbeat>(value);
            Logger.Debug($"Received HB from: '{heartbeatData.DeviceAppId}'");

            var cf = new BigMissionDbContextFactory();
            using var db = cf.CreateDbContext(new[] { Config["ConnectionString"] });

            if (heartbeatData.DeviceAppId == 0)
            {
                var deviceApp = db.DeviceAppConfig.FirstOrDefault(d => d.DeviceAppKey == heartbeatData.DeviceKey);
                if (deviceApp != null)
                {
                    heartbeatData.DeviceAppId = deviceApp.Id;
                }
                else
                {
                    Logger.Debug($"No app configured with key {heartbeatData.DeviceKey}");
                }
            }

            if (heartbeatData.DeviceAppId > 0)
            {
                // Update heartbeat
                await CommitHeartbeat(heartbeatData, value);

                // Check log level
                await CheckDeviceAppLogLevel(heartbeatData);
            }
        }

        /// <summary>
        /// Save the latest timestamp update to the database.
        /// </summary>
        /// <param name="hb"></param>
        /// <param name="db"></param>
        private async Task CommitHeartbeat(DeviceApp.Shared.DeviceAppHeartbeat hb, string hbjson)
        {
            Logger.Trace($"Saving heartbeat: {hb.DeviceAppId}");

            var cache = cacheMuxer.GetDatabase();
            await cache.HashSetAsync(Consts.DEVICEAPP_STATUS, new RedisValue(hb.DeviceAppId.ToString()), hbjson);
        }

        /// <summary>
        /// Determine if there is a user log level override set for the device.  If so, send it to the device.
        /// </summary>
        /// <param name="hb"></param>
        /// <param name="deviceAppKey"></param>
        private async Task CheckDeviceAppLogLevel(DeviceApp.Shared.DeviceAppHeartbeat hb)
        {
            var cache = cacheMuxer.GetDatabase();
            var key = string.Format(Consts.DEVICEAPP_LOG_DESIRED_LEVEL, hb.DeviceKey);
            var rv = await cache.StringGetAsync(key);
            if (rv.HasValue)
            {
                LogLevel desiredLevel;
                try
                {
                    // This will throw ArgumentException if value is not valid and bail out
                    desiredLevel = LogLevel.FromString(rv.ToString());
                    var currentLevel = LogLevel.FromString(hb.LogLevel);
                    if (desiredLevel != currentLevel)
                    {
                        Logger.Debug($"Sending log level update for device {hb.DeviceKey}");
                        var cmd = new Command
                        {
                            CommandType = CommandTypes.SET_LOG_LEVEL,
                            Data = desiredLevel.Name,
                            DestinationId = hb.DeviceKey.ToString(),
                            Timestamp = DateTime.UtcNow
                        };
                        await Commands.SendCommandAsync(cmd, new Guid(cmd.DestinationId));
                    }
                }
                catch (ArgumentException) { }
            }
        }
    }
}
