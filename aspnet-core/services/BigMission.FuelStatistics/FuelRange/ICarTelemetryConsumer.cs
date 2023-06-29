using BigMission.DeviceApp.Shared;
using System.Threading.Tasks;

namespace BigMission.FuelStatistics.FuelRange
{
    public interface ICarTelemetryConsumer
    {
        Task UpdateTelemetry(ChannelDataSetDto telem);
    }
}
