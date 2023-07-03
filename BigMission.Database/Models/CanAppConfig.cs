using System;
using System.Collections.Generic;

namespace BigMission.Database.Models
{
    public partial class CanAppConfig
    {
        public int Id { get; set; }
        public int DeviceAppId { get; set; }
        public Guid ConfigurationId { get; set; }
        public string CanCmd { get; set; }
        public string CanArg { get; set; }
        public string CanBitrate { get; set; }
        public int HeartbeatFrequencyMs { get; set; }
        public int StandardUpdateFrequencyMs { get; set; }
        public int FullUpdateFrequencyMs { get; set; }
        public string ApiUrl { get; set; }
        public bool? Can1Enable { get; set; }
        public string Can2Arg { get; set; }
        public string Can2Bitrate { get; set; }
        public string Can2Cmd { get; set; }
        public bool? Can2Enable { get; set; }
        public bool? EnableLocalRaceHeroStatus { get; set; }
        public bool? EnableModemResetWatchdog { get; set; }
        public int ModemResetButton { get; set; }
        public bool? EnableRebootOnDisconnect { get; set; }
        public int RebootOnDisconnectTimeoutMins { get; set; }
        public int CanDecoderVersion { get; set; }
        public bool? SilentOnCanBus { get; set; }
    }
}
