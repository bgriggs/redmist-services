using System;
using System.Collections.Generic;

namespace BigMission.Database.Models
{
    public partial class SimulationSetting
    {
        public int Id { get; set; }
        public bool YellowFlags { get; set; }
        public bool PitStops { get; set; }
    }
}
