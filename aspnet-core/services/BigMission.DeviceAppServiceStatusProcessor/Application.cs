using Azure.Messaging.EventHubs.Consumer;
using BigMission.Cache;
using BigMission.Cache.Models;
using BigMission.CommandTools;
using BigMission.CommandTools.Models;
using BigMission.EntityFrameworkCore;
using BigMission.ServiceData;
using BigMission.ServiceStatusTools;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using NLog;
using StackExchange.Redis;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace BigMission.DeviceAppServiceStatusProcessor
{
    /// <summary>
    /// Processes application status from the in car apps. (not channel status)
    /// </summary>
    class Application
    {
        private IConfiguration Config { get; }
        private ILogger Logger { get; }
        private ServiceTracking ServiceTracking { get; }
        private AppCommands Commands { get; }
        private readonly EventHubHelpers ehReader;
        private readonly ManualResetEvent serviceBlock = new ManualResetEvent(false);
        private readonly ConnectionMultiplexer cacheMuxer;
        private readonly int _maxListLength = 1000;
        private readonly TimeSpan _lengthTrim = TimeSpan.FromSeconds(30);
        private readonly Dictionary<string, DateTime> _lastTrims = new Dictionary<string, DateTime>();
        private readonly DeviceAppContext deviceAppContext;


        public Application(IConfiguration config, ILogger logger, ServiceTracking serviceTracking)
        {
            Config = config;
            Logger = logger;
            ServiceTracking = serviceTracking;
            var serviceId = new Guid(Config["ServiceId"]);
            Commands = new AppCommands(serviceId, Config["ApiKey"], Config["ApiUrl"], logger);
            ehReader = new EventHubHelpers(logger);
            cacheMuxer = ConnectionMultiplexer.Connect(Config["RedisConn"]);
            deviceAppContext = new DeviceAppContext(cacheMuxer);
        }


        public void Run()
        {
            ServiceTracking.Update(ServiceState.STARTING, string.Empty);

            var cf = new BigMissionDbContextFactory();
            using var db = cf.CreateDbContext(new[] { Config["ConnectionString"] });
            deviceAppContext.WarmUpDeviceConfigIds(db);

            // Process changes from stream and cache them here is the service
            var partitionFilter = EventHubHelpers.GetPartitionFilter(Config["PartitionFilter"]);
            Task receiveStatus = ehReader.ReadEventHubPartitionsAsync(
                Config["KafkaConnectionString"], Config["KafkaHeartbeatTopic"], Config["KafkaConsumerGroup"],
                partitionFilter, EventPosition.Latest, ReceivedEventCallback);

            // Start updating service status
            ServiceTracking.Start();
            Logger.Info("Started");
            receiveStatus.Wait();
            serviceBlock.WaitOne();
        }

        private Task ReceivedEventCallback(PartitionEvent receivedEvent)
        {
            try
            {
                // Check for heartbeat
                if (receivedEvent.Data.Properties.TryGetValue("DeviceKey", out object keyObject))
                {
                    string deviceKey = keyObject.ToString();

                    var json = Encoding.UTF8.GetString(receivedEvent.Data.Body.ToArray());
                    var heartbeatData = JsonConvert.DeserializeObject<DeviceApp.Shared.DeviceAppHeartbeat>(json);
                    Logger.Debug($"Received HB from: '{heartbeatData.DeviceAppId}'");

                    var cf = new BigMissionDbContextFactory();
                    using var db = cf.CreateDbContext(new[] { Config["ConnectionString"] });

                    if (heartbeatData.DeviceAppId == 0 && !string.IsNullOrWhiteSpace(deviceKey))
                    {
                        var key = Guid.Parse(deviceKey);
                        var deviceApp = db.DeviceAppConfig.FirstOrDefault(d => d.DeviceAppKey == key);
                        if (deviceApp != null)
                        {
                            heartbeatData.DeviceAppId = deviceApp.Id;
                        }
                        else
                        {
                            Logger.Debug($"No app configured with key {deviceKey}");
                        }
                    }

                    if (heartbeatData.DeviceAppId > 0)
                    {
                        // Update heartbeat
                        CommitHeartbeat(heartbeatData, json).Wait();

                        // Check configuration
                        ValidateConfiguration(deviceKey, heartbeatData, db).Wait();

                        // Check log level
                        CheckDeviceAppLogLevel(heartbeatData, deviceKey).Wait();
                    }
                }
                // Check for logs
                else if (receivedEvent.Data.Properties.TryGetValue("LogSourceID", out object sourceId))
                {
                    var deviceKey = sourceId.ToString();
                    Logger.Trace($"RX log from: {deviceKey}");
                    var cacheKey = string.Format(Consts.DEVICEAPP_LOG, deviceKey);
                    var cache = cacheMuxer.GetDatabase();
                    var log = Encoding.UTF8.GetString(receivedEvent.Data.Body.ToArray());
                    cache.ListLeftPush(cacheKey, log, flags: CommandFlags.FireAndForget);

                    if (_maxListLength > 0)
                    {
                        lock (_lastTrims)
                        {
                            if (_lastTrims.TryGetValue(deviceKey, out var lastTrim))
                            {
                                if ((DateTime.UtcNow - lastTrim) > _lengthTrim)
                                {
                                    cache.ListTrim(cacheKey, 0, _maxListLength, flags: CommandFlags.FireAndForget);
                                    _lastTrims[deviceKey] = DateTime.UtcNow;
                                    Logger.Trace($"Trimed logs for: {deviceKey}");
                                }
                            }
                            else
                            {
                                _lastTrims[deviceKey] = DateTime.UtcNow;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Unable to process event from event hub partition");
            }

            return Task.CompletedTask;
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
        /// Determine if the app is running the latest configuration.  If not, send the latest configuration to the application.
        /// </summary>
        /// <param name="hb"></param>
        /// <param name="db"></param>
        private async Task ValidateConfiguration(string deviceAppKey, DeviceApp.Shared.DeviceAppHeartbeat hb, BigMissionDbContext db)
        {
            Logger.Debug($"Validate configuration for: {hb.DeviceAppId}");
            var serverConfigId = await deviceAppContext.GetDeviceConfigId(deviceAppKey);
            Logger.Debug($"Server config ID={serverConfigId} HD Config ID={hb.ConfigurationId}");

            // Determine if the configuration guid is the same as whats in the database
            if (serverConfigId != null && new Guid(serverConfigId) != hb.ConfigurationId)
            {
                // The configuration changed, send it to the app
                Logger.Info($"Configuration for {hb.DeviceAppId} is expired.");

                // Load the new configuration
                var latestConfig = db.CanAppConfig.SingleOrDefault(c => c.DeviceAppId == hb.DeviceAppId);
                if (latestConfig != null)
                {
                    latestConfig.DeviceAppKey = deviceAppKey;

                    // Load channel mappings
                    var channelMappings = db.ChannelMappings.Where(m => m.DeviceAppId == hb.DeviceAppId);
                    latestConfig.ChannelMappings = channelMappings.ToArray();

                    var cmd = new Command
                    {
                        DestinationId = latestConfig.DeviceAppKey.ToString(),
                        CommandType = CommandTypes.UPDATE_CONFIG,
                        Timestamp = DateTime.UtcNow
                    };

                    AppCommands.EncodeCommandData(latestConfig, cmd);

                    await Commands.SendCommand(cmd, new Guid(cmd.DestinationId));

                    Logger.Info($"Sending updated configuraiton to application: {latestConfig.DeviceAppId}");
                }
                else
                {
                    Logger.Warn($"Configuration for {hb.DeviceAppId} not found in DB. Leaving app configuration in place.");
                }
            }

            Logger.Debug($"Finished validating configuration for: {hb.DeviceAppId}");
        }

        /// <summary>
        /// Determine if there is a user log level override set for the device.  If so, send it to the device.
        /// </summary>
        /// <param name="hb"></param>
        /// <param name="deviceAppKey"></param>
        private async Task CheckDeviceAppLogLevel(DeviceApp.Shared.DeviceAppHeartbeat hb, string deviceAppKey)
        {
            var cache = cacheMuxer.GetDatabase();
            var key = string.Format(Consts.DEVICEAPP_LOG_DESIRED_LEVEL, deviceAppKey);
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
                        Logger.Debug($"Sending log level update for device {deviceAppKey}");
                        var cmd = new Command
                        {
                            CommandType = CommandTypes.SET_LOG_LEVEL,
                            Data = desiredLevel.Name,
                            DestinationId = deviceAppKey,
                            Timestamp = DateTime.UtcNow
                        };
                        await Commands.SendCommand(cmd, new Guid(cmd.DestinationId));
                    }
                }
                catch (ArgumentException) { }
            }
        }
    }
}
