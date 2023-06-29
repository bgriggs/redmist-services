using BigMission.Cache.Models;
using Newtonsoft.Json;
using StackExchange.Redis;

namespace BigMission.RaceControlLog.EventStatus
{
    /// <summary>
    /// Gets a race hero event data from local cache.
    /// </summary>
    internal class EventRedisStatus : IEventStatus
    {
        private readonly IConnectionMultiplexer cacheMuxer;

        public EventRedisStatus(IConnectionMultiplexer cacheMuxer)
        {
            this.cacheMuxer = cacheMuxer;
        }

        public async Task<RaceHeroEventStatus> GetEventStatusAsync(int rhEventId)
        {
            var cache = cacheMuxer.GetDatabase();
            var key = string.Format(Consts.EVENT_STATUS, rhEventId);
            var json = await cache.StringGetAsync(key);
            return JsonConvert.DeserializeObject<RaceHeroEventStatus?>(json);
        }
    }
}
