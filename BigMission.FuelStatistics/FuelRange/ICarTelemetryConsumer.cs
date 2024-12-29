using BigMission.DeviceApp.Shared;

namespace BigMission.FuelStatistics.FuelRange;

public interface ICarTelemetryConsumer
{
    Task UpdateTelemetry(ChannelDataSetDto telem);
}
