using System;
using System.Collections.Generic;

namespace BigMission.Database.Models
{
    public partial class ApiKey
    {
        public int Id { get; set; }
        public Guid ServiceId { get; set; }
        public string Key { get; set; }
        public bool IsPrimary { get; set; }
        public DateTime Expires { get; set; }
    }
}
