using BigMission.Cache.Models.FuelStatistics;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;

namespace BigMission.FuelStatistics
{
    public class PitStop : PitStopBase
    {
        /// <summary>
        /// Laps with pit stop flag.  This sometimes includes more than one when pit loop is before pit stall.
        /// </summary>
        [JsonIgnore] 
        public SortedDictionary<int, Lap> Laps { get; } = new SortedDictionary<int, Lap>();

        public override double EstPitStopSecs
        {
            get
            {
                if (RefLapTimeSecs > 0 && Laps.Any())
                {
                    var totalTime = Laps.Sum(l => l.Value.LastLapTimeSeconds);
                    return totalTime - RefLapTimeSecs;
                }
                return 0;
            }
        }

        public override DateTime EndPitTime
        {
            get
            {
                if (EstPitStopSecs > 0 && Laps.Any())
                {
                    var f = Laps.First().Value.Timestamp;
                    return f + TimeSpan.FromSeconds(EstPitStopSecs);
                }

                return DateTime.MinValue;
            }
        }
    }
}
