using BigMission.Cache.Models;
using BigMission.ServiceStatusTools;
using Newtonsoft.Json;
using StackExchange.Redis;
using System.Threading.Tasks.Dataflow;

namespace BigMission.CarTelemetryProcessor
{
    /// <summary>
    /// Subscribes to, receives, and propagates telemetry to registered consumers.
    /// </summary>
    internal class TelemetryPipeline : BackgroundService
    {
        public ILogger Logger { get; }
        public IEnumerable<ITelemetryConsumer> TelemetryConsumers { get; }

        private readonly IConnectionMultiplexer cacheMuxer;
        private readonly StartupHealthCheck startup;
        private readonly BroadcastBlock<DeviceApp.Shared.ChannelDataSetDto> producer;


        public TelemetryPipeline(ILoggerFactory loggerFactory, IConnectionMultiplexer cache, IEnumerable<ITelemetryConsumer> telemetryConsumers, StartupHealthCheck startup)
        {
            Logger = loggerFactory.CreateLogger(GetType().Name);
            TelemetryConsumers = telemetryConsumers;
            this.startup = startup;
            cacheMuxer = cache;
            producer = new BroadcastBlock<DeviceApp.Shared.ChannelDataSetDto>(chs => chs);

            foreach (var tc in telemetryConsumers)
            {
                var ab = new ActionBlock<DeviceApp.Shared.ChannelDataSetDto>(tc.ProcessTelemetryMessage);
                producer.LinkTo(ab);
            }
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
            await sub.SubscribeAsync(RedisChannel.Literal(Consts.CAR_TELEM_SUB), async (channel, message) =>
            {
                await HandleTelemetry(message);
            });

            Logger.LogInformation("Started");
            await startup.SetStarted();
        }

        private async Task HandleTelemetry(RedisValue value)
        {
            var telemetryData = JsonConvert.DeserializeObject<DeviceApp.Shared.ChannelDataSetDto>(value);
            if (telemetryData != null)
            {
                Logger.LogDebug($"Received telemetry from: '{telemetryData.DeviceAppId}'");
                if (telemetryData.Data != null)
                {
                    await producer.SendAsync(telemetryData);
                }
                else
                {
                    Logger.LogDebug($"Skipping empty data from: '{telemetryData.DeviceAppId}'");
                }
            }
        }
    }
}
