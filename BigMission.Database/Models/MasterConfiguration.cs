namespace BigMission.Database.Models
{
#pragma warning disable CS8632 // The annotation for nullable reference types should only be used in code within a '#nullable' annotations context.
    public class MasterConfiguration
    {
        public Guid DeviceAppKey { get; set; }
        public CanAppConfig? BaseConfig { get; set; }
        public ChannelMapping[]? ChannelMappings { get; set; }
        public FuelCarAppSetting? FuelSettings { get; set; }
        public KeypadCarAppConfig? KeypadSettings { get; set; }
        public TpmsConfig? TpmsSettings { get; set; }
        public Car? Car { get; set; } = new();
        public RaceHeroSetting? RaceHeroSetting { get; set; } = new();
        public RaceEventSetting? RaceEventSetting { get; set; } = new();
        public EcuFuelCalcConfig? EcuFuelCalcConfig { get; set; } = new();
        public UdpTelemetryConfig? UdpTelemetryConfig { get; set; }
    }
}
