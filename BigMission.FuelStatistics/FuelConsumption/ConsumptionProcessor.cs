﻿using BigMission.Database.Models;
using BigMission.DeviceApp.Shared;
using BigMission.TestHelpers;

namespace BigMission.FuelStatistics.FuelConsumption;

/// <summary>
/// Manages directly measured fuel consumption for the connected cars in an event instance.
/// </summary>
public class ConsumptionProcessor
{
    private readonly Dictionary<int, CarConsumption> cars = new();
    private readonly ILoggerFactory loggerFactory;
    private readonly IDataContext dataContext;
    private readonly IDateTimeHelper dateTimeHelper;

    public ConsumptionProcessor(ILoggerFactory loggerFactory, IDataContext dataContext, IDateTimeHelper dateTimeHelper) 
    {
        this.loggerFactory = loggerFactory;
        this.dataContext = dataContext;
        this.dateTimeHelper = dateTimeHelper;
    }

    public async Task UpdateLaps(List<Lap> laps, int carId)
    {
        if (!cars.TryGetValue(carId, out CarConsumption? car))
        {
            car = new CarConsumption(carId, loggerFactory, dataContext, dateTimeHelper);
            cars[carId] = car;
        }

        // Process last lap. Do not use stale laps if status stalls out for whatever reason.
        await car.Process(laps.Last());
    }

    /// <summary>
    /// Capture fuel level changes from car's telemetry.
    /// </summary>
    public void UpdateTelemetry(ChannelDataSetDto telem, int carId, ChannelMapping fuelMapping)
    {
        var fuelCh = telem.Data.First(c => c.ChannelId == fuelMapping.Id);
        if (fuelMapping == null) { return; }

        if (!cars.TryGetValue(carId, out CarConsumption? car))
        {
            car = new CarConsumption(carId, loggerFactory, dataContext, dateTimeHelper);
            cars[carId] = car;
        }
        car.FuelLevel = fuelCh.Value;
    }

    /// <summary>
    /// Update consumption and range timestamps for the event's cars.
    /// </summary>
    public async Task PublishConsumptionRanges()
    {
        var pubTasks = cars.Values.Select(async (e) =>
        {
            await e.PublishLastChannels();
        });

        await Task.WhenAll(pubTasks);
    }
}
