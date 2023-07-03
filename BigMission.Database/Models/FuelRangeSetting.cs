using System;
using System.Collections.Generic;

namespace BigMission.Database.Models
{
    public partial class FuelRangeSetting
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
        public int CarId { get; set; }
        public bool UseRaceHeroTrigger { get; set; }
        public bool UseTelemetry { get; set; }
        public double CapacityGals { get; set; }
        public double RangeMins { get; set; }
        public double YellowFlagBonusPerc { get; set; }
        public double PitStopSecs { get; set; }
    }
}
