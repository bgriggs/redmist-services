using BigMission.Cache.Models;
using BigMission.Database;
using BigMission.Database.Models;
using BigMission.DeviceApp.Shared;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace BigMission.AlarmProcessor
{
    class AlarmStatus
    {
        public CarAlarm Alarm { get; }
        public string ConnectionString { get; }
        private readonly List<ConditionStatus> conditionStatus = new();
        private ILogger Logger { get; }
        private readonly IConnectionMultiplexer cacheMuxer;
        private readonly Func<int, Task<int>> getDeviceId;
        //private readonly Dictionary<int, int[]> deviceToChannelMappings;
        public const string HIGHLIGHT_COLOR = "HighlightColor";

        public string AlarmGroup
        {
            get { return Alarm.AlarmGroup; }
        }

        public int Priority
        {
            get { return Alarm.Order; }
        }

        private const string ALL = "All";
        private const string ANY = "Any";


        public AlarmStatus(CarAlarm alarm, string connectionString, ILogger logger, IConnectionMultiplexer cacheMuxer, Func<int, Task<int>> getDeviceId)
        {
            Alarm = alarm ?? throw new ArgumentNullException();
            ConnectionString = connectionString ?? throw new ArgumentNullException();
            Logger = logger ?? throw new ArgumentNullException();
            this.cacheMuxer = cacheMuxer ?? throw new ArgumentNullException();
            this.getDeviceId = getDeviceId;
            //this.deviceToChannelMappings = deviceToChannelMappings;

            InitializeConditions(alarm.AlarmConditions.ToArray());
        }


        public void InitializeConditions(Database.Models.AlarmCondition[] conditions)
        {
            conditionStatus.Clear();

            foreach (var cond in conditions)
            {
                var cs = new ConditionStatus(cond, cacheMuxer);
                conditionStatus.Add(cs);
            }
        }

        public async Task<bool> CheckConditions(ChannelStatusDto[] channelStatus)
        {
            var results = new List<bool?>();
            var conditionTasks = conditionStatus.Select(async condStatus =>
            {
                try
                {
                    var result = await condStatus.CheckConditions(channelStatus);
                    if (result != null)
                    {
                        Logger.LogTrace($"Checking condition {condStatus.ConditionConfig.Id} is {result}");
                    }
                    else
                    {
                        Logger.LogTrace($"Checking condition {condStatus.ConditionConfig.Id} is <null>");
                    }
                    results.Add(result);
                }
                catch (Exception ex)
                {
                    Logger.LogError(ex, "Error updating condition status");
                }
            });
            await Task.WhenAll(conditionTasks);

            if (results.Contains(null))
            {
                Logger.LogTrace($"Cannot check {Alarm.AlarmGroup}. Not all conditions can be checked.");
                return false;
            }

            bool alarmOn = false;
            if (Alarm.ConditionOption == ALL)
            {
                alarmOn = results.All(r => r.Value);
            }
            else if (Alarm.ConditionOption == ANY)
            {
                alarmOn = results.Any(r => r.Value);
            }

            await UpdateStatus(alarmOn, true);

            return alarmOn;
        }

        private async Task UpdateStatus(bool alarmOn, bool applyTriggers)
        {
            Logger.LogTrace($"Alarm {Alarm.Name} conditions result: {alarmOn}");
            var cache = cacheMuxer.GetDatabase();
            var alarmKey = string.Format(Consts.ALARM_STATUS, Alarm.Id);
            var rv = await cache.StringGetAsync(alarmKey);

            // Turn alarm off if it's active
            using var context = new RedMist(ConnectionString);
            if (rv.HasValue && !alarmOn)
            {
                Logger.LogTrace($"Alarm {Alarm.Name} turning off");
                await cache.KeyDeleteAsync(alarmKey);

                // Log change
                var log = new CarAlarmLog { AlarmId = Alarm.Id, Timestamp = DateTime.UtcNow, IsActive = false };
                context.CarAlarmLogs.Add(log);

                // Turn off applicable triggers
                if (applyTriggers)
                {
                    await RemoveTriggers();
                }
            }
            // Turn alarm on if it's off
            else if (!rv.HasValue && alarmOn)
            {
                Logger.LogTrace($"Alarm {Alarm.Name} turning on");

                await cache.StringSetAsync(alarmKey, DateTime.UtcNow.ToString());

                // Log change
                var log = new CarAlarmLog { AlarmId = Alarm.Id, Timestamp = DateTime.UtcNow, IsActive = true };
                context.CarAlarmLogs.Add(log);

                // Activate triggers when alarms turn on
                if (applyTriggers)
                {
                    await ProcessTriggers();
                }
            }

            Logger.LogTrace($"Alarm {Alarm.Name} saving...");
            await context.SaveChangesAsync();
            Logger.LogTrace($"Alarm {Alarm.Name} saving finished");
        }

        private async Task ProcessTriggers()
        {
            var triggerTasks = Alarm.AlarmTriggers.Select(async trigger =>
            {
                try
                {
                    Logger.LogTrace($"Alarm {Alarm.Name} trigger {trigger.TriggerType} setting to active");

                    // Dashboard highlight
                    if (trigger.TriggerType == HIGHLIGHT_COLOR)
                    {
                        // At the moment, use the first condition's channel
                        var ch = Alarm.AlarmConditions.First();
                        var cache = cacheMuxer.GetDatabase();
                        var deviceAppId = await getDeviceId(ch.ChannelId);
                        await cache.HashSetAsync(string.Format(Consts.ALARM_CH_CONDS, deviceAppId), ch.ChannelId.ToString(), trigger.Color);
                        Logger.LogTrace($"Alarm {Alarm.Name} trigger {trigger.TriggerType} setting to active finished");
                    }
                    else
                    {
                        throw new NotImplementedException($"Trigger not implemented: {trigger.TriggerType}");
                    }
                }
                catch (Exception ex)
                {
                    Logger.LogError(ex, "Error creating triggers");
                }
            });

            await Task.WhenAll(triggerTasks);
        }

        private async Task RemoveTriggers()
        {
            var triggerTasks = Alarm.AlarmTriggers.Select(async trigger =>
            {
                try
                {
                    Logger.LogTrace($"Alarm {Alarm.Name} trigger {trigger.TriggerType} setting to off");

                    // Dashboard highlight
                    if (trigger.TriggerType == HIGHLIGHT_COLOR)
                    {
                        // At the moment, use the first condition's channel
                        var ch = Alarm.AlarmConditions.First();
                        var cache = cacheMuxer.GetDatabase();
                        var deviceAppId = await getDeviceId(ch.ChannelId);
                        await cache.HashDeleteAsync(string.Format(Consts.ALARM_CH_CONDS, deviceAppId), ch.ChannelId.ToString());
                        Logger.LogTrace($"Alarm {Alarm.Name} trigger {trigger.TriggerType} setting to off finished");
                    }
                }
                catch (Exception ex)
                {
                    Logger.LogError(ex, "Error creating triggers");
                }
            });
            await Task.WhenAll(triggerTasks);
        }

        public async Task Supersede()
        {
            await UpdateStatus(alarmOn: false, applyTriggers: false);
        }

        //private int GetDeviceAppId(int channelId)
        //{
        //    foreach (var kvp in deviceToChannelMappings)
        //    {
        //        if (kvp.Value.Contains(channelId))
        //        {
        //            return kvp.Key;
        //        }
        //    }
        //    return 0;
        //}
    }
}
