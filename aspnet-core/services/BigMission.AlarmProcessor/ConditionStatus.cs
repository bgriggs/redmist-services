using BigMission.EntityFrameworkCore;
using BigMission.RaceManagement;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace BigMission.AlarmProcessor
{
    class ConditionStatus : IDisposable, IAsyncDisposable
    {
        public AlarmCondition ConditionConfig { get; }
        private readonly BigMissionDbContext context;
        private bool disposed;

        public ConditionStatus(AlarmCondition conditionConfig, string connectionString)
        {
            if (conditionConfig == null || connectionString == null) { throw new ArgumentNullException(); }
            ConditionConfig = conditionConfig;

            var cf = new BigMissionDbContextFactory();
            context = cf.CreateDbContext(new[] { connectionString });
        }


        public bool CheckConditions(ChannelStatus[] channelStatus)
        {
            bool conditionActive = false;
            var chstatus = channelStatus.FirstOrDefault(s => s.ChannelId == ConditionConfig.ChannelId);
            if (chstatus != null)
            {
                int.TryParse(ConditionConfig.OnFor, out int onfor);

                var condStatus = context.AlarmConditionStatus.FirstOrDefault(c => c.ConditionId == ConditionConfig.Id);
                var conditionState = EvalCondition(chstatus);

                // See if this is newly turned on
                if (conditionState && condStatus == null)
                {
                    var row = new AlarmConditionStatus { ConditionId = ConditionConfig.Id, ActiveTimestamp = DateTime.UtcNow };
                    row.OnForMet = onfor == 0;
                    context.AlarmConditionStatus.Add(row);
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
                    context.AlarmConditionStatus.Remove(condStatus);
                }

                context.SaveChanges();
            }

            return conditionActive;
        }

        private bool EvalCondition(ChannelStatus channelStatus)
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
