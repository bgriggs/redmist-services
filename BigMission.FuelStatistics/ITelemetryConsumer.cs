using BigMission.DeviceApp.Shared;
using System;

namespace BigMission.FuelStatistics
{
    public interface ITelemetryConsumer
    {
        Action<ChannelDataSetDto> ReceiveData { get; set; }
        void Connect();
    }
}
