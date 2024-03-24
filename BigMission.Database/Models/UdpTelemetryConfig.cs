using System;
using System.Collections.Generic;

namespace BigMission.Database.Models
{
    public partial class UdpTelemetryConfig
    {
        public int Id { get; set; }
        public int? CarId { get; set; }
        public bool IsEnabled { get; set; }
        public string LocalNicName { get; set; }
        public int LocalPort { get; set; }
        public string DestinationIp { get; set; }
        public int DestinationPort { get; set; }
        public int SendIntervalMs { get; set; }
    }
}
