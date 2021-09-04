using Azure.Messaging.EventHubs.Consumer;
using BigMission.CommandTools;
using BigMission.DeviceApp.Shared;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Newtonsoft.Json;
using NLog;
using System.Collections.Generic;
using System.Linq;
using System.Text;
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
        private readonly EventHubHelpers ehReader;
        private readonly IEnumerable<ICarTelemetryConsumer> telemetryConsumers;


        public CarTelemetryService(IConfiguration config, ILogger logger, IEnumerable<ICarTelemetryConsumer> telemetryConsumers)
        {
            Config = config;
            this.telemetryConsumers = telemetryConsumers;
            ehReader = new EventHubHelpers(logger);
        }


        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var partitionFilter = EventHubHelpers.GetPartitionFilter(Config["PartitionFilter"]);
            await ehReader.ReadEventHubPartitionsAsync(Config["KafkaConnectionString"], Config["KafkaDataTopic"], Config["KafkaConsumerGroup"],
                partitionFilter, EventPosition.Latest, ReceivedEventCallback);
        }

        private async Task ReceivedEventCallback(PartitionEvent receivedEvent)
        {
            var json = Encoding.UTF8.GetString(receivedEvent.Data.Body.ToArray());
            var chDataSet = JsonConvert.DeserializeObject<ChannelDataSetDto>(json);

            if (chDataSet.Data == null)
            {
                chDataSet.Data = new ChannelStatusDto[] { };
            }

            var consumerTasks = telemetryConsumers.Select(async (consumer) => 
            {
                await consumer.UpdateTelemetry(chDataSet);
            });

            await Task.WhenAll(consumerTasks);
        }
    }
}
