using BigMission.EntityFrameworkCore;
using BigMission.RaceManagement;
using NLog;
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
        private List<ConditionStatus> conditionStatus = new List<ConditionStatus>();
        private ILogger Logger { get; }

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


        public AlarmStatus(CarAlarms alarm, string connectionString, ILogger logger)
        {
            Alarm = alarm ?? throw new ArgumentNullException();
            ConnectionString = connectionString ?? throw new ArgumentNullException();
            Logger = logger ?? throw new ArgumentNullException();

            InitializeConditions(alarm.Conditions.ToArray());

            var cf = new BigMissionDbContextFactory();
            context = cf.CreateDbContext(new[] { connectionString });
        }


        public void InitializeConditions(AlarmCondition[] conditions)
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
                var cs = new ConditionStatus(cond, ConnectionString);
                conditionStatus.Add(cs);
            }
        }

        public bool CheckConditions(ChannelStatus[] channelStatus)
        {
            var results = new List<bool>();
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

            bool alarmOn = false;
            if (Alarm.ConditionOption == ALL)
            {
                alarmOn = results.All(r => r);
            }
            else if (Alarm.ConditionOption == ANY)
            {
                alarmOn = results.Any(r => r);
            }

            UpdateStatus(alarmOn, true);

            return alarmOn;
        }

        private void UpdateStatus(bool alarmOn, bool applyTriggers)
        {
            Logger.Trace($"Alarm {Alarm.Name} conditions result: {alarmOn}");
            var alarmStatus = context.CarAlarmStatus.FirstOrDefault(a => a.AlarmId == Alarm.Id);

            // Turn alarm off if it's active
            if (alarmStatus != null && !alarmOn)
            {
                Logger.Trace($"Alarm {Alarm.Name} turning off");
                context.CarAlarmStatus.Remove(alarmStatus);

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
            else if (alarmStatus == null && alarmOn)
            {
                Logger.Trace($"Alarm {Alarm.Name} turning on");

                var row = new CarAlarmStatus { AlarmId = Alarm.Id, ActiveTimestamp = DateTime.UtcNow };
                context.CarAlarmStatus.Add(row);

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
                    var cf = new BigMissionDbContextFactory();
                    using var db = cf.CreateDbContext(new[] { ConnectionString });

                    Logger.Trace($"Alarm {Alarm.Name} trigger {trigger.TriggerType} setting to active");

                    // Dashboard highlight
                    if (trigger.TriggerType == AlarmTriggerType.HIGHLIGHT_COLOR)
                    {
                        // At the moment, use the first condition's channel
                        var ch = Alarm.Conditions.First();
                        var chStatusRow = db.ChannelStatus.FirstOrDefault(c => c.ChannelId == ch.ChannelId);
                        if (chStatusRow != null)
                        {
                            chStatusRow.AlarmMetadata = trigger.Color;
                            db.SaveChanges();
                            Logger.Trace($"Alarm {Alarm.Name} trigger {trigger.TriggerType} setting to active finished");
                        }
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
                    var cf = new BigMissionDbContextFactory();
                    using var db = cf.CreateDbContext(new[] { ConnectionString });

                    Logger.Trace($"Alarm {Alarm.Name} trigger {trigger.TriggerType} setting to off");

                    // Dashboard highlight
                    if (trigger.TriggerType == AlarmTriggerType.HIGHLIGHT_COLOR)
                    {
                        // At the moment, use the first condition's channel
                        var ch = Alarm.Conditions.First();
                        var chStatusRow = db.ChannelStatus.FirstOrDefault(c => c.ChannelId == ch.ChannelId);
                        if (chStatusRow != null)
                        {
                            chStatusRow.AlarmMetadata = string.Empty;
                            db.SaveChanges();
                            Logger.Trace($"Alarm {Alarm.Name} trigger {trigger.TriggerType} setting to off finished");
                        }
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
