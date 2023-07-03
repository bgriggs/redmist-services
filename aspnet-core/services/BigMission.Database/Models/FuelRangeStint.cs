using System;
using System.Collections.Generic;

namespace BigMission.Database.Models
{
    public partial class FuelRangeStint
    {
        public int Id { get; set; }
        public int TenantId { get; set; }
        public int CarId { get; set; }
        public DateTime Start { get; set; }
        public DateTime? End { get; set; }
        public DateTime? StartOverride { get; set; }
        public DateTime? EndOverride { get; set; }
        public double StartingFuelGals { get; set; }
        public double? StartingFuelGalsOverride { get; set; }
        public double? YellowDurationMins { get; set; }
        public double? YellowDurationOverrideMins { get; set; }
        public double? YellowBonusMins { get; set; }
        public double? YellowBonusOverrideMins { get; set; }
        public double? RedDurationMins { get; set; }
        public double? RedDurationOverrideMins { get; set; }
        public double? RedBonusMins { get; set; }
        public double? RedBonusOverrideMins { get; set; }
        public double? AverageLapTime { get; set; }
        public double? RefuelAmountGal { get; set; }
        public double? ConsumptionGalMins { get; set; }
        public double? CalculatedRange { get; set; }
        public string Note { get; set; }
        public int EventId { get; set; }
        public int RunId { get; set; }
        public double? DefaultRangeMins { get; set; }
        public double? MaxRangeMins { get; set; }
        public double? PitInMins { get; set; }
        public double? DefaultPitTimeSecs { get; set; }
    }
}
