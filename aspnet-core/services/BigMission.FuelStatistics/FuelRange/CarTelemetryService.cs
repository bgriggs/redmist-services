using BigMission.Cache.Models;
using BigMission.DeviceApp.Shared;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Newtonsoft.Json;
using StackExchange.Redis;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace BigMission.FuelStatistics.FuelRange
{
    /// <summary>
    /// Receives car telemetry and propigates it to consumers.
    /// </summary>
    public class CarTelemetryService : BackgroundService
    {
        public IConfiguration Config { get; }
        private readonly IEnumerable<ICarTelemetryConsumer> telemetryConsumers;
        private readonly ConnectionMultiplexer cacheMuxer;

        public CarTelemetryService(IConfiguration config, IEnumerable<ICarTelemetryConsumer> telemetryConsumers)
        {
            Config = config;
            this.telemetryConsumers = telemetryConsumers;
            cacheMuxer = ConnectionMultiplexer.Connect(config["RedisConn"]);
        }


        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var sub = cacheMuxer.GetSubscriber();
            await sub.SubscribeAsync(Consts.CAR_TELEM_SUB, async (channel, message) =>
            {
                await HandleTelemetry(message);
            });
        }

        private async Task HandleTelemetry(RedisValue value)
        {
            var telemetryData = JsonConvert.DeserializeObject<ChannelDataSetDto>(value);

            if (telemetryData.Data == null)
            {
                telemetryData.Data = new ChannelStatusDto[] { };
            }

            var consumerTasks = telemetryConsumers.Select(async (consumer) =>
            {
                await consumer.UpdateTelemetry(telemetryData);
            });

            await Task.WhenAll(consumerTasks);
        }
    }
}
