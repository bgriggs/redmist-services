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
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using NLog;
using NUglify.Helpers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace BigMission.VirtualChannelAggregator
{
    /// <summary>
    /// Manages the sending of virtual channels to CAN device Apps.  This reads the Virtuals channel
    /// configuration and initializes from the Channel DB status.  Then it receives channel data 
    /// updates from the cardata channel.  The services originating the Virtual channel data are 
    /// responsible for publishing that data to the cardata channel.  The StatusProcessor will
    /// make sure that data get pushed to the DB as well.
    /// </summary>
    class Application
    {
        private IConfiguration Config { get; }
        private ILogger Logger { get; }
        private ServiceTracking ServiceTracking { get; }
        private ConfigurationCommands configurationChanges;
        private static readonly string[] configChanges = new[] { ConfigurationCommandTypes.DEVICE_MODIFIED, ConfigurationCommandTypes.CHANNEL_MODIFIED };
        private readonly BigMissionDbContextFactory contextFactory = new BigMissionDbContextFactory();

        private EventProcessorClient processor;
        private Dictionary<int, Tuple<AppCommands, DeviceAppConfig>> deviceCommandClients = new Dictionary<int, Tuple<AppCommands, DeviceAppConfig>>();
        private Timer fullUpdateTimer;

        public Application(IConfiguration config, ILogger logger, ServiceTracking serviceTracking)
        {
            Config = config;
            Logger = logger;
            ServiceTracking = serviceTracking;
        }


        public void Run()
        {
            ServiceTracking.Update(ServiceState.STARTING, string.Empty);

            InternalRun();

            // Start updating service status
            ServiceTracking.Start();
        }

        private void InternalRun()
        {
            InitDeviceClients();

            // Process changes from stream and cache them here is the service
            var storageClient = new BlobContainerClient(Config["BlobStorageConnStr"], Config["BlobContainer"]);
            processor = new EventProcessorClient(storageClient, Config["KafkaConsumerGroup"], Config["KafkaConnectionString"], Config["KafkaDataTopic"]);
            processor.ProcessEventAsync += ChannelProcessEventHandler;
            processor.ProcessErrorAsync += ChannelProcessErrorHandler;
            processor.PartitionInitializingAsync += Processor_PartitionInitializingAsync;
            processor.StartProcessing();

            if (configurationChanges == null)
            {
                var group = "Config-" + Config["ServiceId"];
                configurationChanges = new ConfigurationCommands(Config["KafkaConnectionString"], group,
                    Config["KafkaConfigurationTopic"], Config["BlobStorageConnStr"], Config["BlobContainer"], Logger);
                configurationChanges.Subscribe(configChanges, ProcessConfigurationChange);
            }
            if (fullUpdateTimer == null)
            {
                var fullUpdateInterval = int.Parse(Config["FullUpdateFrequenceMs"]);
                if (fullUpdateInterval > 0)
                {
                    fullUpdateTimer = new Timer(FullUpdateCallback, null, 50, fullUpdateInterval);
                }
            }
        }

        private void InitDeviceClients()
        {
            lock (deviceCommandClients)
            {
                // Clean up any existing clients
                if (deviceCommandClients.Any())
                {
                    deviceCommandClients.ForEach(ds =>
                    {
                        try
                        {
                            ds.Value.Item1.Dispose();
                        }
                        catch { }
                    });
                    deviceCommandClients.Clear();
                }
                using var context = contextFactory.CreateDbContext(new[] { Config["ConnectionString"] });
                var devices = context.DeviceAppConfig.Where(d => !d.IsDeleted);
                var deviceIds = devices.Select(d => d.Id);

                foreach (var d in devices)
                {
                    var topic = Config["KafkaCommandTopic"]; // + "-" + d.DeviceAppKey;
                    var t = new Tuple<AppCommands, DeviceAppConfig>(
                         new AppCommands(Config["ServiceId"], Config["KafkaConnectionString"], null, topic,
                         Config["BlobStorageConnStr"], Config["BlobContainer"], Logger),
                          d);
                    deviceCommandClients[d.Id] = t;
                }
            }
        }

        private Task Processor_PartitionInitializingAsync(PartitionInitializingEventArgs arg)
        {
            arg.DefaultStartingPosition = EventPosition.Latest;
            return Task.CompletedTask;
        }

        private async Task ChannelProcessEventHandler(ProcessEventArgs eventArgs)
        {
            var json = Encoding.UTF8.GetString(eventArgs.Data.Body.ToArray());
            var chDataSet = JsonConvert.DeserializeObject<ChannelDataSet>(json);
            if (chDataSet != null)
            {
                Logger.Info($"Received status from: '{chDataSet.DeviceAppId}'");

                // Only process virtual channels
                if (chDataSet.IsVirtual)
                {
                    await SendChannelStaus(chDataSet.Data);
                }
            }

            // Update checkpoint in the blob storage so that the app receives only new events the next time it's run
            await eventArgs.UpdateCheckpointAsync(eventArgs.CancellationToken);
        }

        private Task ChannelProcessErrorHandler(ProcessErrorEventArgs eventArgs)
        {
            // Write details about the error to the console window
            Logger.Error(eventArgs.Exception, $"\tPartition '{ eventArgs.PartitionId}': an unhandled exception was encountered.");
            return Task.CompletedTask;
        }

        /// <summary>
        /// On a regular frequency send a full status update of all virtual channels to each device.
        /// </summary>
        /// <param name="obj"></param>
        private void FullUpdateCallback(object obj)
        {
            try
            {
                if (Monitor.TryEnter(fullUpdateTimer))
                {
                    try
                    {
                        Logger.Info("Sending full status udpate");

                        int[] deviceIds;
                        lock (deviceCommandClients)
                        {
                            deviceIds = deviceCommandClients.Keys.ToArray();
                        }

                        using var context = contextFactory.CreateDbContext(new[] { Config["ConnectionString"] });
                        var virtualChannels = context.ChannelMappings.Where(c => deviceIds.Contains(c.DeviceAppId) && c.IsVirtual);
                        var channelIds = virtualChannels.Select(c => c.Id).ToArray();

                        // Load current status
                        var channelStatus = context.ChannelStatus.Where(s => channelIds.Contains(s.ChannelId));
                        SendChannelStaus(channelStatus.ToArray()).Wait();
                    }
                    finally
                    {
                        Monitor.Exit(fullUpdateTimer);
                    }
                }
                else
                {
                    Logger.Debug("Full update timer skipped");
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error sending full updates to devices.");
            }
        }

        private async Task SendChannelStaus(ChannelStatus[] status)
        {
            var deviceStatus = status.GroupBy(s => s.DeviceAppId);
            var tasks = deviceStatus.Select(async ds =>
            {
                try
                {
                    bool hasDevice;
                    Tuple<AppCommands, DeviceAppConfig> client;
                    lock (deviceCommandClients)
                    {
                        hasDevice = deviceCommandClients.TryGetValue(ds.Key, out client);
                    }
                    if (hasDevice)
                    {
                        Logger.Trace($"Sending virtual status to device {ds.Key}");
                        var dataSet = new ChannelDataSet { DeviceAppId = ds.Key, IsVirtual = true, Timestamp = DateTime.UtcNow, Data = ds.ToArray() };
                        var cmd = new Command
                        {
                            DestinationId = client.Item2.DeviceAppKey.ToString(),
                            CommandType = CommandTypes.SEND_CAN,
                            Timestamp = DateTime.UtcNow
                        };
                        AppCommands.EncodeCommandData(dataSet, cmd);

                        await client.Item1.SendCommand(cmd);
                    }
                }
                catch (Exception ex)
                {
                    Logger.Error(ex, "Unable to send status");
                }
            });

            await Task.WhenAll(tasks);
        }

        /// <summary>
        /// When a change to devices or channels is received go ahead and reload everything.
        /// </summary>
        /// <param name="command"></param>
        private void ProcessConfigurationChange(KeyValuePair<string, string> command)
        {
            TearDown();
            InternalRun();
        }

        /// <summary>
        /// Psuedo dispose for closing down the channels to recreate with new configuration.  
        /// Does not kill the object though.
        /// </summary>
        public void TearDown()
        {
            processor.StopProcessing();

            var clientDispose = deviceCommandClients.Select(async c =>
            {
                await c.Value.Item1.DisposeAsync();
            });
            Task.WaitAll(clientDispose.ToArray());
        }
    }
}
