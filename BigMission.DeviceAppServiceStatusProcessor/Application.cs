using BigMission.Cache.Models;
using BigMission.CommandTools;
using BigMission.CommandTools.Models;
using BigMission.Database;
using BigMission.ServiceStatusTools;
using BigMission.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
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
        private ILogger Logger { get; }
        private IDateTimeHelper DateTime { get; }
        private readonly IConnectionMultiplexer cacheMuxer;
        private readonly IDbContextFactory<RedMist> dbFactory;
        private readonly StartupHealthCheck startup;
        private readonly IAppCommandsFactory commandsFactory;

        public Application(ILoggerFactory loggerFactory, IDateTimeHelper dateTime, IConnectionMultiplexer cache, IDbContextFactory<RedMist> dbFactory, StartupHealthCheck startup, IAppCommandsFactory commandsFactory)
        {
            Logger = loggerFactory.CreateLogger(GetType().Name);
            DateTime = dateTime;
            this.dbFactory = dbFactory;
            this.startup = startup;
            this.commandsFactory = commandsFactory;
            cacheMuxer = cache;
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
            await startup.Start();

            var sub = cacheMuxer.GetSubscriber();
            await sub.SubscribeAsync(Consts.HEARTBEAT_CH, async (channel, message) =>
            {
                await HandleHeartbeat(message, stoppingToken);
            });

            // Watch for changes in device app configuration such as channels
            var commands = commandsFactory.CreateAppCommands();
            await sub.SubscribeAsync(Consts.CAR_CONFIG_CHANGED_SUB, async (channel, message) =>
            {
                if (int.TryParse(message, out int deviceId))
                {
                    Logger.LogInformation("Car device app configuration notification received.  Sending command to restart car device application.");
                    using var db = await dbFactory.CreateDbContextAsync(stoppingToken);
                    var deviceApp = db.DeviceAppConfigs.FirstOrDefault(d => d.Id == deviceId);
                    if (deviceApp != null)
                    {
                        var cmd = new Command
                        {
                            CommandType = CommandTypes.RESTART,
                            DestinationId = deviceApp.DeviceAppKey.ToString(),
                            Timestamp = DateTime.UtcNow
                        };
                        await commands.SendCommandAsync(cmd, new Guid(cmd.DestinationId));
                    }
                    else
                    {
                        Logger.LogWarning($"Unable to prompt device app configuration because of missing device ID for {deviceId}");
                    }
                }
                else
                {
                    Logger.LogWarning($"Unable to prompt device app configuration because of missing device ID: {message}");
                }
            });

            Logger.LogInformation("Started");
            await startup.SetStarted();
        }

        private async Task HandleHeartbeat(RedisValue value, CancellationToken stoppingToken)
        {
            var heartbeatData = JsonConvert.DeserializeObject<DeviceApp.Shared.DeviceAppHeartbeat>(value);
            Logger.LogDebug($"Received HB from: '{heartbeatData.DeviceAppId}'");

            using var db = await dbFactory.CreateDbContextAsync(stoppingToken);

            if (heartbeatData.DeviceAppId == 0)
            {
                var deviceApp = db.DeviceAppConfigs.FirstOrDefault(d => d.DeviceAppKey == heartbeatData.DeviceKey);
                if (deviceApp != null)
                {
                    heartbeatData.DeviceAppId = deviceApp.Id;
                }
                else
                {
                    Logger.LogDebug($"No app configured with key {heartbeatData.DeviceKey}");
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
            Logger.LogTrace($"Saving heartbeat: {hb.DeviceAppId}");

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
                //LogLevel desiredLevel;
                //try
                //{
                //    // This will throw ArgumentException if value is not valid and bail out
                //    desiredLevel = LogLevel.FromString(rv.ToString());
                //    var currentLevel = LogLevel.FromString(hb.LogLevel);
                //    if (desiredLevel != currentLevel)
                //    {
                //        Logger.LogDebug($"Sending log level update for device {hb.DeviceKey}");
                //        var cmd = new Command
                //        {
                //            CommandType = CommandTypes.SET_LOG_LEVEL,
                //            Data = desiredLevel.Name,
                //            DestinationId = hb.DeviceKey.ToString(),
                //            Timestamp = DateTime.UtcNow
                //        };
                //        await Commands.SendCommandAsync(cmd, new Guid(cmd.DestinationId));
                //    }
                //}
                //catch (ArgumentException) { }
            }
        }
    }
}
