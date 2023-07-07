using BigMission.Cache.Models;
using BigMission.DeviceApp.Shared;
using BigMission.ServiceStatusTools;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using StackExchange.Redis;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace BigMission.FuelStatistics.FuelRange
{
    /// <summary>
    /// Receives car telemetry and propagates it to consumers.
    /// </summary>
    public class CarTelemetryService : BackgroundService
    {
        private ILogger Logger { get; set; }
        private readonly IEnumerable<ICarTelemetryConsumer> telemetryConsumers;
        private readonly StartupHealthCheck startup;
        private readonly IConnectionMultiplexer cacheMuxer;

        public CarTelemetryService(IConnectionMultiplexer cacheMuxer, IEnumerable<ICarTelemetryConsumer> telemetryConsumers, ILoggerFactory loggerFactory, StartupHealthCheck startup)
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
            var telemetryData = JsonConvert.DeserializeObject<ChannelDataSetDto>(value);

            if (telemetryData.Data == null)
            {
                telemetryData.Data = System.Array.Empty<ChannelStatusDto>();
            }

            var consumerTasks = telemetryConsumers.Select(async (consumer) =>
            {
                await consumer.UpdateTelemetry(telemetryData);
            });

            await Task.WhenAll(consumerTasks);
        }
    }
}
