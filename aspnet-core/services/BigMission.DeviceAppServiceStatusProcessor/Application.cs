using Azure.Messaging.EventHubs.Consumer;
using BigMission.CommandTools;
using BigMission.CommandTools.Models;
using BigMission.EntityFrameworkCore;
using BigMission.RaceManagement;
using BigMission.ServiceData;
using BigMission.ServiceStatusTools;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using NLog;
using System;
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


        public Application(IConfiguration config, ILogger logger, ServiceTracking serviceTracking)
        {
            Config = config;
            Logger = logger;
            ServiceTracking = serviceTracking;
            Commands = new AppCommands(Config["ServiceId"], Config["KafkaConnectionString"], logger);
            ehReader = new EventHubHelpers(logger);
        }


        public void Run()
        {
            ServiceTracking.Update(ServiceState.STARTING, string.Empty);

            // Process changes from stream and cache them here is the service
            var partitionFilter = EventHubHelpers.GetPartitionFilter(Config["PartitionFilter"]);
            Task receiveStatus = ehReader.ReadEventHubPartitionsAsync(Config["KafkaConnectionString"], Config["KafkaHeartbeatTopic"], Config["KafkaConsumerGroup"], partitionFilter, EventPosition.Latest, ReceivedEventCallback);

            // Start updating service status
            ServiceTracking.Start();
            Logger.Info("Started");
            receiveStatus.Wait();
            serviceBlock.WaitOne();
        }

        private void ReceivedEventCallback(PartitionEvent receivedEvent)
        {
            try
            {
                if (receivedEvent.Data.Properties.TryGetValue("DeviceKey", out object keyObject))
                {
                    string deviceKey = keyObject.ToString();

                    var json = Encoding.UTF8.GetString(receivedEvent.Data.Body.ToArray());
                    var heartbeatData = JsonConvert.DeserializeObject<DeviceAppHeartbeat>(json);
                    Logger.Info($"Received HB from: '{heartbeatData.DeviceAppId}'");

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
                            Logger.Debug($"Not app configured with key {deviceKey}");
                        }
                    }

                    if (heartbeatData.DeviceAppId > 0)
                    {
                        // Update heartbeat
                        CommitHeartbeat(heartbeatData, db).Wait();

                        // Check configuration
                        ValidateConfiguration(heartbeatData, db).Wait();
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Unable to process event from event hub partition");
            }
        }

        /// <summary>
        /// Save the latest timestamp update to the database.
        /// </summary>
        /// <param name="hb"></param>
        /// <param name="db"></param>
        private async Task CommitHeartbeat(DeviceAppHeartbeat hb, BigMissionDbContext db)
        {
            Logger.Debug($"Saving heartbeat: {hb.DeviceAppId}");

            var row = db.DeviceAppHeartbeats.SingleOrDefault(r => r.DeviceAppId == hb.DeviceAppId);
            if (row != null)
            {
                row.Timestamp = hb.Timestamp;
            }
            else
            {
                db.DeviceAppHeartbeats.Add(hb);
            }

            Logger.Debug($"Saved heartbeat: {hb.DeviceAppId}");
            await db.SaveChangesAsync();
        }

        /// <summary>
        /// Determine if the app is running the latest configuration.  If not, send the latest configuration to the application.
        /// </summary>
        /// <param name="hb"></param>
        /// <param name="db"></param>
        private async Task ValidateConfiguration(DeviceAppHeartbeat hb, BigMissionDbContext db)
        {
            Logger.Debug($"Validate configuration for: {hb.DeviceAppId}");

            // Determine if the configuration guid is the same as whats in the database
            var exists = db.CanAppConfig.Count(c => c.DeviceAppId == hb.DeviceAppId && c.ConfigurationId == hb.ConfigurationId);
            if (exists == 0)
            {
                // The configuration changed, send it to the app
                Logger.Debug($"Configuration for {hb.DeviceAppId} is expired.");

                // Load the new configuration
                var latestConfig = db.CanAppConfig.SingleOrDefault(c => c.DeviceAppId == hb.DeviceAppId);
                if (latestConfig != null)
                {
                    // Load the apps key/Guid for the app to verify against
                    var device = db.DeviceAppConfig.Single(d => d.Id == hb.DeviceAppId);
                    latestConfig.DeviceAppKey = device.DeviceAppKey.ToString();

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

                    await Commands.SendCommand(cmd, Config["KafkaCommandTopic"], cmd.DestinationId);

                    Logger.Info($"Sending updated configuraiton to application: {latestConfig.DeviceAppId}");
                }
                else
                {
                    Logger.Warn($"Configuration for {hb.DeviceAppId} not found in DB. Leaving app configuration in place.");
                }
            }

            Logger.Debug($"Finished validating configuration for: {hb.DeviceAppId}");
        }
    }
}
