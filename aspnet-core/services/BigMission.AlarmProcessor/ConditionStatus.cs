using BigMission.Cache;
using BigMission.Cache.Models;
using BigMission.EntityFrameworkCore;
using BigMission.RaceManagement;
using Newtonsoft.Json;
using StackExchange.Redis;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace BigMission.AlarmProcessor
{
    class ConditionStatus : IDisposable, IAsyncDisposable
    {
        public RaceManagement.AlarmCondition ConditionConfig { get; }
        private readonly BigMissionDbContext context;
        private bool disposed;
        private ConnectionMultiplexer cacheMuxer;


        public ConditionStatus(RaceManagement.AlarmCondition conditionConfig, string connectionString, ConnectionMultiplexer cacheMuxer)
        {
            if (conditionConfig == null || connectionString == null) { throw new ArgumentNullException(); }
            ConditionConfig = conditionConfig;

            var cf = new BigMissionDbContextFactory();
            context = cf.CreateDbContext(new[] { connectionString });
            this.cacheMuxer = cacheMuxer;
        }


        public bool CheckConditions(RaceManagement.ChannelStatus[] channelStatus)
        {
            bool conditionActive = false;
            var chstatus = channelStatus.FirstOrDefault(s => s.ChannelId == ConditionConfig.ChannelId);
            if (chstatus != null)
            {
                int.TryParse(ConditionConfig.OnFor, out int onfor);

                var cache = cacheMuxer.GetDatabase();
                var rv = cache.StringGet(string.Format(Consts.ALARM_CONDS, ConditionConfig.Id));
                Cache.Models.AlarmCondition condStatus = null;
                if (rv.HasValue)
                {
                    condStatus = JsonConvert.DeserializeObject<Cache.Models.AlarmCondition>(rv);
                }

                var conditionState = EvalCondition(chstatus);

                // See if this is newly turned on
                if (conditionState && !rv.HasValue)
                {
                    var row = new Cache.Models.AlarmCondition { ActiveTimestamp = DateTime.UtcNow };
                    row.OnForMet = onfor == 0;
                    var str = JsonConvert.SerializeObject(row);
                    cache.StringSet(string.Format(Consts.ALARM_CONDS, ConditionConfig.Id), str);
                    conditionActive = row.OnForMet;
                }
                // When active, see if it has been on for the requisite duration
                else if (conditionState && condStatus != null && !condStatus.OnForMet)
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
                    cache.KeyDelete(string.Format(Consts.ALARM_CONDS, ConditionConfig.Id));
                }
            }

            return conditionActive;
        }

        private bool EvalCondition(RaceManagement.ChannelStatus channelStatus)
        {
            var configVal = float.Parse(ConditionConfig.ChannelValue);
            if (ConditionConfig.ConditionType == AlarmConditionType.EQUALS)
            {
                return channelStatus.Value == configVal;
            }
            if (ConditionConfig.ConditionType == AlarmConditionType.GREATER_THAN)
            {
                return channelStatus.Value > configVal;
            }
            if (ConditionConfig.ConditionType == AlarmConditionType.LESS_THAN)
            {
                return channelStatus.Value < configVal;
            }
            if (ConditionConfig.ConditionType == AlarmConditionType.NOT_EQUAL)
            {
                return channelStatus.Value != configVal;
            }
            throw new NotImplementedException("Unknown condition: " + ConditionConfig.ConditionType);
        }

        #region Dispose

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposed)
                return;

            if (disposing)
            {
                context.Dispose();
            }

            disposed = true;
        }

        public virtual ValueTask DisposeAsync()
        {
            try
            {
                return context.DisposeAsync(); 
            }
            catch (Exception exception)
            {
                return new ValueTask(Task.FromException(exception));
            }
        }

        #endregion
    }
}
