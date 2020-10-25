using Azure.Messaging.EventHubs;
using Azure.Messaging.EventHubs.Processor;
using Azure.Storage.Blobs;
using BigMission.EntityFrameworkCore;
using BigMission.RaceManagement;
using BigMission.ServiceData;
using BigMission.ServiceStatusTools;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using NLog;
using System;
using System.Text;
using System.Threading.Tasks;
using System.Threading;
using System.Diagnostics;

namespace BigMission.CarDataLogger
{
    /// <summary>
    /// Log car CAN bus data to database.
    /// </summary>
    class Application
    {
        private IConfiguration Config { get; }
        private ILogger Logger { get; }
        private ServiceTracking ServiceTracking { get; }
        private EventProcessorClient processor;
        private ManualResetEvent serviceBlock = new ManualResetEvent(false);

        public Application(IConfiguration config, ILogger logger, ServiceTracking serviceTracking)
        {
            Config = config;
            Logger = logger;
            ServiceTracking = serviceTracking;
        }


        public void Run()
        {
            ServiceTracking.Update(ServiceState.STARTING, string.Empty);

            var storageClient = new BlobContainerClient(Config["BlobStorageConnStr"], Config["BlobContainer"]);
            processor = new EventProcessorClient(storageClient, Config["KafkaConsumerGroup"], Config["KafkaConnectionString"], Config["KafkaDataTopic"]);
            processor.ProcessEventAsync += LogProcessEventHandler;
            processor.ProcessErrorAsync += LogProcessErrorHandler;
            processor.StartProcessing();

            // Start updating service status
            ServiceTracking.Start();
            Logger.Info("Started");
            serviceBlock.WaitOne();
        }

        private async Task LogProcessEventHandler(ProcessEventArgs eventArgs)
        {
            var json = Encoding.UTF8.GetString(eventArgs.Data.Body.ToArray());
            var chDataSet = JsonConvert.DeserializeObject<ChannelDataSet>(json);

            Logger.Trace($"Received log: {chDataSet.DeviceAppId}");

            await SaveLog(chDataSet);

            // Update checkpoint in the blob storage so that the app receives only new events the next time it's run
            await eventArgs.UpdateCheckpointAsync(eventArgs.CancellationToken);
        }

        private Task LogProcessErrorHandler(ProcessErrorEventArgs eventArgs)
        {
            // Write details about the error to the console window
            Logger.Error(eventArgs.Exception, $"\tPartition '{ eventArgs.PartitionId}': an unhandled exception was encountered.");
            return Task.CompletedTask;
        }

        private async Task SaveLog(ChannelDataSet ds)
        {
            try
            {
                if (ds.Data?.Length > 0)
                {
                    foreach(var l in ds.Data)
                    {
                        if (l.DeviceAppId == 0)
                        {
                            l.DeviceAppId = ds.DeviceAppId;
                        }
                    }

                    var cf = new BigMissionDbContextFactory();
                    using var db = cf.CreateDbContext(new[] { Config["ConnectionString"] });
                    db.ChannelLog.AddRange(ds.GetLogs());
                    var sw = Stopwatch.StartNew();
                    await db.SaveChangesAsync();
                    Logger.Trace($"Device source {ds.DeviceAppId} DB Commit in {sw.ElapsedMilliseconds}ms");
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Unable to save logs");
            }
        }
    }
}
