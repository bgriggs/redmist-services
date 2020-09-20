using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using NLog;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Linq;
using BigMission.ServiceStatusTools;
using BigMission.RaceManagement;
using BigMission.ServiceData;
using BigMission.EntityFrameworkCore;
using Azure.Messaging.EventHubs;
using Azure.Storage.Blobs;
using System.Threading.Tasks;
using Azure.Messaging.EventHubs.Processor;
using System.Text;
using Azure.Messaging.EventHubs.Consumer;

namespace BigMission.CarRealTimeStatusProcessor
{
    /// <summary>
    /// Processes channel status from a device and updates the latest values into a table.
    /// </summary>
    class Application
    {
        private IConfiguration Config { get; }
        private ILogger Logger { get; }
        private ServiceTracking ServiceTracking { get; }

        private EventProcessorClient processor;
        private readonly Dictionary<int, ChannelStatus> last = new Dictionary<int, ChannelStatus>();
        private Timer saveTimer;
        private BigMissionDbContext context;


        public Application(IConfiguration config, ILogger logger, ServiceTracking serviceTracking)
        {
            Config = config;
            Logger = logger;
            ServiceTracking = serviceTracking;
        }


        public void Run()
        {
            ServiceTracking.Update(ServiceState.STARTING, string.Empty);

            var cf = new BigMissionDbContextFactory();
            context = cf.CreateDbContext(new[] { Config["ConnectionString"] });

            // Process changes from stream and cache them here is the service
            var storageClient = new BlobContainerClient(Config["BlobStorageConnStr"], Config["BlobContainer"]);
            processor = new EventProcessorClient(storageClient, Config["KafkaConsumerGroup"], Config["KafkaConnectionString"], Config["KafkaDataTopic"]);
            processor.ProcessEventAsync += StatusProcessEventHandler;
            processor.ProcessErrorAsync += StatusProcessErrorHandler;
            processor.PartitionInitializingAsync += Processor_PartitionInitializingAsync;
            processor.StartProcessing();

            // Process the cached status and update the database
            saveTimer = new Timer(SaveCallback, null, 2000, 700);

            // Start updating service status
            ServiceTracking.Start();
            Logger.Info("Started");
        }

        private Task Processor_PartitionInitializingAsync(PartitionInitializingEventArgs arg)
        {
            arg.DefaultStartingPosition = EventPosition.Latest;
            return Task.CompletedTask;
        }

        private async Task StatusProcessEventHandler(ProcessEventArgs eventArgs)
        {
            var json = Encoding.UTF8.GetString(eventArgs.Data.Body.ToArray());
            var chDataSet = JsonConvert.DeserializeObject<ChannelDataSet>(json);

            if (chDataSet.Data == null)
            {
                chDataSet.Data = new ChannelStatus[] { };
            }

            Logger.Trace($"Received log: {chDataSet.DeviceAppId}");

            // Update cached status
            lock (last)
            {
                foreach (var ch in chDataSet.Data)
                {
                    if (ch.DeviceAppId == 0)
                    {
                        ch.DeviceAppId = chDataSet.DeviceAppId;
                    }

                    if (last.TryGetValue(ch.ChannelId, out ChannelStatus row))
                    {
                        if (row.Value != ch.Value)
                        {
                            row.Value = ch.Value;
                            row.Timestamp = ch.Timestamp;
                        }
                    }
                    else // Create new row
                    {
                        var cr = new ChannelStatus { DeviceAppId = ch.DeviceAppId, ChannelId = ch.ChannelId, Value = ch.Value, Timestamp = ch.Timestamp };
                        last[ch.ChannelId] = cr;
                    }
                }
            }

            // Update checkpoint in the blob storage so that the app receives only new events the next time it's run
            await eventArgs.UpdateCheckpointAsync(eventArgs.CancellationToken);
        }

        private Task StatusProcessErrorHandler(ProcessErrorEventArgs eventArgs)
        {
            // Write details about the error to the console window
            Logger.Error(eventArgs.Exception, $"\tPartition '{ eventArgs.PartitionId}': an unhandled exception was encountered.");
            return Task.CompletedTask;
        }

        public void UpdateChanges(ChannelStatus[] rows)
        {
            foreach (var updated in rows)
            {
                var r = context.ChannelStatus.SingleOrDefault(c => c.DeviceAppId == updated.DeviceAppId && c.ChannelId == updated.ChannelId);
                if (r != null)
                {
                    r.Value = updated.Value;
                    r.Timestamp = updated.Timestamp;
                }
                else
                {
                    context.ChannelStatus.Add(updated);
                }
            }

            context.SaveChanges();
        }

        private void SaveCallback(object obj)
        {
            if (Monitor.TryEnter(saveTimer))
            {
                try
                {
                    // Get a copy of the current status as not to block
                    ChannelStatus[] status;
                    lock (last)
                    {
                        status = last.Select(l => l.Value.Clone()).ToArray();
                    }

                    // Commit changes to DB
                    UpdateChanges(status);
                }
                catch (Exception ex)
                {
                    Logger.Error(ex, "Unable to commit status");
                }
                finally
                {
                    Monitor.Exit(saveTimer);
                }
            }
        }
    }
}
