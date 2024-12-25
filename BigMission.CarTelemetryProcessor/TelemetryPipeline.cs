using BigMission.ServiceStatusTools;
using Newtonsoft.Json;
using StackExchange.Redis;
using System.Threading.Tasks.Dataflow;

namespace BigMission.CarTelemetryProcessor;

/// <summary>
/// Subscribes to, receives, and propagates telemetry to registered consumers.
/// </summary>
internal class TelemetryPipeline : BackgroundService
{
    public ILogger Logger { get; }
    public IEnumerable<ITelemetryConsumer> TelemetryConsumers { get; }

    private readonly IConnectionMultiplexer cacheMuxer;
    private readonly StartupHealthCheck startup;
    private readonly ServiceTracking serviceTracking;
    private readonly BroadcastBlock<DeviceApp.Shared.ChannelDataSetDto> producer;


    public TelemetryPipeline(ILoggerFactory loggerFactory, IConnectionMultiplexer cache, IEnumerable<ITelemetryConsumer> telemetryConsumers,
        StartupHealthCheck startup, ServiceTracking serviceTracking)
    {
        Logger = loggerFactory.CreateLogger(GetType().Name);
        TelemetryConsumers = telemetryConsumers;
        this.startup = startup;
        this.serviceTracking = serviceTracking;
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

        // Ensure the consumer group exists for car telemetry processor channel status
        var db = cacheMuxer.GetDatabase();
        if (!(await db.KeyExistsAsync(Backend.Shared.Consts.CHANNEL_TELEM)) || (await db.StreamGroupInfoAsync(Backend.Shared.Consts.CHANNEL_TELEM)).All(x => x.Name != Backend.Shared.Consts.CHANNEL_TELEM_CAR_TELEM_PROC_GRP))
        {
            await db.StreamCreateConsumerGroupAsync(Backend.Shared.Consts.CHANNEL_TELEM, Backend.Shared.Consts.CHANNEL_TELEM_CAR_TELEM_PROC_GRP, "0-0", true);
        }

        Logger.LogInformation("Started");
        await startup.SetStarted();

        while (!stoppingToken.IsCancellationRequested)
        {
            var result = await db.StreamReadGroupAsync(Backend.Shared.Consts.CHANNEL_TELEM, Backend.Shared.Consts.CHANNEL_TELEM_CAR_TELEM_PROC_GRP, serviceTracking.ServiceId, ">", 1);
            if (result.Length == 0)
            {
                await Task.Delay(100, stoppingToken);
                continue;
            }

            Logger.LogDebug($"Received {result.Length} channel dtos.");
            foreach (var r in result)
            {
                foreach (var nv in r.Values)
                {
                    try
                    {
                        await HandleTelemetry(nv.Value);
                    }
                    catch (Exception ex)
                    {
                        Logger.LogError(ex, "Error processing telemetry");
                    }
                }

                await db.StreamAcknowledgeAsync(Backend.Shared.Consts.CHANNEL_TELEM, Backend.Shared.Consts.CHANNEL_TELEM_CAR_TELEM_PROC_GRP, r.Id);
            }
        }
    }

    private async Task HandleTelemetry(RedisValue value)
    {
        var telemetryData = JsonConvert.DeserializeObject<DeviceApp.Shared.ChannelDataSetDto>(value);
        if (telemetryData != null)
        {
            Logger.LogDebug($"Received telemetry from: '{telemetryData.DeviceAppId}'");
            if (telemetryData.Data != null && telemetryData.Data.Length > 0)
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
