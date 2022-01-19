using BigMission.Cache.Models;
using BigMission.Cache.Models.Flags;
using Newtonsoft.Json;
using StackExchange.Redis;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BigMission.FuelStatistics
{
    internal class FlagContext : IFlagContext
    {
        private readonly ConnectionMultiplexer cacheMuxer;

        public FlagContext(ConnectionMultiplexer cacheMuxer)
        {
            this.cacheMuxer = cacheMuxer;
        }

        public async Task<List<EventFlag>> GetFlags(int rhEventId)
        {
            var cache = cacheMuxer.GetDatabase();
            var flags = new List<EventFlag>();
            var key = string.Format(Consts.EVENT_FLAGS, rhEventId);
            var json = await cache.StringGetAsync(key);
            if (!string.IsNullOrEmpty(json))
            {
                var f = JsonConvert.DeserializeObject<List<EventFlag>>(json);
                if (f != null)
                {
                    flags = f;
                }
            }
            return flags;
        }
    }
}
