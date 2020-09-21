using Abp.Extensions;
using Azure.Messaging.EventHubs;
using Azure.Messaging.EventHubs.Consumer;
using Azure.Messaging.EventHubs.Processor;
using Azure.Storage.Blobs;
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
        private EventProcessorClient processor;


        public Application(IConfiguration config, ILogger logger, ServiceTracking serviceTracking)
        {
            Config = config;
            Logger = logger;
            ServiceTracking = serviceTracking;
            Commands = new AppCommands(Config["ServiceId"], Config["KafkaConnectionString"], null,
                Config["KafkaCommandTopic"], Config["BlobStorageConnStr"], Config["BlobContainer"], logger);
        }


        public void Run()
        {
            ServiceTracking.Update(ServiceState.STARTING, string.Empty);

            var storageClient = new BlobContainerClient(Config["BlobStorageConnStr"], Config["BlobContainer"]);
            processor = new EventProcessorClient(storageClient, Config["KafkaConsumerGroup"], Config["KafkaConnectionString"], Config["KafkaHeartbeatTopic"]);
            processor.ProcessEventAsync += HbProcessEventHandler;
            processor.ProcessErrorAsync += HbProcessErrorHandler;
            processor.PartitionInitializingAsync += Processor_PartitionInitializingAsync;
            processor.StartProcessing();

            // Start updating service status
            ServiceTracking.Start();
            Logger.Info("Started");
        }

        private Task Processor_PartitionInitializingAsync(PartitionInitializingEventArgs arg)
        {
            arg.DefaultStartingPosition = EventPosition.Latest;
            return Task.CompletedTask;
        }

        private async Task HbProcessEventHandler(ProcessEventArgs eventArgs)
        {
            if (eventArgs.Data.Properties.TryGetValue("DeviceKey", out object keyObject))
            {
                string deviceKey = keyObject.ToString();

                var json = Encoding.UTF8.GetString(eventArgs.Data.Body.ToArray());
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
                    await CommitHeartbeat(heartbeatData, db);

                    // Check configuration
                    await ValidateConfiguration(heartbeatData, db);
                }
            }

            // Update checkpoint in the blob storage so that the app receives only new events the next time it's run
            await eventArgs.UpdateCheckpointAsync(eventArgs.CancellationToken);
        }

        private Task HbProcessErrorHandler(ProcessErrorEventArgs eventArgs)
        {
            // Write details about the error to the console window
            Logger.Error(eventArgs.Exception, $"\tPartition '{ eventArgs.PartitionId}': an unhandled exception was encountered.");
            return Task.CompletedTask;
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

                    await Commands.SendCommand(cmd);

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
