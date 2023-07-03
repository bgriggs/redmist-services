using System;
using System.Collections.Generic;

namespace BigMission.Database.Models
{
    public partial class TpmsConfig
    {
        public int Id { get; set; }
        public int? CarId { get; set; }
        public int Lfsensor { get; set; }
        public int Rfsensor { get; set; }
        public int Lrsensor { get; set; }
        public int Rrsensor { get; set; }
        public bool BroadcastOnCan { get; set; }
        public bool ConvertToUsunits { get; set; }
    }
}
