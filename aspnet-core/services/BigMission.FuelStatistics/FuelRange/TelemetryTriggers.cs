using BigMission.Database.Models;
using BigMission.DeviceApp.Shared;
using BigMission.TestHelpers;
using System;
using System.Collections.Generic;
using System.Linq;

namespace BigMission.FuelStatistics.FuelRange
{
    /// <summary>
    /// Tracks speed and fuel data to determine when 
    /// a car stops and starts a new stint.
    /// </summary>
    public class TelemetryTriggers
    {
        private readonly IDateTimeHelper dateTimeHelper;
        private readonly TimeSpan telemetryTimeoutDuration = TimeSpan.FromSeconds(30);
        private DateTime lastTelemetryUpdate;
        public ChannelMapping SpeedChannel { get; set; }
        public ChannelMapping FuelLevelChannel { get; set; }

        /// <summary>
        /// Determines if there is sufficient telemetry available to be usable.
        /// </summary>
        public bool IsTelemetryAvailable
        {
            get
            {
                var diff = dateTimeHelper.UtcNow - lastTelemetryUpdate;
                return diff < telemetryTimeoutDuration;
            }
        }

        private readonly List<Tuple<float, DateTime>> speedWindow = new();
        private readonly TimeSpan speedTimeThreshold = TimeSpan.FromSeconds(6);
        private readonly int speedTriggerMinSamples = 3;
        private readonly float speedTriggerThreshold = 35f;
        private readonly RefuelCheck refuelCheck;


        public TelemetryTriggers(IDateTimeHelper dateTimeHelper)
        {
            this.dateTimeHelper = dateTimeHelper;
            refuelCheck = new RefuelCheck(dateTimeHelper);
        }


        /// <summary>
        /// Uses car's data to determine if a stint is to start or stop.
        /// This requires speed and fuel channels.
        /// </summary>
        /// <param name="telem">latest updates from the car</param>
        /// <param name="currentStint">used to determine whether to check for stint stopping or starting</param>
        /// <returns>Start or End updates with notes, or null if not available</returns>
        public Cache.Models.FuelRange.Stint ProcessCarTelemetry(ChannelDataSetDto telem, Cache.Models.FuelRange.Stint currentStint)
        {
            // If we don't have speed and fuel channels defined, we can't use telemetry
            if (SpeedChannel == null || FuelLevelChannel == null)
            {
                return null;
            }

            var speedCh = telem.Data?.FirstOrDefault(d => d.ChannelId == SpeedChannel.Id);
            var flCh = telem.Data?.FirstOrDefault(d => d.ChannelId == FuelLevelChannel.Id);

            var stintUpdates = new Cache.Models.FuelRange.Stint();

            if (speedCh == null || flCh == null)
            {
                return stintUpdates;
            }

            // Check for start of a new stint
            if (currentStint == null || currentStint.End.HasValue)
            {
                var startTriggered = CheckForSpeedStartTrigger(speedCh.Value);
                if (startTriggered)
                {
                    stintUpdates.Start = dateTimeHelper.UtcNow;
                    stintUpdates.Note += "Start triggered from telem. ";
                }
            }
            else // Check for stint end
            {
                // Check for end of stint based on a refuel point
                if (speedCh != null && flCh != null)
                {
                    var isRefueling = refuelCheck.IsRefuling((int)speedCh.Value, flCh.Value);
                    if (isRefueling)
                    {
                        stintUpdates.End = dateTimeHelper.UtcNow;
                        stintUpdates.Note += "End triggered by telem refueling. ";
                    }
                }
            }

            lastTelemetryUpdate = dateTimeHelper.UtcNow;
            return stintUpdates;
        }

        private bool CheckForSpeedStartTrigger(float speed)
        {
            // Remove sample outside of the specified window of time
            var now = dateTimeHelper.UtcNow;
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
    }
}
