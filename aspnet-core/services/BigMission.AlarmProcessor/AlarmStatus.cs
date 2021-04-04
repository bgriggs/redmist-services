using BigMission.Cache;
using BigMission.Cache.Models;
using BigMission.DeviceApp.Shared;
using BigMission.EntityFrameworkCore;
using BigMission.RaceManagement;
using NLog;
using StackExchange.Redis;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace BigMission.AlarmProcessor
{
    class AlarmStatus
    {
        public CarAlarms Alarm { get; }
        public string ConnectionString { get; }
        private readonly BigMissionDbContext context;
        private readonly List<ConditionStatus> conditionStatus = new List<ConditionStatus>();
        private ILogger Logger { get; }
        private readonly ConnectionMultiplexer cacheMuxer;
        private readonly ChannelContext channelContext;

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


        public AlarmStatus(CarAlarms alarm, string connectionString, ILogger logger, ConnectionMultiplexer cacheMuxer, ChannelContext channelContext)
        {
            Alarm = alarm ?? throw new ArgumentNullException();
            ConnectionString = connectionString ?? throw new ArgumentNullException();
            Logger = logger ?? throw new ArgumentNullException();
            this.cacheMuxer = cacheMuxer ?? throw new ArgumentNullException();
            this.channelContext = channelContext;

            var cf = new BigMissionDbContextFactory();
            context = cf.CreateDbContext(new[] { connectionString });

            InitializeConditions(alarm.Conditions.ToArray());
        }


        public void InitializeConditions(RaceManagement.AlarmCondition[] conditions)
        {
            foreach (var cond in conditionStatus)
            {
                try
                {
                    cond.Dispose();
                }
                catch { }
            }

            conditionStatus.Clear();

            foreach (var cond in conditions)
            {
                var cs = new ConditionStatus(cond, ConnectionString, cacheMuxer);
                conditionStatus.Add(cs);
            }
        }

        public bool CheckConditions(ChannelStatusDto[] channelStatus)
        {
            var results = new List<bool?>();
            Parallel.ForEach(conditionStatus, (condStatus) =>
            {
                try
                {
                    var result = condStatus.CheckConditions(channelStatus);
                    Logger.Trace($"Checking condition {condStatus.ConditionConfig.Id} is {result}");
                    results.Add(result);
                }
                catch (Exception ex)
                {
                    Logger.Error(ex, "Error updating condition status");
                }
            });

            if (results.Contains(null))
            {
                Logger.Trace($"Cannot check {Alarm.AlarmGroup}. Not all conditions can be checked.");
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

            UpdateStatus(alarmOn, true);

            return alarmOn;
        }

        private void UpdateStatus(bool alarmOn, bool applyTriggers)
        {
            Logger.Trace($"Alarm {Alarm.Name} conditions result: {alarmOn}");
            var cache = cacheMuxer.GetDatabase();
            var alarmKey = string.Format(Consts.ALARM_STATUS, Alarm.Id);
            var rv = cache.StringGet(alarmKey);

            // Turn alarm off if it's active
            if (rv.HasValue && !alarmOn)
            {
                Logger.Trace($"Alarm {Alarm.Name} turning off");
                cache.KeyDelete(alarmKey);

                // Log change
                var log = new CarAlarmLog { AlarmId = Alarm.Id, Timestamp = DateTime.UtcNow, IsActive = false };
                context.CarAlarmLog.Add(log);

                // Turn off applicable triggers
                if (applyTriggers)
                {
                    RemoveTriggers();
                }
            }
            // Turn alarm on if it's off
            else if (!rv.HasValue && alarmOn)
            {
                Logger.Trace($"Alarm {Alarm.Name} turning on");

                cache.StringSet(alarmKey, DateTime.UtcNow.ToString());

                // Log change
                var log = new CarAlarmLog { AlarmId = Alarm.Id, Timestamp = DateTime.UtcNow, IsActive = true };
                context.CarAlarmLog.Add(log);

                // Activate triggers when alarms turn on
                if (applyTriggers)
                {
                    ProcessTriggers();
                }
            }

            Logger.Trace($"Alarm {Alarm.Name} saving...");
            context.SaveChanges();
            Logger.Trace($"Alarm {Alarm.Name} saving finished");
        }

        private void ProcessTriggers()
        {
            Parallel.ForEach(Alarm.Triggers, (trigger) =>
            {
                try
                {
                    Logger.Trace($"Alarm {Alarm.Name} trigger {trigger.TriggerType} setting to active");

                    // Dashboard highlight
                    if (trigger.TriggerType == AlarmTriggerType.HIGHLIGHT_COLOR)
                    {
                        // At the moment, use the first condition's channel
                        var ch = Alarm.Conditions.First();
                        var cache = cacheMuxer.GetDatabase();
                        var deviceAppId = channelContext.GetDeviceAppId(ch.ChannelId);
                        cache.HashSet(string.Format(Consts.ALARM_CH_CONDS, deviceAppId), ch.ChannelId.ToString(), trigger.Color);
                        Logger.Trace($"Alarm {Alarm.Name} trigger {trigger.TriggerType} setting to active finished");
                    }
                    else
                    {
                        throw new NotImplementedException($"Trigger not implemented: {trigger.TriggerType}");
                    }
                }
                catch (Exception ex)
                {
                    Logger.Error(ex, "Error creating triggers");
                }
            });
        }

        private void RemoveTriggers()
        {
            Parallel.ForEach(Alarm.Triggers, (trigger) =>
            {
                try
                {
                    Logger.Trace($"Alarm {Alarm.Name} trigger {trigger.TriggerType} setting to off");

                    // Dashboard highlight
                    if (trigger.TriggerType == AlarmTriggerType.HIGHLIGHT_COLOR)
                    {
                        // At the moment, use the first condition's channel
                        var ch = Alarm.Conditions.First();
                        var cache = cacheMuxer.GetDatabase();
                        var deviceAppId = channelContext.GetDeviceAppId(ch.ChannelId);
                        cache.HashDelete(string.Format(Consts.ALARM_CH_CONDS, deviceAppId), ch.ChannelId.ToString());
                        Logger.Trace($"Alarm {Alarm.Name} trigger {trigger.TriggerType} setting to off finished");
                    }
                }
                catch (Exception ex)
                {
                    Logger.Error(ex, "Error creating triggers");
                }
            });
        }

        public void Supersede()
        {
            UpdateStatus(alarmOn: false, applyTriggers: false);
        }
    }
}
