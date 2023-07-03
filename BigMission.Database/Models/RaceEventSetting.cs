using System;
using System.Collections.Generic;

namespace BigMission.Database.Models
{
    public partial class RaceEventSetting
    {
        public int Id { get; set; }
        public DateTime CreationTime { get; set; }
        public long? CreatorUserId { get; set; }
        public DateTime? LastModificationTime { get; set; }
        public long? LastModifierUserId { get; set; }
        public bool IsDeleted { get; set; }
        public long? DeleterUserId { get; set; }
        public DateTime? DeletionTime { get; set; }
        public int TenantId { get; set; }
        public bool IsEnabled { get; set; }
        public DateTime EventStart { get; set; }
        public DateTime EventEnd { get; set; }
        public string RaceHeroEventId { get; set; }
        public bool EventEnded { get; set; }
        public string CarIds { get; set; }
        public string EventTimeZoneId { get; set; }
        public string ControlLogParameter { get; set; }
        public string ControlLogSmsUserSubscriptions { get; set; }
        public string ControlLogType { get; set; }
        public bool? EnableControlLog { get; set; }
    }
}
