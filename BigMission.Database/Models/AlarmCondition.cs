using System;
using System.Collections.Generic;

namespace BigMission.Database.Models
{
    public partial class AlarmCondition
    {
        public int Id { get; set; }
        public int Order { get; set; }
        public int ChannelId { get; set; }
        public string ConditionType { get; set; }
        public string ChannelValue { get; set; }
        public string OnFor { get; set; }
        public int? CarAlarmsId { get; set; }

        public virtual CarAlarm CarAlarms { get; set; }
    }
}
