using Azure.Messaging.EventHubs;
using Azure.Messaging.EventHubs.Consumer;
using Azure.Messaging.EventHubs.Processor;
using Azure.Storage.Blobs;
using BigMission.EntityFrameworkCore;
using BigMission.RaceManagement;
using BigMission.ServiceData;
using BigMission.ServiceStatusTools;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using NLog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace BigMission.AlarmProcessor
{
    /// <summary>
    /// Processes channel status from a device and look for alarm conditions to be met.
    /// </summary>
    class Application
    {
        private IConfiguration Config { get; }
        private ILogger Logger { get; }
        private ServiceTracking ServiceTracking { get; }
        private readonly List<AlarmStatus> alarmStatus = new List<AlarmStatus>();

        private EventProcessorClient processor;
        private BigMissionDbContext context;
        private readonly ManualResetEvent serviceBlock = new ManualResetEvent(false);


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
            LoadAlarmConfiguration(null);

            // Process changes from stream and cache them here is the service
            var storageClient = new BlobContainerClient(Config["BlobStorageConnStr"], Config["BlobContainer"]);
            processor = new EventProcessorClient(storageClient, Config["KafkaConsumerGroup"], Config["KafkaConnectionString"], Config["KafkaDataTopic"]);
            processor.ProcessEventAsync += StatusProcessEventHandler;
            processor.ProcessErrorAsync += StatusProcessErrorHandler;
            processor.PartitionInitializingAsync += Processor_PartitionInitializingAsync;
            processor.StartProcessing();

            // Start updating service status
            ServiceTracking.Start();
            Logger.Info("Started");
            serviceBlock.WaitOne();
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

            Logger.Trace($"Received status: {chDataSet.DeviceAppId}");

            lock (alarmStatus)
            {
                Parallel.ForEach(alarmStatus, (alarm) =>
                {
                    try
                    {
                        Logger.Trace($"Processing alarm: {alarm.Alarm.Name}");
                        alarm.CheckConditions(chDataSet.Data);
                    }
                    catch (Exception ex)
                    {
                        Logger.Error(ex, "Unable to process alarm status update");
                    }
                });
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

        #region Alarm Configuration

        private void LoadAlarmConfiguration(object obj)
        {
            try
            {
                //var cf = new BigMissionDbContextFactory();
                //using var context = cf.CreateDbContext(new[] { Config["ConnectionString"] });
                var alarmConfig = context.CarAlarms
                    .Where(a => !a.IsDeleted && a.IsEnabled)
                    .Include(a => a.Conditions)
                    .Include(a => a.Triggers);

                Logger.Info($"Loaded {alarmConfig.Count()} Alarms");
                lock (alarmStatus)
                {
                    alarmStatus.Clear();
                    foreach (var ac in alarmConfig)
                    {
                        var a = new AlarmStatus(ac, Config["ConnectionString"], Logger);
                        alarmStatus.Add(a);
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Unable to initialize alarms");
            }
        }

        #endregion

    }
}
