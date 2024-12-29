using BigMission.Cache.Models.FuelStatistics;
using MathNet.Numerics.Statistics;
using Newtonsoft.Json;

namespace BigMission.FuelStatistics;

public class Car : CarBase
{
    [JsonIgnore]
    public SortedDictionary<int, Lap> Laps { get; } = new SortedDictionary<int, Lap>();

    public Car(string number, string className)
    {
        if (string.IsNullOrWhiteSpace(number)) { throw new ArgumentException("number"); }
        Number = number;
        ClassName = className;
    }

    private void ClearMetrics()
    {
        PositionOverall = null;
        LastStop = null;
        LastPitLap = null;
        NextPitTime = null;
        MinutesRemaining = null;
        LapsRemaining = null;
        PitUnderYellow = null;
        MaxRangeSecs = null;
        MeanRangeSecs = null;
        BestPitStopSecs = null;
        MeanPitStopSecs = null;
        MedianPitStopSecs = null;
    }

    public void Reset()
    {
        Laps.Clear();
        ClearMetrics();
    }

    public void AddLap(params Lap[] laps)
    {
        ClearMetrics();

        foreach (var lap in laps)
        {
            if (lap.LastLapTimeSeconds > 0)
            {
                Laps[lap.CurrentLap] = lap;
            }
        }

        if (Laps.Any())
        {
            PositionOverall = Laps.Last().Value.PositionInRun;
            LastLapSecs = Laps.Last().Value.LastLapTimeSeconds;
        }
        else
        {
            PositionOverall = 0;
            LastLapSecs = 0;
        }

        // Update stats
        Stints.Clear();
        Pits.Clear();

        var currentStint = new Stint { StintNumber = 1 };
        Stints.Add(currentStint);

        foreach (var lap in Laps)
        {
            // Look for pit stops
            bool includeLapInStint = true;
            if (lap.Value.CurrentLap == lap.Value.LastPitLap)
            {
                includeLapInStint = false;

                var ps = new PitStop { PitStopNumber = Pits.Count + 1 };
                Pits.Add(ps);
                ps.Laps[lap.Key] = lap.Value;
                ps.Comments = "Using flagged pit lap as pit stop lap. ";

                // Create a new stint on pit lap
                if (currentStint.Laps.Any())
                {
                    currentStint = new Stint { StintNumber = Stints.Count + 1 };
                    Stints.Add(currentStint);
                }
            }

            // When the lap after the flagged pit stop time is greater than last lap pit stops, assume timing loop is before pit stall
            // such that this lap contains the pit stop
            if ((lap.Value.LastPitLap + 1) == lap.Value.CurrentLap && Laps.ContainsKey(lap.Value.LastPitLap))
            {
                var lastPitLapTime = Laps[lap.Value.LastPitLap].LastLapTimeSeconds;
                if (lap.Value.LastLapTimeSeconds > lastPitLapTime && Pits.Any())
                {
                    var lps = (PitStop)Pits.Last();
                    // Todo: Consider transferring misclassified pit laps to the previous stint before clearing
                    lps.Laps.Clear();
                    lps.Laps[lap.Key] = lap.Value;
                    lps.Comments = $"Timing loop is before pit stall, including next lap as pit stop. ";
                    includeLapInStint = false;
                }
            }

            // Use the new drivers out lap in the pit time calculation.
            if (Pits.Count != 0)
            {
                var lps = (PitStop)Pits.Last();
                var lastPitLap = lps.Laps.Last().Value;

                // When the last lap of the last pit stop is not flagged as a pit stop, this indicates we need the time
                // of the driver that just got in the car.
                if (lap.Value.CurrentLap == lastPitLap.CurrentLap + 1)
                {
                    lps.RefLapTimeSecs = lap.Value.LastLapTimeSeconds;
                    lps.Comments += "Using new driver's first clean lap as reference. ";
                }
            }

            if (includeLapInStint)
            {
                currentStint.Laps[lap.Value.CurrentLap] = lap.Value;
            }
        }

        StintLaps = Stints.Last().TotalLaps;
        StintTimeSecs = (int)Stints.Last().StintDurationSecs;
        PitStops = Pits.Count;

        if (Pits.Count != 0 && Stints.Count != 0)
        {
            var lps = (PitStop)Pits.Last();

            if (lps.EndPitTime != default)
            {
                LastStop = lps.EndPitTime;
            }
            else
            {
                LastStop = null;
            }
            LastPitLap = lps.Laps.Last().Key;

            var estStopTimes = Pits.Select(p => p.EstPitStopSecs);
            MeanPitStopSecs = Statistics.Mean(estStopTimes.Where(st => st > 0));
            MedianPitStopSecs = Statistics.Median(estStopTimes.Where(st => st > 0));
            BestPitStopSecs = Statistics.Minimum(estStopTimes.Where(st => st > 0));

            MeanRangeSecs = Statistics.Mean(Stints.Where(st => st.StintDurationSecs > 0).Select(s => s.StintDurationSecs));
            MaxRangeSecs = Statistics.Maximum(Stints.Where(st => st.StintDurationSecs > 0).Select(s => s.StintDurationSecs));

            MeanRangeLaps = Statistics.Mean(Stints.Select(s => (double)s.TotalLaps));
            MaxRangeLaps = Statistics.Maximum(Stints.Select(s => (double)s.TotalLaps));
            MedianRangeLaps = Statistics.Median(Stints.Select(s => (double)s.TotalLaps));

            if (MaxRangeSecs.HasValue && MaxRangeSecs > 0)
            {
                if (LastStop.HasValue)
                {
                    NextPitTime = LastStop + TimeSpan.FromSeconds(MaxRangeSecs ?? 0);
                    MinutesRemaining = (NextPitTime.Value - Laps.Last().Value.Timestamp).TotalMinutes;
                }
                else
                {
                    NextPitTime = null;
                    MinutesRemaining = null;
                }

                if (MinutesRemaining > 0)
                {
                    LapsRemaining = MinutesRemaining / (Stints.Last().MedianLapTimeSecs / 60);
                }
                else
                {
                    MinutesRemaining = null;
                    LapsRemaining = null;
                }
            }
            else
            {
                NextPitTime = null;
                MinutesRemaining = null;
                LapsRemaining = null;
            }
        }
    }
}
