using BigMission.Cache;
using BigMission.DeviceApp.Shared;
using BigMission.RaceManagement;
using System;
using System.Collections.Generic;
using System.Linq;
using static BigMission.RaceHeroSdk.RaceHeroClient;

namespace BigMission.FuelStatistics.FuelRange
{
    /// <summary>
    /// Tracks range and fuel for a car.
    /// </summary>
    class CarRange
    {
        public int CarId { get; private set; }
        private readonly FuelRangeSettings settings;
        private readonly RefuelCheck refuelCheck;
        public FuelRangeStint CurrentStint { get; private set; }
        public FuelRangeStint StintDataToSave
        {
            get
            {
                lock (stintDataToSaveLock)
                {
                    return stintDataToSave;
                }
            }
            set
            {
                lock (stintDataToSaveLock)
                {
                    stintDataToSave = value;
                }
            }
        }
        private FuelRangeStint stintDataToSave;
        private readonly object stintDataToSaveLock = new object();
        private readonly TimeSpan telemetryTimeoutDuration = TimeSpan.FromSeconds(30);
        private DateTime lastTelemetryUpdate;
        public ChannelMapping SpeedChannel { get; set; }
        public ChannelMapping FuelLevelChannel { get; set; }
        private readonly FuelRangeContext fuelRangeContext;
        private readonly List<Tuple<float, DateTime>> speedWindow = new List<Tuple<float, DateTime>>();
        private readonly TimeSpan speedTimeThreshold = TimeSpan.FromSeconds(6);
        private readonly int speedTriggerMinSamples = 3;
        private readonly float speedTriggerThreshold = 35f;
        private List<EventFlag> lastEventFlags = new List<EventFlag>();


        public CarRange(FuelRangeSettings settings, FuelRangeContext fuelRangeContext)
        {
            this.settings = settings;
            this.fuelRangeContext = fuelRangeContext;
            CarId = settings.CarId;
            refuelCheck = new RefuelCheck();
        }


        public void UpdateWithRaceHero(Lap[] laps)
        {
            // Update stint range based on pit stops from race hero.  This get overriden when we have car telemetry
            // that allows for determining when the car was refuled and when the car starts moving at speed again.
            if (settings.UseRaceHeroTrigger && (!settings.UseTelemetry || !IsTelemAvailable()))
            {
                foreach (var lap in laps)
                {
                    // Check for start of stint
                    if (CurrentStint == null)
                    {
                        // Look for lap after pit or in the case of a start, it should be lap 1
                        if (lap.CurrentLap > lap.LastPitLap)
                        {
                            CreateNewStint();
                        }
                    }

                    // Check for end of stint
                    if (lap.CurrentLap == lap.LastPitLap)
                    {
                        CurrentStint.End = DateTime.UtcNow;

                        StintDataToSave = CurrentStint;
                        CurrentStint = null;
                    }
                }
            }

            // Update the duration when new laps push the stint time forward
            UpdateFlagState(lastEventFlags);
        }

        public void UpdateWithTelemetry(ChannelDataSetDto telem)
        {
            if (!settings.UseTelemetry || SpeedChannel == null || FuelLevelChannel == null)
            {
                return;
            }

            var speedCh = telem.Data.FirstOrDefault(d => d.ChannelId == SpeedChannel.Id);
            var flCh = telem.Data.FirstOrDefault(d => d.ChannelId == FuelLevelChannel.Id);

            // Check for start of stint
            if (CurrentStint == null)
            {
                var startTriggered = CheckForSpeedTrigger(speedCh.Value);
                if (startTriggered)
                {
                    CreateNewStint();
                }
            }

            // Check for end of stint based on a refuel point
            if (speedCh != null && flCh != null)
            {
                var isRefuling = refuelCheck.IsRefuling((int)speedCh.Value, flCh.Value);
                if (isRefuling)
                {
                    CurrentStint.End = DateTime.UtcNow;

                    StintDataToSave = CurrentStint;
                    CurrentStint = null;
                }
            }

            lastTelemetryUpdate = DateTime.Now;
        }

        private bool IsTelemAvailable()
        {
            var diff = DateTime.Now - lastTelemetryUpdate;
            return diff > telemetryTimeoutDuration;
        }

        private void CreateNewStint()
        {
            CurrentStint = fuelRangeContext.CreateNewStint(settings.TenantId, CarId, DateTime.UtcNow);
        }

        private bool CheckForSpeedTrigger(float speed)
        {
            // Remove sample outside of the specified window of time
            var now = DateTime.Now;
            speedWindow.RemoveAll(t => now - t.Item2 > speedTimeThreshold);

            speedWindow.Add(Tuple.Create(speed, now));

            // If there are enough samples, determine if the speed exceeds the start threshold
            if (speedWindow.Count >= speedTriggerMinSamples)
            {
                var average = speedWindow.Average(t => t.Item1);
                if (average >= speedTriggerThreshold)
                {
                    return true;
                }
            }

            return false;
        }

        public void UpdateFlagState(List<EventFlag> eventFlags)
        {
            var stint = CurrentStint;
            if (stint != null)
            {
                double yellowDuration = 0;
                double redDuration = 0;
                foreach (var flag in eventFlags)
                {
                    var f = ParseFlag(flag.Flag);
                    if (f == Flag.Yellow)
                    {
                        yellowDuration += CalcFlagDuration(flag, stint);
                    }
                    else if (f == Flag.Red)
                    {
                        redDuration += CalcFlagDuration(flag, stint);
                    }
                }

                stint.YellowDurationMins = yellowDuration;
                stint.RedDurationMins = redDuration;
            }

            lastEventFlags = eventFlags;
        }

        private static double CalcFlagDuration(EventFlag flag, FuelRangeStint stint)
        {
            double duration = 0;

            // Flag starts during current stint
            if (flag.Start >= stint.Start)
            {
                if (flag.End.HasValue)
                {
                    duration += (flag.End.Value - flag.Start).TotalMinutes;
                }
                else // Flag still active
                {
                    duration += (DateTime.UtcNow - flag.Start).TotalMinutes;
                }
            }
            // Flag started before the stint and is still active
            else if (flag.Start < stint.Start && flag.End == null)
            {
                duration += (DateTime.UtcNow - stint.Start).TotalMinutes;
            }
            // Flag started before the stint and ended during the stint
            else if (flag.Start < stint.Start && flag.End.HasValue && flag.End.Value > flag.Start)
            {
                duration += (flag.End.Value - stint.Start).TotalMinutes;
            }

            return duration;
        }
    }
}
