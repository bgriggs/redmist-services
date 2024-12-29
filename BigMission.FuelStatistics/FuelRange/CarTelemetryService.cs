using BigMission.Cache.Models;
using BigMission.DeviceApp.Shared;
using BigMission.ServiceStatusTools;
using Newtonsoft.Json;
using StackExchange.Redis;

namespace BigMission.FuelStatistics.FuelRange;

/// <summary>
/// Receives car telemetry and propagates it to consumers.
/// </summary>
public class CarTelemetryService : BackgroundService
{
    private ILogger Logger { get; set; }
    private readonly IEnumerable<ICarTelemetryConsumer> telemetryConsumers;
    private readonly IStartupHealthCheck startup;
    private readonly IConnectionMultiplexer cacheMuxer;

    public CarTelemetryService(IConnectionMultiplexer cacheMuxer, IEnumerable<ICarTelemetryConsumer> telemetryConsumers, ILoggerFactory loggerFactory, IStartupHealthCheck startup)
    {
        this.telemetryConsumers = telemetryConsumers;
        this.startup = startup;
        this.cacheMuxer = cacheMuxer;
        Logger = loggerFactory.CreateLogger(GetType().Name);
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

        var sub = cacheMuxer.GetSubscriber();
        await sub.SubscribeAsync(RedisChannel.Literal(Consts.CAR_TELEM_SUB), async (channel, message) =>
        {
            await HandleTelemetry(message);
        });
    }

    private async Task HandleTelemetry(RedisValue value)
    {
        if (!value.HasValue)
        {
            Logger.LogWarning("Received empty telemetry message.");
            return;
        }

        var telemetryData = JsonConvert.DeserializeObject<ChannelDataSetDto>(value!);
        if (telemetryData == null)
        {
            Logger.LogWarning("Received telemetry message with null data.");
            return;
        }
        if (telemetryData.Data == null)
        {
            telemetryData.Data = [];
        }

        var consumerTasks = telemetryConsumers.Select(async (consumer) =>
        {
            await consumer.UpdateTelemetry(telemetryData);
        });

        await Task.WhenAll(consumerTasks);
    }
}
