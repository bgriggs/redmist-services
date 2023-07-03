using System;
using System.Collections.Generic;

namespace BigMission.Database.Models
{
    public partial class CarAlarmLog
    {
        public int AlarmId { get; set; }
        public DateTime Timestamp { get; set; }
        public bool IsActive { get; set; }
    }
}
