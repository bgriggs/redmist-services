namespace BigMission.CarTelemetryProcessor
{
    internal interface ITelemetryConsumer
    {
        Task ProcessTelemetryMessage(DeviceApp.Shared.ChannelDataSetDto receivedTelem);
    }
}
