using System;
using System.Collections.Generic;

namespace BigMission.Database.Models
{
    public partial class CarAlarm
    {
        public CarAlarm()
        {
            AlarmConditions = new HashSet<AlarmCondition>();
            AlarmTriggers = new HashSet<AlarmTrigger>();
        }

        public int Id { get; set; }
        public DateTime CreationTime { get; set; }
        public long? CreatorUserId { get; set; }
        public DateTime? LastModificationTime { get; set; }
        public long? LastModifierUserId { get; set; }
        public bool IsDeleted { get; set; }
        public long? DeleterUserId { get; set; }
        public DateTime? DeletionTime { get; set; }
        public string Name { get; set; }
        public bool IsEnabled { get; set; }
        public int CarId { get; set; }
        public string ConditionOption { get; set; }
        public string AlarmGroup { get; set; }
        public int Order { get; set; }

        public virtual ICollection<AlarmCondition> AlarmConditions { get; set; }
        public virtual ICollection<AlarmTrigger> AlarmTriggers { get; set; }
    }
}
