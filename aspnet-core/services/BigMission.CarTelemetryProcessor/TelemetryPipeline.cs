using BigMission.Cache.Models;
using BigMission.ServiceStatusTools;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Newtonsoft.Json;
using NLog;
using StackExchange.Redis;
using System.Threading.Tasks.Dataflow;

namespace BigMission.CarTelemetryProcessor
{
    /// <summary>
    /// Subscribes to, receives, and propogates telemetry to registered consumers.
    /// </summary>
    internal class TelemetryPipeline : BackgroundService
    {
        public IConfiguration Configuration { get; }
        public ILogger Logger { get; }
        public IEnumerable<ITelemetryConsumer> TelemetryConsumers { get; }
        public ServiceTracking ServiceTracking { get; }

        private readonly ConnectionMultiplexer cacheMuxer;
        private readonly BroadcastBlock<DeviceApp.Shared.ChannelDataSetDto> producer;


        public TelemetryPipeline(IConfiguration configuration, ILogger logger, IEnumerable<ITelemetryConsumer> telemetryConsumers, ServiceTracking serviceTracking)
        {
            Configuration = configuration;
            Logger = logger;
            TelemetryConsumers = telemetryConsumers;
            ServiceTracking = serviceTracking;
            cacheMuxer = ConnectionMultiplexer.Connect(Configuration["RedisConn"]);
            producer = new BroadcastBlock<DeviceApp.Shared.ChannelDataSetDto>(chs => chs);

            foreach (var tc in telemetryConsumers)
            {
                var ab = new ActionBlock<DeviceApp.Shared.ChannelDataSetDto>(tc.ProcessTelemetryMessage);
                producer.LinkTo(ab);
            }
        }


        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            ServiceTracking.Update(ServiceState.STARTING, $"Consumers: {TelemetryConsumers.Count()}");

            var sub = cacheMuxer.GetSubscriber();
            await sub.SubscribeAsync(Consts.CAR_TELEM_SUB, async (channel, message) =>
            {
                await HandleTelemetry(message);
            });
            Logger.Info("Started");
        }

        private async Task HandleTelemetry(RedisValue value)
        {
            var telemetryData = JsonConvert.DeserializeObject<DeviceApp.Shared.ChannelDataSetDto>(value);
            if (telemetryData != null)
            {
                Logger.Debug($"Received telemetry from: '{telemetryData.DeviceAppId}'");
                if (telemetryData.Data != null)
                {
                    await producer.SendAsync(telemetryData);
                }
                else
                {
                    Logger.Debug($"Skipping empty data from: '{telemetryData.DeviceAppId}'");
                }
            }
        }
    }
}
