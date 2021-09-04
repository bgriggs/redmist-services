using BigMission.DeviceApp.Shared;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace BigMission.FuelStatistics.FuelRange
{
    public interface ICarTelemetryConsumer
    {
        Task UpdateTelemetry(ChannelDataSetDto telem);
    }
}
