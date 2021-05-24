using Azure.Messaging.EventHubs.Consumer;
using BigMission.Cache;
using BigMission.Cache.Models;
using BigMission.CommandTools;
using BigMission.DeviceApp.Shared;
using BigMission.RaceManagement;
using BigMission.ServiceData;
using BigMission.ServiceStatusTools;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using NLog;
using StackExchange.Redis;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace BigMission.KeypadServices
{
    class Application
    {
        private IConfiguration Config { get; }
        private ILogger Logger { get; }
        private ServiceTracking ServiceTracking { get; }
        private readonly EventHubHelpers ehReader;

        private readonly ManualResetEvent serviceBlock = new ManualResetEvent(false);

        private ConnectionMultiplexer cacheMuxer;


        public Application(IConfiguration config, ILogger logger, ServiceTracking serviceTracking)
        {
            Config = config;
            Logger = logger;
            ServiceTracking = serviceTracking;
            ehReader = new EventHubHelpers(logger);
        }


        public void Run()
        {
            ServiceTracking.Update(ServiceState.STARTING, string.Empty);

            // Process changes from stream and cache them here is the service
            var partitionFilter = EventHubHelpers.GetPartitionFilter(Config["PartitionFilter"]);
            Task receiveStatus = ehReader.ReadEventHubPartitionsAsync(Config["KafkaConnectionString"], Config["KafkaDataTopic"], Config["KafkaConsumerGroup"],
                partitionFilter, EventPosition.Latest, ReceivedEventCallback);

            // Start updating service status
            ServiceTracking.Start();
            Logger.Info("Started");
            receiveStatus.Wait();
            serviceBlock.WaitOne();
        }

        private void ReceivedEventCallback(PartitionEvent receivedEvent)
        {
            try
            {
                var sw = Stopwatch.StartNew();
                if (receivedEvent.Data.Properties.Count > 0 && receivedEvent.Data.Properties.ContainsKey("KeypadStatusDto"))
                {
                    if (receivedEvent.Data.Properties["Type"].ToString() != "KeypadStatusDto")
                        return;
                }

                var json = Encoding.UTF8.GetString(receivedEvent.Data.Body.ToArray());
                var keypadStatus = JsonConvert.DeserializeObject<KeypadStatusDto>(json);

                Logger.Trace($"Received keypad status: {keypadStatus.DeviceAppId} Count={keypadStatus.LedStates.Count}");

                // Reset time to server time to prevent timeouts when data is being updated.
                keypadStatus.Timestamp = DateTime.UtcNow;
                var kvps = new List<KeyValuePair<RedisKey, RedisValue>>();
                
                var db = cacheMuxer.GetDatabase();
                if (db != null)
                {
                    var key = string.Format(Consts.KEYPAD_STATUS, keypadStatus.DeviceAppId);
                    var buttonEntries = new List<HashEntry>();
                    foreach(var bStatus in keypadStatus.LedStates)
                    {
                        var ledjson = JsonConvert.SerializeObject(bStatus);
                        var h = new HashEntry(bStatus.ButtonNumber, ledjson);
                        buttonEntries.Add(h);
                    }

                    db.HashSet(key, buttonEntries.ToArray());
                  
                    Logger.Trace($"Cached new keypad status for device: {keypadStatus.DeviceAppId}");
                }
                else
                {
                    Logger.Warn("Cache was not available, failed to update status.");
                }

                Logger.Trace($"Processed status in {sw.ElapsedMilliseconds}ms");
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Unable to process event from event hub partition");
            }
        }
    }
}
