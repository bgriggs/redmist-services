using Azure.Messaging.EventHubs.Consumer;
using BigMission.Cache;
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
using StackExchange.Redis;
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
        private EventHubHelpers ehReader;
        private Task receiveStatus;
        private readonly Dictionary<int, Tuple<AppCommands, DeviceAppConfig, int[]>> deviceCommandClients = new Dictionary<int, Tuple<AppCommands, DeviceAppConfig, int[]>>();
        private Timer fullUpdateTimer;
        private readonly ManualResetEvent serviceBlock = new ManualResetEvent(false);
        private readonly ConnectionMultiplexer cacheMuxer;

        public Application(IConfiguration config, ILogger logger, ServiceTracking serviceTracking)
        {
            Config = config;
            Logger = logger;
            ServiceTracking = serviceTracking;
            cacheMuxer = ConnectionMultiplexer.Connect(config["RedisConn"]);
        }


        public void Run()
        {
            ServiceTracking.Update(ServiceState.STARTING, string.Empty);

            InternalRun();

            // Start updating service status
            ServiceTracking.Start();
            serviceBlock.WaitOne();
        }

        private void InternalRun()
        {
            InitDeviceClients();

            // Process changes from stream and cache them here in the service
            ehReader = new EventHubHelpers(Logger);
            var partitionFilter = EventHubHelpers.GetPartitionFilter(Config["PartitionFilter"]);
            receiveStatus = ehReader.ReadEventHubPartitionsAsync(Config["KafkaConnectionString"], Config["KafkaDataTopic"], Config["KafkaConsumerGroup"],
                partitionFilter, EventPosition.Latest, ReceivedEventCallback);

            if (configurationChanges == null)
            {
                var group = "config-" + Config["ServiceId"];
                configurationChanges = new ConfigurationCommands(Config["KafkaConnectionString"], group, Config["KafkaConfigurationTopic"], Logger);
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

        private void ReceivedEventCallback(PartitionEvent receivedEvent)
        {
            try
            {
                var json = Encoding.UTF8.GetString(receivedEvent.Data.Body.ToArray());
                var chDataSet = JsonConvert.DeserializeObject<ChannelDataSet>(json);
                if (chDataSet != null)
                {
                    Logger.Debug($"Received status from: '{chDataSet.DeviceAppId}'");

                    // Only process virtual channels
                    if (chDataSet.IsVirtual)
                    {
                        SendChannelStaus(chDataSet.Data).Wait();
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Unable to process event from event hub partition");
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
                var channels = context.ChannelMappings.Where(c => c.IsVirtual).ToArray();

                foreach (var d in devices)
                {
                    var devVirtChs = channels.Where(c => c.DeviceAppId == d.Id).Select(c => c.Id).ToArray();
                    var t = new Tuple<AppCommands, DeviceAppConfig, int[]>(
                        new AppCommands(Config["ServiceId"], Config["KafkaConnectionString"], Logger), d, devVirtChs);
                    deviceCommandClients[d.Id] = t;
                }
            }
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
                        int[] channelIds;
                        lock (deviceCommandClients)
                        {
                            deviceIds = deviceCommandClients.Keys.ToArray();
                            channelIds = deviceCommandClients.Values.SelectMany(v => v.Item3).ToArray();
                        }

                        // Load current status
                        var cache = cacheMuxer.GetDatabase();
                        var rks = channelIds.Select(i => new RedisKey(string.Format(Cache.Models.Consts.CHANNEL_KEY, i))).ToArray();
                        var channelStatusStrs = cache.StringGet(rks);
                        var channelStatus = ChannelContext.ConvertToEfChStatus(channelIds, channelStatusStrs);
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
                    Tuple<AppCommands, DeviceAppConfig, int[]> client;
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

                        await client.Item1.SendCommand(cmd, Config["KafkaCommandTopic"], cmd.DestinationId);
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
            ehReader.CancelProcessing();

            var clientDispose = deviceCommandClients.Select(async c =>
            {
                await c.Value.Item1.DisposeAsync();
            });
            Task.WaitAll(clientDispose.ToArray());
        }
    }
}
