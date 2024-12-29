using BigMission.Cache.Models.FuelStatistics;
using MathNet.Numerics.Statistics;
using System.Text.Json.Serialization;

namespace BigMission.FuelStatistics;

public class Stint : StintBase
{
    /// <summary>
    /// Laps in the stint not including pit laps.
    /// </summary>
    [JsonIgnore]
    public SortedDictionary<int, Lap> Laps { get; } = [];

    public override DateTime Start
    {
        get
        {
            if (Laps.Any())
            {
                var fl = Laps.First().Value;
                return fl.Timestamp.AddSeconds(-fl.LastLapTimeSeconds);
            }
            return default;
        }
    }
    public override DateTime End
    {
        get
        {
            if (Laps.Any())
            {
                var fl = Laps.Last().Value;
                return fl.Timestamp;
            }
            return default;
        }
    }

    public override double StintDurationSecs
    {
        get
        {
            if (Laps.Count != 0)
            {
                var first = Laps.First();
                var last = Laps.Last();

                return (last.Value.Timestamp - first.Value.Timestamp).TotalSeconds + first.Value.LastLapTimeSeconds;
            }

            return 0;
        }
    }

    public override int TotalLaps
    {
        get
        {
            if (Laps.Count != 0)
            {
                var first = Laps.First();
                var last = Laps.Last();

                return (last.Value.CurrentLap - first.Value.CurrentLap) + 1;
            }

            return 0;
        }
    }

    public override double MeanLapTimeSecs
    {
        get
        {
            if (Laps.Count != 0)
            {
                return Statistics.Mean(Laps.Select(l => l.Value.LastLapTimeSeconds));
            }
            return -0;
        }
    }

    public override double MedianLapTimeSecs
    {
        get
        {
            if (Laps.Count != 0)
            {
                return Statistics.Median(Laps.Select(l => l.Value.LastLapTimeSeconds));
            }
            return -0;
        }
    }

    public override double BestLapTimeSecs
    {
        get
        {
            if (Laps.Count != 0)
            {
                return Statistics.Minimum(Laps.Select(l => l.Value.LastLapTimeSeconds));
            }
            return -0;
        }
    }

    public override double LapTimeStdDev
    {
        get
        {
            if (Laps.Count != 0)
            {
                return Statistics.StandardDeviation(Laps.Select(l => l.Value.LastLapTimeSeconds));
            }
            return -0;
        }
    }

}
