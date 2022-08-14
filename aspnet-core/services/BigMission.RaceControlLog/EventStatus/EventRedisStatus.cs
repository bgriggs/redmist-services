using BigMission.Cache.Models;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using StackExchange.Redis;

namespace BigMission.RaceControlLog.EventStatus
{
    /// <summary>
    /// Gets a race hero event data from local cache.
    /// </summary>
    internal class EventRedisStatus : IEventStatus
    {
        private readonly ConnectionMultiplexer cacheMuxer;

        public EventRedisStatus(IConfiguration config)
        {
            cacheMuxer = ConnectionMultiplexer.Connect(config["RedisConn"]);
        }

        public async Task<RaceHeroEventStatus?> GetEventStatusAsync(int rhEventId)
        {
            var cache = cacheMuxer.GetDatabase();
            var key = string.Format(Consts.EVENT_STATUS, rhEventId);
            var json = await cache.StringGetAsync(key);
            return JsonConvert.DeserializeObject<RaceHeroEventStatus?>(json);
        }
    }
}
