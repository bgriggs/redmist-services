using BigMission.Cache.Models;
using BigMission.EntityFrameworkCore;
using BigMission.RaceManagement;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using NLog;
using StackExchange.Redis;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using static BigMission.RaceHeroSdk.RaceHeroClient;

namespace BigMission.RaceHeroAggregator
{
    class FlagStatus
    {
        private ILogger Logger { get; }
        private IConfiguration Config { get; }
        private readonly ConnectionMultiplexer cacheMuxer;
        private readonly List<EventFlag> eventFlags = new List<EventFlag>();
        private readonly int eventId;

        public FlagStatus(int eventId, ILogger logger, IConfiguration config, ConnectionMultiplexer cacheMuxer)
        {
            this.eventId = eventId;
            Logger = logger;
            Config = config;
            this.cacheMuxer = cacheMuxer;
        }

        public async Task ProcessFlagStatus(Flag flag)
        {
            if (eventFlags.Count == 0)
            {
                var ef = AddNewFlag(flag);

                var cacheTask = UpdateCache();
                var cf = new BigMissionDbContextFactory();
                using var db = cf.CreateDbContext(new[] { Config["ConnectionString"] });
                db.EventFlags.Add(ef);
                await db.SaveChangesAsync();
                await cacheTask;
                Logger.Trace($"Saved event flag update {flag}");
            }
            else
            {
                var currentFlag = eventFlags.Last();
                if (currentFlag.Flag != flag.ToString())
                {
                    Logger.Trace($"Processing flag change from {currentFlag.Flag} to {flag}");
                    currentFlag.End = DateTime.UtcNow;
                    var ef = AddNewFlag(flag);

                    // Save changes
                    var cacheTask = UpdateCache();
                    var cf = new BigMissionDbContextFactory();
                    using var db = cf.CreateDbContext(new[] { Config["ConnectionString"] });
                    db.Update(currentFlag);
                    db.EventFlags.Add(ef);
                    await db.SaveChangesAsync();
                    await cacheTask;
                    Logger.Trace($"Saved flag change from {currentFlag.Flag} to {flag}");
                }
            }
        }

        public async Task EndEvent()
        {
            var currentFlag = eventFlags.Last();
            if (currentFlag.End == null)
            {
                currentFlag.End = DateTime.UtcNow;

                // Save changes
                var cacheTask = UpdateCache();
                var cf = new BigMissionDbContextFactory();
                using var db = cf.CreateDbContext(new[] { Config["ConnectionString"] });
                db.Update(currentFlag);
                await db.SaveChangesAsync();
                await cacheTask;
                Logger.Trace($"Saved event {eventId} end");
            }
        }

        private EventFlag AddNewFlag(Flag flag)
        {
            var ef = new EventFlag { EventId = eventId, Flag = flag.ToString(), Start = DateTime.UtcNow };
            eventFlags.Add(ef);
            return ef;
        }

        private async Task UpdateCache()
        {
            var key = string.Format(Consts.EVENT_FLAGS, eventId);
            var json = JsonConvert.SerializeObject(eventFlags);
            var cache = cacheMuxer.GetDatabase();
            await cache.StringSetAsync(key, json);
        }
    }
}
