using Azure.Messaging.EventHubs;
using Azure.Messaging.EventHubs.Consumer;
using Azure.Messaging.EventHubs.Processor;
using Azure.Storage.Blobs;
using BigMission.EntityFrameworkCore;
using BigMission.RaceManagement;
using BigMission.ServiceData;
using BigMission.ServiceStatusTools;
using Microsoft.AspNetCore.Http.Connections;
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

        /// <summary>
        /// Alarms by their group
        /// </summary>
        private readonly Dictionary<string, List<AlarmStatus>> alarmStatus = new Dictionary<string, List<AlarmStatus>>();

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

            // If the status is too old, toss it
            var diff = DateTime.UtcNow - chDataSet.Timestamp;
            if (diff > TimeSpan.FromSeconds(5))
            {
                Logger.Trace($"Skipping status, too old: {diff.TotalSeconds} secs, {chDataSet.DeviceAppId}");
                return;
            }

            lock (alarmStatus)
            {
                Parallel.ForEach(alarmStatus.Values, (alarmGrp) =>
                {
                    try
                    {
                        // Order the alarms by priority and check the highest priority first
                        var orderedAlarms = alarmGrp.OrderBy(a => a.Priority);
                        bool channelAlarmActive = false;
                        foreach (var alarm in orderedAlarms)
                        {
                            Logger.Trace($"Processing alarm: {alarm.Alarm.Name}");
                            // If the an alarm is already active on the channel, skip it
                            if (!channelAlarmActive)
                            {
                                // Run the check on the alarm and preform triggers
                                channelAlarmActive = alarm.CheckConditions(chDataSet.Data);
                            }
                            else // Alarm for channel is active, turn off lower priority alarms
                            {
                                alarm.Supersede();
                                Logger.Trace($"Superseded alarm {alarm.Alarm.Name} due to higher priority being active");
                            }
                        }
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

                var als = new List<AlarmStatus>();
                foreach (var ac in alarmConfig)
                {
                    var a = new AlarmStatus(ac, Config["ConnectionString"], Logger);
                    als.Add(a);
                }

                // Group the alarms by the targeted channel for alarm progression support, e.g. info, warning, error
                var grps = als.GroupBy(a => a.AlarmGroup);

                lock (alarmStatus)
                {
                    alarmStatus.Clear();
                    foreach(var chGrp in grps)
                    {
                        List<AlarmStatus> channelAlarms;
                        if (!alarmStatus.TryGetValue(chGrp.Key, out channelAlarms))
                        {
                            channelAlarms = new List<AlarmStatus>();
                            alarmStatus[chGrp.Key] = channelAlarms;
                        }

                        channelAlarms.AddRange(chGrp);
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
