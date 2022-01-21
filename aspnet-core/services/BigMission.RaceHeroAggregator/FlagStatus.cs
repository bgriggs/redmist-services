using BigMission.Cache.Models;
using BigMission.Cache.Models.Flags;
using BigMission.Database;
using BigMission.TestHelpers;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using NLog;
using StackExchange.Redis;
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
        private IDateTimeHelper DateTime { get; }

        private readonly ConnectionMultiplexer cacheMuxer;
        private readonly List<EventFlag> eventFlags = new();
        private readonly int eventId;

        public FlagStatus(int eventId, ILogger logger, IConfiguration config, ConnectionMultiplexer cacheMuxer, IDateTimeHelper dateTime)
        {
            this.eventId = eventId;
            Logger = logger;
            Config = config;
            this.cacheMuxer = cacheMuxer;
            DateTime = dateTime;
        }

        public async Task ProcessFlagStatus(Flag flag, int runId)
        {
            using var db = new RedMist(Config["ConnectionString"]);
            if (eventFlags.Count == 0)
            {
                var ef = AddNewFlag(flag, runId);
                var cacheTask = UpdateCache();                
                db.EventFlags.Add(ConvertFlag(ef));
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
                    var ef = AddNewFlag(flag, runId);

                    // Save changes
                    var cacheTask = UpdateCache();
                    db.Update(ConvertFlag(currentFlag));
                    db.EventFlags.Add(ConvertFlag(ef));
                    await db.SaveChangesAsync();
                    await cacheTask;
                    Logger.Trace($"Saved flag change from {currentFlag.Flag} to {flag}");
                }
            }
        }

        public async Task EndEvent()
        {
            var currentFlag = eventFlags.LastOrDefault();
            if (currentFlag != null && currentFlag.End == null)
            {
                currentFlag.End = DateTime.UtcNow;

                // Save changes
                var cacheTask = UpdateCache();
                using var db = new RedMist(Config["ConnectionString"]);
                db.Update(ConvertFlag(currentFlag));
                await db.SaveChangesAsync();
                await cacheTask;
                Logger.Trace($"Saved event {eventId} end");
            }
        }

        private EventFlag AddNewFlag(Flag flag, int runId)
        {
            var ef = new EventFlag { EventId = eventId, RunId = runId, Flag = flag.ToString(), Start = DateTime.UtcNow };
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

        private static Database.Models.EventFlag ConvertFlag(EventFlag cf)
        {
            return new Database.Models.EventFlag
            {
                Id = cf.Id,
                EventId = cf.EventId,
                RunId = cf.RunId,
                Start = cf.Start,
                End = cf.End,
                Flag = cf.Flag,
            };
        }
    }
}
