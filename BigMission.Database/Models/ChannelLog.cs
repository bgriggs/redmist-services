using System;
using System.Collections.Generic;

namespace BigMission.Database.Models
{
    public partial class ChannelLog
    {
        public DateTime Timestamp { get; set; }
        public int DeviceAppId { get; set; }
        public int ChannelId { get; set; }
        public float Value { get; set; }
    }
}
