using System;
using System.Collections.Generic;

namespace BigMission.Database.Models
{
    public partial class AlarmTrigger
    {
        public int Id { get; set; }
        public int Order { get; set; }
        public string TriggerType { get; set; }
        public string Color { get; set; }
        public string Severity { get; set; }
        public string Message { get; set; }
        public int ChannelId { get; set; }
        public string ChannelValue { get; set; }
        public int? CarAlarmsId { get; set; }

        public virtual CarAlarm CarAlarms { get; set; }
    }
}
