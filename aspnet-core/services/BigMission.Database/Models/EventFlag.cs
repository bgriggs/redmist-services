using System;
using System.Collections.Generic;

namespace BigMission.Database.Models
{
    public partial class EventFlag
    {
        public int Id { get; set; }
        public int EventId { get; set; }
        public string Flag { get; set; }
        public DateTime Start { get; set; }
        public DateTime? End { get; set; }
        public int RunId { get; set; }
    }
}
