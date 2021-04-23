using Azure.Messaging.EventHubs.Consumer;
using BigMission.Cache;
using BigMission.Cache.Models;
using BigMission.CommandTools;
using BigMission.CommandTools.Models;
using BigMission.DeviceApp.Shared;
using BigMission.EntityFrameworkCore;
using BigMission.ServiceData;
using BigMission.ServiceStatusTools;
using Microsoft.EntityFrameworkCore;
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
        private readonly EventHubHelpers ehReader;
        private readonly ConnectionMultiplexer cacheMuxer;
        private readonly ChannelContext channelContext;
        private ConfigurationCommands configurationChanges;
        private static readonly string[] configChanges = new[]
        {
            ConfigurationCommandTypes.DEVICE_MODIFIED,
            ConfigurationCommandTypes.CHANNEL_MODIFIED,
            ConfigurationCommandTypes.ALARM_CHANGED
        };

        /// <summary>
        /// Alarms by their group
        /// </summary>
        private readonly Dictionary<string, List<AlarmStatus>> alarmStatus = new Dictionary<string, List<AlarmStatus>>();

        private BigMissionDbContext context;
        private readonly ManualResetEvent serviceBlock = new ManualResetEvent(false);


        public Application(IConfiguration config, ILogger logger, ServiceTracking serviceTracking)
        {
            Config = config;
            Logger = logger;
            ServiceTracking = serviceTracking;
            ehReader = new EventHubHelpers(logger);
            cacheMuxer = ConnectionMultiplexer.Connect(Config["RedisConn"]);
            channelContext = new ChannelContext(Config["ConnectionString"], cacheMuxer, true);
        }


        public void Run()
        {
            ServiceTracking.Update(ServiceState.STARTING, string.Empty);

            var cf = new BigMissionDbContextFactory();
            context = cf.CreateDbContext(new[] { Config["ConnectionString"] });
            LoadAlarmConfiguration(null);
            channelContext.WarmUp();

            // Process changes from stream and cache them here is the service
            var partitionFilter = EventHubHelpers.GetPartitionFilter(Config["PartitionFilter"]);
            Task receiveStatus = ehReader.ReadEventHubPartitionsAsync(Config["KafkaConnectionString"], Config["KafkaDataTopic"], Config["KafkaConsumerGroup"],
                partitionFilter, EventPosition.Latest, ReceivedEventCallback);

            if (configurationChanges == null)
            {
                var group = "config-" + Config["ServiceId"];
                configurationChanges = new ConfigurationCommands(Config["KafkaConnectionString"], group, Config["KafkaConfigurationTopic"], Logger);
                configurationChanges.Subscribe(configChanges, ProcessConfigurationChange);
            }

            // Start updating service status
            ServiceTracking.Start();
            Logger.Info("Started");
            receiveStatus.Wait();
            serviceBlock.WaitOne();
        }

        private void ReceivedEventCallback(PartitionEvent receivedEvent)
        {
            var sw = Stopwatch.StartNew();
            try
            {
                var json = Encoding.UTF8.GetString(receivedEvent.Data.Body.ToArray());
                var chDataSet = JsonConvert.DeserializeObject<ChannelDataSetDto>(json);

                if (chDataSet.Data == null)
                {
                    chDataSet.Data = new ChannelStatusDto[] { };
                }

                Logger.Trace($"Received status: {chDataSet.DeviceAppId}");

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
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Unable to process event from event hub partition");
            }
            finally
            {
                Logger.Trace($"Processed channels in {sw.ElapsedMilliseconds}ms");
            }
        }

        #region Alarm Configuration

        private void LoadAlarmConfiguration(object obj)
        {
            try
            {
                //var cf = new BigMissionDbContextFactory();
                //using var context = cf.CreateDbContext(new[] { Config["ConnectionString"] });
                var alarmConfig = context.CarAlarms
                    //.Where(a => !a.IsDeleted && a.IsEnabled)
                    .Include(a => a.Conditions)
                    .Include(a => a.Triggers)
                    .ToArray();

                Logger.Info($"Loaded {alarmConfig.Count()} Alarms");
                
                var delKeys = new List<RedisKey>();
                foreach (var ac in alarmConfig)
                {
                    var alarmKey = string.Format(Consts.ALARM_STATUS, ac.Id);
                    delKeys.Add(alarmKey);
                }
                Logger.Info($"Clearing {delKeys.Count()} alarm status");

                var deviceAppIds = context.DeviceAppConfig.Where(d => !d.IsDeleted).Select(d => d.Id).ToArray();
                foreach (var dai in deviceAppIds)
                {
                    delKeys.Add(string.Format(Consts.ALARM_CH_CONDS, dai));
                }
                Logger.Info($"Clearing {deviceAppIds.Count()} alarm channel status");

                var cache = cacheMuxer.GetDatabase();
                cache.KeyDelete(delKeys.ToArray(), CommandFlags.FireAndForget);
                
                // Filter down to active alarms
                alarmConfig = alarmConfig.Where(a => !a.IsDeleted && a.IsEnabled).ToArray();
                Logger.Debug($"Found {alarmConfig.Count()} enabled alarms");
                var als = new List<AlarmStatus>();
                foreach (var ac in alarmConfig)
                {
                    var a = new AlarmStatus(ac, Config["ConnectionString"], Logger, cacheMuxer, channelContext);
                    als.Add(a);
                }

                // Group the alarms by the targeted channel for alarm progression support, e.g. info, warning, error
                var grps = als.GroupBy(a => a.AlarmGroup);

                lock (alarmStatus)
                {
                    alarmStatus.Clear();
                    foreach (var chGrp in grps)
                    {
                        if (!alarmStatus.TryGetValue(chGrp.Key, out List<AlarmStatus> channelAlarms))
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

        /// <summary>
        /// When a change to devices or channels is received invalidate and reload.
        /// </summary>
        /// <param name="command"></param>
        private void ProcessConfigurationChange(KeyValuePair<string, string> command)
        {
            if (command.Key == ConfigurationCommandTypes.CHANNEL_MODIFIED || command.Key == ConfigurationCommandTypes.DEVICE_MODIFIED)
            {
                channelContext.Invalidate();
            }
            else if (command.Key == ConfigurationCommandTypes.ALARM_CHANGED)
            {
                LoadAlarmConfiguration(null);
            }
        }
    }
}
