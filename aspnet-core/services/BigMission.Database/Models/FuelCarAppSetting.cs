using System;
using System.Collections.Generic;

namespace BigMission.Database.Models
{
    public partial class FuelCarAppSetting
    {
        public int Id { get; set; }
        public int DeviceAppId { get; set; }
        public string FuelDatabaseConnection { get; set; }
        public bool EnableAutoPrime { get; set; }
        public bool EnableFuelConsumption { get; set; }
        public int FuelPrimeButtonNumber { get; set; }
    }
}
