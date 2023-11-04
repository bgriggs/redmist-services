using BigMission.Database.Models;
using BigMission.DeviceApp.Shared;
using BigMission.TestHelpers;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using static BigMission.RaceHeroSdk.RaceHeroClient;

namespace BigMission.FuelStatistics.FuelConsumption;

/// <summary>
/// Tracks the fuel level changes across laps and calculates range.
/// </summary>
public class CarConsumption
{
    public float FuelLevel { get; set; }
    private float lastLapFuelLevel;
    private readonly List<(double lapSecs, double cons)> lapConsHistory = new();
    private const int MAX_CONS_HISTORY = 3;

    public const string SVR_RANGE_LAPS = "SrvRangeLaps";
    public const string SVR_RANGE_TIME = "SrvRangeTime";
    public const string SVR_FL_RANGE_LAPS = "SrvFlRangeLaps";
    public const string SVR_FL_RANGE_TIME = "SrvFlRangeTime";

    public static readonly string[] ConsumptionChannelNames = new[]
    {
        SVR_RANGE_LAPS,
        SVR_RANGE_TIME,
        SVR_FL_RANGE_LAPS,
        SVR_FL_RANGE_TIME,
    };

    private readonly int carId;
    private readonly IDataContext dataContext;
    private readonly IDateTimeHelper dateTime;
    private List<ChannelMapping> channelMappings;
    private ILogger Logger { get; set; }

    public CarConsumption(int carId, ILoggerFactory loggerFactory, IDataContext dataContext, IDateTimeHelper dateTimeHelper)
    {
        Logger = loggerFactory.CreateLogger(GetType().Name);
        this.carId = carId;
        this.dataContext = dataContext;
        dateTime = dateTimeHelper;
    }

    public async Task Process(Lap lap)
    {
        if (lastLapFuelLevel <= 0)
        {
            lastLapFuelLevel = FuelLevel;
            return;
        }

        Logger.LogDebug($"Processing lap for direct fuel consumption for car {carId}...");
        double cons = 0;
        double rangeLaps = 0;
        double rangeTime = 0;
        double rangeLapsFiltered = 0;
        double rangeTimeFiltered = 0;
        var diff = lastLapFuelLevel - FuelLevel;
        lastLapFuelLevel = FuelLevel;

        // Make sure there was a reduction in fuel before using the data
        if (diff > 0)
        {
            cons = diff;
            rangeLaps = FuelLevel / cons;
            rangeTime = lap.LastLapTimeSeconds * rangeLaps;

            if (rangeLaps < 0) rangeLaps = 0;
            if (rangeTime < 0) rangeTime = 0;

            // Determined smoothed and filtered ranges
            if ((Flag)lap.Flag == Flag.Green)
            {
                if (lapConsHistory.Count == MAX_CONS_HISTORY)
                {
                    lapConsHistory.RemoveAt(0);
                }

                lapConsHistory.Add((lap.LastLapTimeSeconds, cons));
            }
            else
            {
                Logger.LogDebug($"Fuel consumption calculation excluding non-green lap data for car {carId}");
            }
        }

        if (lapConsHistory.Any())
        {
            rangeLapsFiltered = FuelLevel / lapConsHistory.Select(c => c.cons).Average();
            rangeTimeFiltered = lapConsHistory.Select(c => c.lapSecs).Average() * rangeLapsFiltered;

            if (rangeLapsFiltered < 0) rangeLapsFiltered = 0;
            if (rangeTimeFiltered < 0) rangeTimeFiltered = 0;
        }

        await PublishChannels(rangeLaps, rangeTime, rangeLapsFiltered, rangeTimeFiltered);
    }

    private async Task PublishChannels(double rangeLaps, double rangeTimeSecs, double rangeLapsFiltered, double rangeTimeSecsFiltered)
    {
        channelMappings ??= await dataContext.GetConsumptionChannels(carId);

        if (channelMappings.Any())
        {
            var channelStatusUpdates = new List<ChannelStatusDto>();

            // Range Laps
            var rangeLapsCh = channelMappings.FirstOrDefault(c => c.ReservedName == SVR_RANGE_LAPS);
            if (rangeLapsCh != null)
            {
                var s = new ChannelStatusDto
                {
                    ChannelId = rangeLapsCh.Id,
                    Timestamp = dateTime.UtcNow,
                    DeviceAppId = rangeLapsCh.DeviceAppId,
                    Value = (float)Math.Truncate(rangeLaps)
                };
                channelStatusUpdates.Add(s);
            }

            // Range Time
            var rangeTimeCh = channelMappings.FirstOrDefault(c => c.ReservedName == SVR_RANGE_TIME);
            if (rangeTimeCh != null)
            {
                var s = new ChannelStatusDto
                {
                    ChannelId = rangeTimeCh.Id,
                    Timestamp = dateTime.UtcNow,
                    DeviceAppId = rangeTimeCh.DeviceAppId,
                    Value = (float)Math.Round(rangeTimeSecs / 60.0, 1)
                };
                channelStatusUpdates.Add(s);
            }

            // Range Smoothed/Filtered Laps
            var rangeLapsFilteredCh = channelMappings.FirstOrDefault(c => c.ReservedName == SVR_FL_RANGE_LAPS);
            if (rangeLapsFilteredCh != null)
            {
                var s = new ChannelStatusDto
                {
                    ChannelId = rangeLapsFilteredCh.Id,
                    Timestamp = dateTime.UtcNow,
                    DeviceAppId = rangeLapsFilteredCh.DeviceAppId,
                    Value = (float)Math.Truncate(rangeLapsFiltered)
                };
                channelStatusUpdates.Add(s);
            }

            // Range Smoothed/Filtered Laps
            var rangeTimeSecsFilteredCh = channelMappings.FirstOrDefault(c => c.ReservedName == SVR_FL_RANGE_TIME);
            if (rangeTimeSecsFilteredCh != null)
            {
                var s = new ChannelStatusDto
                {
                    ChannelId = rangeTimeSecsFilteredCh.Id,
                    Timestamp = dateTime.UtcNow,
                    DeviceAppId = rangeTimeSecsFilteredCh.DeviceAppId,
                    Value = (float)Math.Round(rangeTimeSecsFiltered / 60.0, 1)
                };
                channelStatusUpdates.Add(s);
            }

            var channelDs = new ChannelDataSetDto { IsVirtual = true, Timestamp = dateTime.UtcNow, Data = channelStatusUpdates.ToArray() };
            await dataContext.PublishChannelStatus(channelDs);
        }
        else
        {
            Logger.LogDebug($"No fuel consumption channels configured for car {carId}");
        }
    }
}
