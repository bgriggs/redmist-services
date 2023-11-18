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

    public const string SVR_CONS_GAL_LAP = "SrvConsGalLap";
    public const string SVR_RANGE_LAPS = "SrvRangeLaps";
    public const string SVR_RANGE_TIME = "SrvRangeTime";
    public const string SVR_FL_CONS_GAL_LAP = "SrvFlConsGalLap";
    public const string SVR_FL_RANGE_LAPS = "SrvFlRangeLaps";
    public const string SVR_FL_RANGE_TIME = "SrvFlRangeTime";

    public static readonly string[] ConsumptionChannelNames = new[]
    {
        SVR_CONS_GAL_LAP,
        SVR_RANGE_LAPS,
        SVR_RANGE_TIME,
        SVR_FL_CONS_GAL_LAP,
        SVR_FL_RANGE_LAPS,
        SVR_FL_RANGE_TIME,
    };

    private readonly int carId;
    private readonly IDataContext dataContext;
    private readonly IDateTimeHelper dateTime;
    private List<ChannelMapping> channelMappings;
    private ILogger Logger { get; set; }
    private record ConsRange(double ConsGalLap, double RangeLaps, double RangeTimeSecs, double ConsFiltered, double RangeLapsFiltered, double RangeTimeSecsFiltered);

    private ConsRange lastConsRange;


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
        double consFiltered = 0;
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
            consFiltered = lapConsHistory.Select(c => c.cons).Average();
            rangeLapsFiltered = FuelLevel / consFiltered;
            rangeTimeFiltered = lapConsHistory.Select(c => c.lapSecs).Average() * rangeLapsFiltered;

            if (rangeLapsFiltered < 0) rangeLapsFiltered = 0;
            if (rangeTimeFiltered < 0) rangeTimeFiltered = 0;
        }

        lastConsRange = new ConsRange(cons, rangeLaps, rangeTime, consFiltered, rangeLapsFiltered, rangeTimeFiltered);
        await PublishChannels(lastConsRange);
    }
    

    private async Task PublishChannels(ConsRange cr)
    {
        channelMappings ??= await dataContext.GetConsumptionChannels(carId);

        if (channelMappings.Any())
        {
            var channelStatusUpdates = new List<ChannelStatusDto>();

            // Consumption Gal/Lap
            var consCh = channelMappings.FirstOrDefault(c => c.ReservedName == SVR_CONS_GAL_LAP);
            if (consCh != null)
            {
                var s = new ChannelStatusDto
                {
                    ChannelId = consCh.Id,
                    Timestamp = dateTime.UtcNow,
                    DeviceAppId = consCh.DeviceAppId,
                    Value = (float)cr.ConsGalLap
                };
                channelStatusUpdates.Add(s);
            }

            // Range Laps
            var rangeLapsCh = channelMappings.FirstOrDefault(c => c.ReservedName == SVR_RANGE_LAPS);
            if (rangeLapsCh != null)
            {
                var s = new ChannelStatusDto
                {
                    ChannelId = rangeLapsCh.Id,
                    Timestamp = dateTime.UtcNow,
                    DeviceAppId = rangeLapsCh.DeviceAppId,
                    Value = (float)Math.Truncate(cr.RangeLaps)
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
                    Value = (float)Math.Round(cr.RangeTimeSecs / 60.0, 1)
                };
                channelStatusUpdates.Add(s);
            }

            // Consumption Smoothed/Filtered Laps
            var consFilteredCh = channelMappings.FirstOrDefault(c => c.ReservedName == SVR_FL_CONS_GAL_LAP);
            if (consFilteredCh != null)
            {
                var s = new ChannelStatusDto
                {
                    ChannelId = consFilteredCh.Id,
                    Timestamp = dateTime.UtcNow,
                    DeviceAppId = consFilteredCh.DeviceAppId,
                    Value = (float)cr.ConsFiltered
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
                    Value = (float)Math.Truncate(cr.RangeLapsFiltered)
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
                    Value = (float)Math.Round(cr.RangeTimeSecsFiltered / 60.0, 1)
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

    /// <summary>
    /// Update timestamp on consumption and range channels so they do not expire.
    /// </summary>
    public async Task PublishLastChannels()
    {
        await PublishChannels(lastConsRange);
    }
}
