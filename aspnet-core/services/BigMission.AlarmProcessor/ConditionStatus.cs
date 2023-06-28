using BigMission.Cache.Models;
using BigMission.DeviceApp.Shared;
using Newtonsoft.Json;
using StackExchange.Redis;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace BigMission.AlarmProcessor
{
    class ConditionStatus
    {
        public Database.Models.AlarmCondition ConditionConfig { get; }
        private readonly IConnectionMultiplexer cacheMuxer;
        private bool? lastConditionActive;

        public static string GREATER_THAN = "GreaterThan";
        public static string LESS_THAN = "LessThan";
        public static string EQUALS = "Equals";
        public static string NOT_EQUAL = "NotEqual";


        public ConditionStatus(Database.Models.AlarmCondition conditionConfig, IConnectionMultiplexer cacheMuxer)
        {
            if (conditionConfig == null || cacheMuxer == null) { throw new ArgumentNullException(); }
            ConditionConfig = conditionConfig;
            this.cacheMuxer = cacheMuxer;
        }


        public async Task<bool?> CheckConditions(ChannelStatusDto[] channelStatus)
        {
            bool conditionActive = false;
            var chstatus = channelStatus.FirstOrDefault(s => s.ChannelId == ConditionConfig.ChannelId);
            if (chstatus != null)
            {
                int.TryParse(ConditionConfig.OnFor, out int onfor);

                var cache = cacheMuxer.GetDatabase();
                var rv = await cache.StringGetAsync(string.Format(Consts.ALARM_CONDS, ConditionConfig.Id));
                AlarmCondition condStatus = null;
                if (rv.HasValue)
                {
                    condStatus = JsonConvert.DeserializeObject<AlarmCondition>(rv);
                }

                var conditionState = EvalCondition(chstatus);

                // See if this is newly turned on
                if (conditionState && !rv.HasValue)
                {
                    var row = new Cache.Models.AlarmCondition { ActiveTimestamp = DateTime.UtcNow };
                    row.OnForMet = onfor == 0;
                    var str = JsonConvert.SerializeObject(row);
                    await cache.StringSetAsync(string.Format(Consts.ALARM_CONDS, ConditionConfig.Id), str);
                    conditionActive = row.OnForMet;
                }
                // When active, see if it has been on for the requisite duration
                else if (conditionState && condStatus != null && condStatus.OnForMet)
                {
                    if (onfor > 0)
                    {
                        var now = DateTime.UtcNow;
                        if (now - condStatus.ActiveTimestamp > TimeSpan.FromMilliseconds(onfor))
                        {
                            condStatus.OnForMet = true;
                        }
                    }
                    conditionActive = condStatus.OnForMet;
                }
                // No longer on, clear out
                else if (!conditionState && condStatus != null)
                {
                    await cache.KeyDeleteAsync(string.Format(Consts.ALARM_CONDS, ConditionConfig.Id));
                }
                lastConditionActive = conditionActive;
                return conditionActive;
            }

            // If there was no update for this channel use the last state when available.
            if (lastConditionActive.HasValue)
            {
                return lastConditionActive;
            }

            return null;
        }

        private bool EvalCondition(ChannelStatusDto channelStatus)
        {
            var configVal = float.Parse(ConditionConfig.ChannelValue);
            if (ConditionConfig.ConditionType == EQUALS)
            {
                return channelStatus.Value == configVal;
            }
            if (ConditionConfig.ConditionType == GREATER_THAN)
            {
                return channelStatus.Value > configVal;
            }
            if (ConditionConfig.ConditionType == LESS_THAN)
            {
                return channelStatus.Value < configVal;
            }
            if (ConditionConfig.ConditionType == NOT_EQUAL)
            {
                return channelStatus.Value != configVal;
            }
            throw new NotImplementedException("Unknown condition: " + ConditionConfig.ConditionType);
        }
    }
}
