using System;
using System.Collections.Generic;

namespace BigMission.Database.Models
{
    public partial class ArchivePurgeSetting
    {
        public int Id { get; set; }
        public DateTime CreationTime { get; set; }
        public long? CreatorUserId { get; set; }
        public DateTime? LastModificationTime { get; set; }
        public long? LastModifierUserId { get; set; }
        public bool IsDeleted { get; set; }
        public long? DeleterUserId { get; set; }
        public DateTime? DeletionTime { get; set; }
        public DateTime RunStart { get; set; }
        public DateTime RunEnd { get; set; }
        public bool RunAuditMaintenance { get; set; }
        public bool RunChannelMaintenance { get; set; }
    }
}
