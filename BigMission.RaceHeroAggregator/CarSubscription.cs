﻿using BigMission.Cache.Models;
using BigMission.Database;
using BigMission.Database.Models;
using BigMission.DeviceApp.Shared;
using BigMission.RaceHeroSdk.Models;
using BigMission.RaceHeroSdk.Status;
using BigMission.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using StackExchange.Redis;

namespace BigMission.RaceHeroAggregator;

class CarSubscription
{
    public const string LAST_LAP = "LastLap";
    public const string BEST_LAP = "BestLap";
    public const string POS_OVERALL = "PositionOverall";
    public const string POS_IN_CLASS = "PositionInClass";
    public const string GAP_FRONT = "GapFront";
    public const string GAP_BEHIND = "GapBehind";

    public static readonly string[] RaceHeroChannelNames =
    [
        POS_OVERALL,
        POS_IN_CLASS,
        GAP_FRONT,
        GAP_BEHIND,
        LAST_LAP,
        BEST_LAP
    ];

    private IDateTimeHelper DateTime { get; }
    public int CarId { get; set; }
    public string? CarNumber { get; set; }
    public ChannelMapping[]? VirtualChannels { get; private set; }
    private readonly IConnectionMultiplexer cacheMuxer;
    private readonly IDbContextFactory<RedMist> dbFactory;

    public CarSubscription(IConnectionMultiplexer cacheMuxer, IDateTimeHelper dateTime, IDbContextFactory<RedMist> dbFactory)
    {
        this.cacheMuxer = cacheMuxer;
        DateTime = dateTime;
        this.dbFactory = dbFactory;
    }


    /// <summary>
    /// Loads virtual channels which include race hero channels.
    /// </summary>
    public async Task InitializeChannels()
    {
        using var db = await dbFactory.CreateDbContextAsync();

        // Check for race hero processing in the car.  Skip it here if it is enabled.
        var canAppConfig = await (from d in db.DeviceAppConfigs
                                  join c in db.CanAppConfigs on d.Id equals c.DeviceAppId
                                  where d.CarId == CarId && !d.IsDeleted && c.EnableLocalRaceHeroStatus == true
                                  select c).ToArrayAsync();
        if (!canAppConfig.Any())
        {
            VirtualChannels = await (from d in db.DeviceAppConfigs
                                     join m in db.ChannelMappings on d.Id equals m.DeviceAppId
                                     where d.CarId == CarId && m.IsVirtual && !d.IsDeleted && RaceHeroChannelNames.Contains(m.ReservedName)
                                     select m).ToArrayAsync();
        }
    }

    public async Task ProcessUpdate(Racer[] racers)
    {
        var lap = racers.FirstOrDefault(r => r.RacerNumber.ToLower() == CarNumber?.ToLower());
        if (lap != null && VirtualChannels != null && VirtualChannels.Length > 0)
        {
            var channelStatusUpdates = new List<ChannelStatusDto>();
            var posOverallCh = VirtualChannels.FirstOrDefault(c => c.ReservedName == POS_OVERALL);
            if (posOverallCh != null)
            {
                var s = new ChannelStatusDto
                {
                    ChannelId = posOverallCh.Id,
                    Timestamp = DateTime.UtcNow,
                    DeviceAppId = posOverallCh.DeviceAppId,
                    Value = lap.PositionInRun
                };
                channelStatusUpdates.Add(s);
            }

            var posInClassCh = VirtualChannels.FirstOrDefault(c => c.ReservedName == POS_IN_CLASS);
            if (posInClassCh != null)
            {
                var s = new ChannelStatusDto
                {
                    ChannelId = posInClassCh.Id,
                    Timestamp = DateTime.UtcNow,
                    DeviceAppId = posInClassCh.DeviceAppId,
                    Value = lap.PositionInClass
                };
                channelStatusUpdates.Add(s);
            }

            var gapFrontCh = VirtualChannels.FirstOrDefault(c => c.ReservedName == GAP_FRONT);
            if (gapFrontCh != null)
            {
                _ = float.TryParse(lap.GapToAheadInRun, out float aheadTime);
                var s = new ChannelStatusDto
                {
                    ChannelId = gapFrontCh.Id,
                    Timestamp = DateTime.UtcNow,
                    DeviceAppId = gapFrontCh.DeviceAppId,
                    Value = aheadTime
                };
                channelStatusUpdates.Add(s);
            }

            var gapBehindCh = VirtualChannels.FirstOrDefault(c => c.ReservedName == GAP_BEHIND);
            if (gapBehindCh != null)
            {
                var behind = PositionHelper.GetCarBehindOverall(lap, racers);
                if (behind != null)
                {
                    _ = float.TryParse(behind.GapToAheadInRun, out float aheadTime);
                    var s = new ChannelStatusDto
                    {
                        ChannelId = gapBehindCh.Id,
                        Timestamp = DateTime.UtcNow,
                        DeviceAppId = gapBehindCh.DeviceAppId,
                        Value = aheadTime
                    };
                    channelStatusUpdates.Add(s);
                }
            }

            var lastTimeCh = VirtualChannels.FirstOrDefault(c => c.ReservedName == LAST_LAP);
            if (lastTimeCh != null)
            {
                var s = new ChannelStatusDto
                {
                    ChannelId = lastTimeCh.Id,
                    Timestamp = DateTime.UtcNow,
                    DeviceAppId = lastTimeCh.DeviceAppId,
                    Value = (float)lap.LastLapTimeSeconds
                };
                channelStatusUpdates.Add(s);
            }

            var bestTimeCh = VirtualChannels.FirstOrDefault(c => c.ReservedName == BEST_LAP);
            if (bestTimeCh != null)
            {
                var s = new ChannelStatusDto
                {
                    ChannelId = bestTimeCh.Id,
                    Timestamp = DateTime.UtcNow,
                    DeviceAppId = bestTimeCh.DeviceAppId,
                    Value = (float)lap.BestLapTimeSeconds
                };
                channelStatusUpdates.Add(s);
            }

            var channelDs = new ChannelDataSetDto { IsVirtual = true, Timestamp = DateTime.UtcNow, Data = [.. channelStatusUpdates] };
            var json = JsonConvert.SerializeObject(channelDs);
            var pub = cacheMuxer.GetSubscriber();
            await pub.PublishAsync(RedisChannel.Literal(Consts.CAR_TELEM_SUB), json);
        }

        //lastLap = lap;
    }
}
