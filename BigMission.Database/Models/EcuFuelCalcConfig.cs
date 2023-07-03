using System;
using System.Collections.Generic;

namespace BigMission.Database.Models
{
    public partial class EcuFuelCalcConfig
    {
        public int Id { get; set; }
        public int? CarId { get; set; }
        public bool IsEnabled { get; set; }
        public string ConsumptionMode { get; set; }
        public bool FilterNonGreenConsumption { get; set; }
        public string OutputVolumeUnits { get; set; }
        public double FuelCapacityGals { get; set; }
        public int ConsumptionRollingSamples { get; set; }
    }
}
