using BigMission.Cache.Models;
using BigMission.Cache.Models.ControlLog;
using BigMission.Database.Models;
using BigMission.RaceControlLog.Configuration;
using BigMission.TestHelpers;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using StackExchange.Redis;

namespace BigMission.RaceControlLog.LogProcessing
{
    /// <summary>
    /// Saves control log update to Redis.
    /// </summary>
    internal class CacheControlLog : ILogProcessor
    {
        public IDateTimeHelper DateTime { get; }
        private readonly ConnectionMultiplexer cacheMuxer;
        private List<RaceControlLogEntry> last = new();

        public CacheControlLog(IConfiguration config, IDateTimeHelper dateTime)
        {
            cacheMuxer = ConnectionMultiplexer.Connect(config["RedisConn"]);
            DateTime = dateTime;
        }


        public async Task Process(RaceEventSetting evt, IEnumerable<RaceControlLogEntry> log, ConfigurationEventData configurationEventData)
        {
            var logList = log.ToList();
            var changed = HasChanges(last, logList);
            if (changed)
            {
                last = logList;
                var cache = cacheMuxer.GetDatabase();
                var rcl = new Cache.Models.ControlLog.RaceControlLog { Timestamp = DateTime.UtcNow };
                rcl.Log.AddRange(log);
                var key = string.Format(Consts.CONTROL_LOG, evt.Id);
                var json = JsonConvert.SerializeObject(rcl);
                await cache.StringSetAsync(key, json);
            }
        }

        private static bool HasChanges(List<RaceControlLogEntry> last, List<RaceControlLogEntry> current)
        {
            if (last.Count != current.Count)
            {
                return true;
            }
            for (int i = 0; i < last.Count; i++)
            {
                if (last[i].HasChanged(current[i]))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
