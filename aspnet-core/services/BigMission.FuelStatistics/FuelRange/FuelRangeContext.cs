using AutoMapper;
using BigMission.Cache.Models;
using BigMission.Cache.Models.FuelRange;
using BigMission.Database;
using BigMission.Database.Models;
using Newtonsoft.Json;
using StackExchange.Redis;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace BigMission.FuelStatistics.FuelRange
{
    internal class FuelRangeContext : IFuelRangeContext
    {
        private string ConnectionString { get; }
        private ConnectionMultiplexer CacheMuxer { get; }
        private readonly IMapper objectMapper;

        public FuelRangeContext(string connectionString, ConnectionMultiplexer cacheMuxer)
        {
            ConnectionString = connectionString;
            CacheMuxer = cacheMuxer;
            var mapperConfiguration = new MapperConfiguration(cfg => 
            {
                cfg.CreateMap<FuelRangeStint, Cache.Models.FuelRange.Stint>();
                cfg.CreateMap<Cache.Models.FuelRange.Stint, FuelRangeStint>();
            });
            objectMapper = mapperConfiguration.CreateMapper();
        }



        public Task<List<Cache.Models.FuelRange.Stint>> GetTeamStints(int teamId, int eventId)
        {
            throw new NotImplementedException();
        }

        public Task<Cache.Models.FuelRange.Stint> LoadTeamStint(int stintId)
        {
            throw new NotImplementedException();
        }

        public Task<List<Cache.Models.FuelRange.Stint>> LoadTeamStints(int teamId, int eventId)
        {
            throw new NotImplementedException();
        }

        public Task PublishStintOverride(RangeUpdate stint)
        {
            throw new NotImplementedException();
        }

        public async Task<Cache.Models.FuelRange.Stint> SaveTeamStint(Cache.Models.FuelRange.Stint stint)
        {
            var dbstint = objectMapper.Map<FuelRangeStint>(stint);
            using var db = new RedMist(ConnectionString);
            db.FuelRangeStints.Add(dbstint);
            await db.SaveChangesAsync();
            return stint;
        }

        public async Task SubscribeToFuelStintOverrides(Func<RangeUpdate, Task> overrideHandler)
        {
            if (overrideHandler == null) { throw new ArgumentNullException(); }

            var sub = CacheMuxer.GetSubscriber();
            await sub.SubscribeAsync(Consts.FUEL_STINT_OVERRIDES, async (channel, message) =>
            {
                var stint = JsonConvert.DeserializeObject<RangeUpdate>(message);
                await overrideHandler(stint);
            });
        }

        public async Task UpdateTeamStints(List<Cache.Models.FuelRange.Stint> stints)
        {
            if (stints != null)
            {
                var teamId = stints.First().TenantId;
                var cache = CacheMuxer.GetDatabase();
                var key = string.Format(Consts.FUEL_RANGE_STATUS, teamId);

                var json = JsonConvert.SerializeObject(stints);
                await cache.StringSetAsync(key, json);

                if (stints.Count > 0)
                {
                    // Save to DB
                    var dbstints = objectMapper.Map<List<FuelRangeStint>>(stints);
                    using var db = new RedMist(ConnectionString);
                    db.UpdateRange(dbstints);
                    await db.SaveChangesAsync();
                }
            }
        }
    }
}
