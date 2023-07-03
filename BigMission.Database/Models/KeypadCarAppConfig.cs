using System;
using System.Collections.Generic;

namespace BigMission.Database.Models
{
    public partial class KeypadCarAppConfig
    {
        public int Id { get; set; }
        public int DeviceAppId { get; set; }
        public int ButtonCount { get; set; }
        public int FullCloudUpdateIntervalMs { get; set; }
        public int SendKeyCommandCount { get; set; }
        public int SendKeyCommandIntervalMs { get; set; }
    }
}
