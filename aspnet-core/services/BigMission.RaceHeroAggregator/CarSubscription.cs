using BigMission.CommandTools;
using BigMission.DeviceApp.Shared;
using BigMission.EntityFrameworkCore;
using BigMission.RaceHeroSdk.Models;
using BigMission.RaceManagement;
using Microsoft.EntityFrameworkCore.Internal;
using Microsoft.Extensions.Configuration;
using NLog;
using System;
using System.Collections.Generic;
using System.Linq;

namespace BigMission.RaceHeroAggregator
{
    class CarSubscription
    {
        public static readonly string[] RaceHeroChannelNames = new[]
        {
            ReservedChannel.POS_OVERALL,
            ReservedChannel.POS_IN_CLASS,
            ReservedChannel.GAP_FRONT,
            ReservedChannel.GAP_BEHIND,
            ReservedChannel.LAST_LAP,
            ReservedChannel.BEST_LAP
        };

        private ILogger Logger { get; }
        private IConfiguration Config { get; }
        public int CarId { get; set; }
        public string CarNumber { get; set; }
        public ChannelMapping[] VirtualChannels { get; private set; }
        private readonly ChannelData channelData;


        public CarSubscription(ILogger logger, IConfiguration config, ChannelData channelData)
        {
            Logger = logger;
            Config = config;
            this.channelData = channelData;
        }


        /// <summary>
        /// Loads virtual channels which include race hero channels.
        /// </summary>
        public void InitializeChannels()
        {
            var cf = new BigMissionDbContextFactory();
            using var db = cf.CreateDbContext(new[] { Config["ConnectionString"] });
            var channels = (from d in db.DeviceAppConfig
                            join m in db.ChannelMappings on d.Id equals m.DeviceAppId
                            where d.CarId == CarId && m.IsVirtual && !d.IsDeleted && RaceHeroChannelNames.Contains(m.ReservedName)
                            select m).ToArray();

            VirtualChannels = channels;
        }

        public void ProcessUpdate(Racer[] racers)
        {
            var lap = racers.FirstOrDefault(r => r.RacerNumber.ToLower() == CarNumber.ToLower());
            if (lap != null && VirtualChannels != null && VirtualChannels.Length > 0)
            {
                var channelStatusUpdates = new List<ChannelStatusDto>();
                var posOverallCh = VirtualChannels.FirstOrDefault(c => c.ReservedName == ReservedChannel.POS_OVERALL);
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

                var posInClassCh = VirtualChannels.FirstOrDefault(c => c.ReservedName == ReservedChannel.POS_IN_CLASS);
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

                var gapFrontCh = VirtualChannels.FirstOrDefault(c => c.ReservedName == ReservedChannel.GAP_FRONT);
                if (gapFrontCh != null)
                {
                    float.TryParse(lap.GapToAheadInRun, out float aheadTime);
                    var s = new ChannelStatusDto
                    {
                        ChannelId = gapFrontCh.Id,
                        Timestamp = DateTime.UtcNow,
                        DeviceAppId = gapFrontCh.DeviceAppId,
                        Value = aheadTime
                    };
                    channelStatusUpdates.Add(s);
                }

                var gapBehindCh = VirtualChannels.FirstOrDefault(c => c.ReservedName == ReservedChannel.GAP_BEHIND);
                if (gapBehindCh != null)
                {
                    var behind = GetCarBehindOverall(lap, racers);
                    if (behind != null)
                    {
                        float.TryParse(behind.GapToAheadInRun, out float aheadTime);
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

                var lastTimeCh = VirtualChannels.FirstOrDefault(c => c.ReservedName == ReservedChannel.LAST_LAP);
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

                var bestTimeCh = VirtualChannels.FirstOrDefault(c => c.ReservedName == ReservedChannel.BEST_LAP);
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

                var channelDs = new ChannelDataSetDto { IsVirtual = true, Timestamp = DateTime.UtcNow, Data = channelStatusUpdates.ToArray() };
                channelData.SendData(channelDs).Wait();
            }

            //lastLap = lap;
        }

        private Racer GetCarBehindOverall(Racer car, Racer[] field)
        {
            var orderedField = field.OrderBy(r => r.PositionInRun).ToArray();
            var index = Array.IndexOf(orderedField, car);

            if ((index + 1) < orderedField.Length)
            {
                return orderedField[index + 1];
            }
            return null;
        }
    }
}
