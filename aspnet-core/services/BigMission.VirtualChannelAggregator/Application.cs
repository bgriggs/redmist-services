using BigMission.Cache.Models;
using BigMission.CommandTools;
using BigMission.CommandTools.Models;
using BigMission.Database;
using BigMission.Database.Models;
using BigMission.DeviceApp.Shared;
using BigMission.ServiceStatusTools;
using BigMission.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Newtonsoft.Json;
using NLog;
using StackExchange.Redis;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
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
    class Application : BackgroundService
    {
        private IConfiguration Config { get; }
        private ILogger Logger { get; }
        private ServiceTracking ServiceTracking { get; }
        public IDateTimeHelper DateTime { get; }

        private readonly Dictionary<int, Tuple<AppCommands, DeviceAppConfig, IEnumerable<int>>> deviceCommandClients = new();
        private readonly ConnectionMultiplexer cacheMuxer;


        public Application(IConfiguration config, ILogger logger, ServiceTracking serviceTracking, IDateTimeHelper dateTimeHelper)
        {
            Config = config;
            Logger = logger;
            ServiceTracking = serviceTracking;
            DateTime = dateTimeHelper;
            cacheMuxer = ConnectionMultiplexer.Connect(config["RedisConn"]);
        }


        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            ServiceTracking.Update(ServiceState.STARTING, string.Empty);
            await InitDeviceClients();

            var sub = cacheMuxer.GetSubscriber();

            // Watch for changes in device app configuraiton such as channels
            await sub.SubscribeAsync(Consts.CAR_CONFIG_CHANGED_SUB, async (channel, message) =>
            {
                Logger.Info("Car device app configuration notification received");
                await InitDeviceClients();
            });

            // Process changes from stream and cache them here in the service
            await sub.SubscribeAsync(Consts.CAR_TELEM_SUB, async (channel, message) =>
            {
                await HandleTelemetry(message);
            });

            // Start loop for sending full updates
            while (!stoppingToken.IsCancellationRequested)
            {
                var sw = Stopwatch.StartNew();
                await SendFullUpdate();
                Logger.Debug($"Sent full update in {sw.ElapsedMilliseconds}ms");
                await Task.Delay(int.Parse(Config["FullUpdateFrequenceMs"]), stoppingToken);
            }
        }

        private async Task HandleTelemetry(RedisValue value)
        {
            var telemetryData = JsonConvert.DeserializeObject<ChannelDataSetDto>(value);
            if (telemetryData != null)
            {
                Logger.Debug($"Received status from: '{telemetryData.DeviceAppId}'");

                // Only process virtual channels
                if (telemetryData.IsVirtual)
                {
                    await SendChannelStaus(telemetryData.Data);
                }
            }
        }

        private async Task InitDeviceClients()
        {
            Logger.Debug("Loading device channel mappings");
            deviceCommandClients.Clear();

            try
            {
                using var context = new RedMist(Config["ConnectionString"]);
                var devices = await context.DeviceAppConfigs.Where(d => !d.IsDeleted).ToListAsync();
                var deviceIds = devices.Select(d => d.Id);
                var channels = await context.ChannelMappings.Where(c => c.IsVirtual).ToListAsync();

                var serviceId = new Guid(Config["ServiceId"]);
                foreach (var d in devices)
                {
                    var devVirtChs = channels.Where(c => c.DeviceAppId == d.Id).Select(c => c.Id);
                    var t = new Tuple<AppCommands, DeviceAppConfig, IEnumerable<int>>(
                        new AppCommands(serviceId, Config["ApiKey"], Config["ApiUrl"], Logger), d, devVirtChs);
                    deviceCommandClients[d.Id] = t;
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Could not initialize car app devices.");
            }
        }

        /// <summary>
        /// On a regular frequency send a full status update of all virtual channels to each device.
        /// </summary>
        private async Task SendFullUpdate()
        {
            try
            {
                Logger.Info("Sending full status udpate");

                var deviceIds = deviceCommandClients.Keys.ToArray();
                var channelIds = deviceCommandClients.Values.SelectMany(v => v.Item3).ToArray();


                // Load current status
                var cache = cacheMuxer.GetDatabase();
                var rks = channelIds.Select(i => new RedisKey(string.Format(Consts.CHANNEL_KEY, i))).ToArray();
                var channelStatusStrs = await cache.StringGetAsync(rks);
                var channelStatus = ConvertToDeviceChStatus(channelIds, channelStatusStrs);
                await SendChannelStaus(channelStatus.ToArray());
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error sending full updates to devices.");
            }
        }

        private static List<ChannelStatusDto> ConvertToDeviceChStatus(int[] channelIds, RedisValue[] values)
        {
            var css = new List<ChannelStatusDto>();
            for (int i = 0; i < channelIds.Length; i++)
            {
                var chstr = values[i];
                if (!chstr.IsNullOrEmpty)
                {
                    var chmod = JsonConvert.DeserializeObject<ChannelStatusDto>(chstr);
                    css.Add(chmod);
                }
            }

            return css;
        }

        private async Task SendChannelStaus(ChannelStatusDto[] status)
        {
            var deviceStatus = status.GroupBy(s => s.DeviceAppId);
            var tasks = deviceStatus.Select(async ds =>
            {
                try
                {
                    var hasDevice = deviceCommandClients.TryGetValue(ds.Key, out Tuple<AppCommands, DeviceAppConfig, IEnumerable<int>> client);
                    if (hasDevice)
                    {
                        Logger.Trace($"Sending virtual status to device {ds.Key}");
                        var dataSet = new ChannelDataSetDto { DeviceAppId = ds.Key, IsVirtual = true, Timestamp = DateTime.UtcNow, Data = ds.ToArray() };
                        var cmd = new Command
                        {
                            DestinationId = client.Item2.DeviceAppKey.ToString(),
                            CommandType = CommandTypes.SEND_CAN,
                            Timestamp = DateTime.UtcNow
                        };
                        AppCommands.EncodeCommandData(dataSet, cmd);

                        await client.Item1.SendCommandAsync(cmd, new Guid(cmd.DestinationId));
                    }
                }
                catch (Exception ex)
                {
                    Logger.Error(ex, "Unable to send status");
                }
            });

            await Task.WhenAll(tasks);
        }
    }
}
