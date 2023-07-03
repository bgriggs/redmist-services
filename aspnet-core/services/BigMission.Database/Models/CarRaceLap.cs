using System;
using System.Collections.Generic;

namespace BigMission.Database.Models
{
    public partial class CarRaceLap
    {
        public int EventId { get; set; }
        public string CarNumber { get; set; }
        public DateTime Timestamp { get; set; }
        public string ClassName { get; set; }
        public int PositionInRun { get; set; }
        public int CurrentLap { get; set; }
        public double LastLapTimeSeconds { get; set; }
        public int LastPitLap { get; set; }
        public int PitStops { get; set; }
        public byte Flag { get; set; }
        public int RunId { get; set; }
    }
}
