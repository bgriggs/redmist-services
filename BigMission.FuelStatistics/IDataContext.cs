﻿using BigMission.Database.Models;
using BigMission.DeviceApp.Shared;

namespace BigMission.FuelStatistics;

/// <summary>
/// Data access for events.
/// </summary>
public interface IDataContext
{
    /// <summary>
    /// Maintains connection across calls.
    /// </summary>
    Task StartBatch();

    /// <summary>
    /// Closes connection.
    /// </summary>
    Task EndBatch();

    Task<List<Lap>> GetSavedLaps(int eventId);
    Task<List<FuelRangeSetting>> GetFuelRangeSettings(int[] carIds);
    Task<List<DeviceAppConfig>> GetDeviceAppConfig(int[] carIds);
    Task<List<BigMission.Database.Models.Car>> GetCars(int[] carIds);
    Task<List<ChannelMapping>> GetChannelMappings(int[] deviceAppIds, string[] channelNames);
    Task<List<FuelRangeStint>> GetTeamStints(int teamId, int eventId);
    Task<List<RaceEventSetting>> GetEventSettings();
    Task<List<ChannelMapping>> GetConsumptionChannels(int carId);

    Task UpdateCarStatus(Car car, int eventId);
    Task<DateTime?> CheckReload(int teamId);
    Task<List<Lap>> PopEventLaps(int eventId);
    Task ClearCachedEvent(int rhEventId);
    Task PublishChannelStatus(ChannelDataSetDto channelDataSetDto);
}
