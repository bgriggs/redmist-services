﻿using BigMission.Cache.Models;
using BigMission.Cache.Models.Flags;
using BigMission.Database;
using BigMission.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using StackExchange.Redis;
using static BigMission.RaceHeroSdk.RaceHeroClient;

namespace BigMission.RaceHeroAggregator;

class FlagStatus
{
    private ILogger Logger { get; }
    private IDateTimeHelper DateTime { get; }

    private readonly IConnectionMultiplexer cacheMuxer;
    private readonly IDbContextFactory<RedMist> dbFactory;
    private readonly List<EventFlag> eventFlags = new();
    private readonly int eventId;

    public FlagStatus(int eventId, ILoggerFactory loggerFactory, IConnectionMultiplexer cacheMuxer, IDateTimeHelper dateTime, IDbContextFactory<RedMist> dbFactory)
    {
        this.eventId = eventId;
        Logger = loggerFactory.CreateLogger(GetType().Name);
        this.cacheMuxer = cacheMuxer;
        DateTime = dateTime;
        this.dbFactory = dbFactory;
    }

    public async Task ProcessFlagStatus(Flag flag, int runId)
    {
        using var db = await dbFactory.CreateDbContextAsync();
        if (eventFlags.Count == 0)
        {
            var ef = AddNewFlag(flag, runId);
            var flagId = await SaveFlagChange(db, null, ef);
            ef.Id = flagId;
            await UpdateCache();                
            Logger.LogTrace($"Saved event flag update {flag}");
        }
        else
        {
            var currentFlag = eventFlags.Last();
            if (currentFlag.Flag != flag.ToString())
            {
                Logger.LogTrace($"Processing flag change from {currentFlag.Flag} to {flag}");
                currentFlag.End = DateTime.UtcNow;
                var ef = AddNewFlag(flag, runId);

                // Save changes
                var flagId = await SaveFlagChange(db, currentFlag, ef);
                ef.Id = flagId;

                await UpdateCache();
                Logger.LogTrace($"Saved flag change from {currentFlag.Flag} to {flag}");
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
            await UpdateCache();
            using var db = await dbFactory.CreateDbContextAsync();
            db.Update(ConvertFlag(currentFlag));
            await db.SaveChangesAsync();
            Logger.LogTrace($"Saved event {eventId} end");
        }
    }

    private static async Task<int> SaveFlagChange(RedMist db, EventFlag? currentFlag, EventFlag nextFlag)
    {
        if (currentFlag != null)
        {
            db.Update(ConvertFlag(currentFlag));
        }
        var dbFlag = ConvertFlag(nextFlag);
        db.EventFlags.Add(dbFlag);
        await db.SaveChangesAsync();
        return dbFlag.Id;
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
