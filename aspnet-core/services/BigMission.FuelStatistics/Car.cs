﻿using BigMission.Cache.Models.FuelStatistics;
using MathNet.Numerics.Statistics;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;

namespace BigMission.FuelStatistics
{
    public class Car : CarBase
    {
        [JsonIgnore]
        public SortedDictionary<int, Lap> Laps { get; } = new SortedDictionary<int, Lap>();

        public Car(string number, string className)
        {
            if (string.IsNullOrWhiteSpace(number)) { throw new ArgumentException("number"); }
            Number = number;
            if (string.IsNullOrWhiteSpace(className)) { throw new ArgumentException("className"); }
            ClassName = className;
        }
        
        private void ClearMetrics()
        {
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

            // Update stats
            Stints.Clear();
            Pits.Clear();

            var currentStint = new Stint();
            Stints.Add(currentStint);

            foreach (var lap in Laps)
            {
                // Look for pit stops
                bool includeLapInStint = true;
                if (lap.Value.CurrentLap == lap.Value.LastPitLap)
                {
                    includeLapInStint = false;

                    var ps = new PitStop();
                    Pits.Add(ps);
                    ps.Laps[lap.Key] = lap.Value;
                    ps.Comments = "Using flagged pit lap as pit stop lap. ";
                    // Create a new stint on pit lap
                    if (currentStint.Laps.Any())
                    {
                        currentStint = new Stint();
                        Stints.Add(currentStint);
                    }
                }

                // When the lap after the flagged pit stop time is greater than last lap pit stops, assume timing loop is before pit stall
                // such that this lap contains the pit stop
                if ((lap.Value.LastPitLap + 1) == lap.Value.CurrentLap && Laps.ContainsKey(lap.Value.LastPitLap))
                {
                    var lastPitLapTime = Laps[lap.Value.LastPitLap].LastLapTimeSeconds;
                    if (lap.Value.LastLapTimeSeconds > lastPitLapTime)
                    {
                        var lps = (PitStop)Pits.Last();
                        // Todo: Consider transering misclassified pit laps to the previous stint before clearing
                        lps.Laps.Clear();
                        lps.Laps[lap.Key] = lap.Value;
                        lps.Comments = $"Timing loop is before pit stall, including next lap as pit stop. ";
                        includeLapInStint = false;
                    }
                }

                // Use the new drivers out lap in the pit time calculation.
                if (Pits.Any())
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

            if (Pits.Any() && Stints.Any())
            {
                var lps =  (PitStop)Pits.Last();
                LastStop = lps.EndPitTime;
                LastPitLap = lps.Laps.Last().Key;

                var estStopTimes = Pits.Select(p => p.EstPitStopSecs);
                MeanPitStopSecs = Statistics.Mean(estStopTimes);
                MedianPitStopSecs = Statistics.Median(estStopTimes);
                BestPitStopSecs = Statistics.Minimum(estStopTimes);


                MeanRangeSecs = Statistics.Mean(Stints.Select(s => s.StintDurationSecs));
                MaxRangeSecs = Statistics.Maximum(Stints.Select(s => s.StintDurationSecs));

                MeanRangeLaps = Statistics.Mean(Stints.Select(s => (double)s.TotalLaps));
                MaxRangeLaps = Statistics.Maximum(Stints.Select(s => (double)s.TotalLaps));
                MedianRangeLaps = Statistics.Median(Stints.Select(s => (double)s.TotalLaps));


                NextPitTime = LastStop + TimeSpan.FromSeconds(MaxRangeSecs ?? 0);
                MinutesRemaining = (NextPitTime.Value - Laps.Last().Value.Timestamp).TotalMinutes;
                LapsRemaining = MinutesRemaining / (Stints.Last().MedianLapTimeSecs / 60);
            }
        }
    }
}