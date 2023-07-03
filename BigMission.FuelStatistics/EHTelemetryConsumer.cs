using Azure.Messaging.EventHubs.Consumer;
using BigMission.CommandTools;
using BigMission.DeviceApp.Shared;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using NLog;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Threading.Tasks;

namespace BigMission.FuelStatistics
{
    class EHTelemetryConsumer : ITelemetryConsumer
    {
        private readonly EventHubHelpers ehReader;
        private IConfiguration Config { get; }
        private ILogger Logger { get; }

        public Action<ChannelDataSetDto> ReceiveData { get; set; }

        public EHTelemetryConsumer(IConfiguration config, ILogger logger)
        {
            Config = config;
            Logger = logger;
            ehReader = new EventHubHelpers(logger);
        }

        public void Connect()
        {
            var partitionFilter = EventHubHelpers.GetPartitionFilter(Config["PartitionFilter"]);
            Task receiveStatus = ehReader.ReadEventHubPartitionsAsync(Config["KafkaConnectionString"], Config["KafkaDataTopic"], Config["KafkaConsumerGroup"],
                partitionFilter, EventPosition.Latest, ReceivedEventCallback);
        }

        private void ReceivedEventCallback(PartitionEvent receivedEvent)
        {
            try
            {
                var sw = Stopwatch.StartNew();
                if (receivedEvent.Data.Properties.Count > 0 && receivedEvent.Data.Properties.ContainsKey("ChannelDataSetDto"))
                {
                    if (receivedEvent.Data.Properties["Type"].ToString() != "ChannelDataSetDto")
                        return;
                }

                var json = Encoding.UTF8.GetString(receivedEvent.Data.Body.ToArray());
                var chDataSet = JsonConvert.DeserializeObject<ChannelDataSetDto>(json);

                if (chDataSet.Data == null)
                {
                    chDataSet.Data = new ChannelStatusDto[] { };
                }

                ReceiveData(chDataSet);

                Logger.Trace($"Processed car status in {sw.ElapsedMilliseconds}ms");
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Unable to process event from event hub partition");
            }
        }
    }
}
