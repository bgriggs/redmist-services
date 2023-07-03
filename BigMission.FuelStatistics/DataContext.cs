using BigMission.Cache.Models;
using BigMission.Database;
using BigMission.Database.Models;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using StackExchange.Redis;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace BigMission.FuelStatistics
{
    class DataContext : IDataContext
    {
        private readonly IConnectionMultiplexer cacheMuxer;
        private readonly IDbContextFactory<RedMist> dbFactory;
        private RedMist batchDbContext;


        public DataContext(IConnectionMultiplexer cacheMuxer, IDbContextFactory<RedMist> dbFactory)
        {
            this.cacheMuxer = cacheMuxer;
            this.dbFactory = dbFactory;
        }

        #region Database

        /// <summary>
        /// Maintains connection across calls.
        /// </summary>
        public async Task StartBatch()
        {
            if (batchDbContext != null)
            {
                throw new InvalidOperationException("Batch already open");
            }
            batchDbContext = await dbFactory.CreateDbContextAsync();
        }

        /// <summary>
        /// Closes connection.
        /// </summary>
        public async Task EndBatch()
        {
            var context = batchDbContext;
            if (context != null)
            {
                batchDbContext = null;
                await context.DisposeAsync();
            }
        }

        private async Task<RedMist> GetDb()
        {
            if (batchDbContext != null)
            {
                return batchDbContext;
            }

            return await dbFactory.CreateDbContextAsync();
        }

        public async Task<List<Lap>> GetSavedLaps(int eventId)
        {
            var db = await GetDb();
            try
            {
                int latestRunId = 0;
                var runIds = await db.CarRaceLaps.Where(l => l.EventId == eventId).Select(p => p.RunId).ToListAsync();
                if (runIds.Any())
                {
                    latestRunId = runIds.Max();
                }

                return await db.CarRaceLaps
                    .Where(l => l.EventId == eventId && l.RunId == latestRunId)
                    .Select(l => new Lap
                    {
                        EventId = eventId,
                        RunId = l.RunId,
                        CarNumber = l.CarNumber,
                        Timestamp = l.Timestamp,
                        ClassName = l.ClassName,
                        PositionInRun = l.PositionInRun,
                        CurrentLap = l.CurrentLap,
                        LastLapTimeSeconds = l.LastLapTimeSeconds,
                        LastPitLap = l.LastPitLap,
                        PitStops = l.PitStops,
                        Flag = l.Flag
                    })
                    .ToListAsync();
            }
            finally
            {
                if (batchDbContext == null)
                {
                    await db.DisposeAsync();
                }
            }
        }

        public async Task<List<FuelRangeSetting>> GetFuelRangeSettings(int[] carIds)
        {
            var db = await GetDb();
            try
            {
                return await db.FuelRangeSettings.Where(c => carIds.Contains(c.CarId)).ToListAsync();
            }
            finally
            {
                if (batchDbContext == null)
                {
                    await db.DisposeAsync();
                }
            }
        }

        public async Task<List<DeviceAppConfig>> GetDeviceAppConfig(int[] carIds)
        {
            var db = await GetDb();
            try
            {
                return await db.DeviceAppConfigs.Where(d => d.CarId.HasValue && carIds.Contains(d.CarId.Value)).ToListAsync();
            }
            finally
            {
                if (batchDbContext == null)
                {
                    await db.DisposeAsync();
                }
            }
        }

        public async Task<List<Database.Models.Car>> GetCars(int[] carIds)
        {
            var db = await GetDb();
            try
            {
                return await db.Cars.Where(c => !c.IsDeleted && carIds.Contains(c.Id)).ToListAsync();
            }
            finally
            {
                if (batchDbContext == null)
                {
                    await db.DisposeAsync();
                }
            }
        }

        public async Task<List<ChannelMapping>> GetChannelMappings(int[] deviceAppIds, string[] channelNames)
        {
            var db = await GetDb();
            try
            {
                return await db.ChannelMappings.Where(ch => deviceAppIds.Contains(ch.DeviceAppId) && channelNames.Contains(ch.ReservedName)).ToListAsync();
            }
            finally
            {
                if (batchDbContext == null)
                {
                    await db.DisposeAsync();
                }
            }
        }

        public async Task<List<FuelRangeStint>> GetTeamStints(int teamId, int rhEventId)
        {
            var db = await GetDb();
            try
            {
                var latestRunId = await db.FuelRangeStints.Where(s => s.TenantId == teamId && s.EventId == rhEventId).Select(p => p.RunId).DefaultIfEmpty(0).MaxAsync();
                return await db.FuelRangeStints.Where(s => s.TenantId == teamId && s.EventId == rhEventId && s.RunId == latestRunId).ToListAsync();
            }
            finally
            {
                if (batchDbContext == null)
                {
                    await db.DisposeAsync();
                }
            }
        }

        public async Task<List<RaceEventSetting>> GetEventSettings()
        {
            var db = await GetDb();
            try
            {
                return await db.RaceEventSettings
                    .Where(s => !s.IsDeleted && s.IsEnabled)
                    .ToListAsync();
            }
            finally
            {
                if (batchDbContext == null)
                {
                    await db.DisposeAsync();
                }
            }
        }

        #endregion

        #region Cache

        public async Task UpdateCarStatus(Car car, int rhEventId)
        {
            var cache = cacheMuxer.GetDatabase();
            var eventKey = string.Format(Consts.FUEL_STAT, rhEventId);
            var carJson = JsonConvert.SerializeObject(car);
            await cache.HashSetAsync(eventKey, car.Number, carJson);
        }

        public async Task<DateTime?> CheckReload(int teamId)
        {
            var cache = cacheMuxer.GetDatabase();
            var key = string.Format(Consts.FUEL_RANGE_STATUS_UPDATED, teamId);
            var dtstr = await cache.StringGetAsync(key);
            if (!string.IsNullOrEmpty(dtstr))
            {
                return DateTime.Parse(dtstr).ToUniversalTime();
            }
            return null;
        }

        public async Task<List<Lap>> PopEventLaps(int rhEventId)
        {
            var cache = cacheMuxer.GetDatabase();
            var laps = new List<Lap>();
            var key = string.Format(Consts.LAPS_FUEL_STAT, rhEventId);
            Lap lap;
            do
            {
                lap = null;
                var lapJson = await cache.ListRightPopAsync(key);
                if (!string.IsNullOrEmpty(lapJson))
                {
                    var racer = JsonConvert.DeserializeObject<CarRaceLap>(lapJson);
                    lap = new Lap
                    {
                        EventId = rhEventId,
                        RunId = racer.RunId,
                        Timestamp = racer.Timestamp,
                        CarNumber = racer.CarNumber,
                        ClassName = racer.ClassName,
                        CurrentLap = racer.CurrentLap,
                        LastLapTimeSeconds = racer.LastLapTimeSeconds,
                        LastPitLap = racer.LastPitLap,
                        PitStops = racer.PitStops,
                        PositionInRun = racer.PositionInRun,
                        Flag = racer.Flag
                    };
                    laps.Add(lap);
                }

            } while (lap != null);

            return laps;
        }

        public async Task ClearCachedEvent(int rhEventId)
        {
            var cache = cacheMuxer.GetDatabase();
            var hashKey = string.Format(Consts.FUEL_STAT, rhEventId);
            var ehash = await cache.HashGetAllAsync(hashKey);
            foreach (var ckey in ehash)
            {
                await cache.HashDeleteAsync(hashKey, ckey.Name.ToString());
            }
        }

        #endregion
    }
}
