﻿using System;
using System.Collections.Generic;

namespace BigMission.Database.Models
{
    public partial class TeamRetentionPolicy
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
        public int ArchiveDays { get; set; }
        public int PurgeDays { get; set; }
    }
}
