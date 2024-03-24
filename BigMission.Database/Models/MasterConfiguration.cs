namespace BigMission.Database.Models
{
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
