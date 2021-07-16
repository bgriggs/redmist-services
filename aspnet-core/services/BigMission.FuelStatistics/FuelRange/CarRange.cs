using BigMission.DeviceApp.Shared;
using BigMission.RaceManagement;
using BigMission.TestHelpers;
using System.Collections.Generic;
using System.Linq;
using static BigMission.RaceHeroSdk.RaceHeroClient;

namespace BigMission.FuelStatistics.FuelRange
{
    /// <summary>
    /// Tracks range and fuel for a car.
    /// </summary>
    public class CarRange
    {
        public int CarId { get; private set; }
        private int EventId { get; set; }
        private int RunId { get; set; }
        private readonly FuelRangeSettings settings;

        public ChannelMapping SpeedChannel 
        {
            get { return telemetryTriggers.SpeedChannel; }
            set { telemetryTriggers.SpeedChannel = value; }
        }
        public ChannelMapping FuelLevelChannel 
        {
            get { return telemetryTriggers.FuelLevelChannel; }
            set { telemetryTriggers.FuelLevelChannel = value; }
        }
        private List<FuelRangeStint> stints = new List<FuelRangeStint>();
        private readonly LapDataTriggers lapDataTriggers;
        private readonly TelemetryTriggers telemetryTriggers;
        private readonly FlagDurationUtils flagUtils;


        public CarRange(FuelRangeSettings settings, IDateTimeHelper dateTimeHelper)
        {
            this.settings = settings;
            CarId = settings.CarId;
            lapDataTriggers = new LapDataTriggers();
            telemetryTriggers = new TelemetryTriggers(dateTimeHelper);
            flagUtils = new FlagDurationUtils(dateTimeHelper);
        }


        public FuelRangeStint[] GetStints()
        {
            lock (stints)
            {
                return stints.ToArray();
            }
        }

        /// <summary>
        /// Clear the stints out and start over for a new race.
        /// </summary>
        /// <param name="eventId">RH event ID</param>
        /// <param name="runId">RH run ID</param>
        public void ResetForNewRace(int eventId, int runId)
        {
            EventId = eventId;
            RunId = runId;

            lock (stints)
            {
                stints.Clear();
            }
        }

        public bool ProcessLaps(Lap[] laps)
        {
            bool changed = false;

            // Update stint range based on pit stops from race hero.  This get overriden when we have car telemetry
            // that allows for determining when the car was refuled and when the car starts moving at speed again.
            if (settings.UseRaceHeroTrigger)
            {
                foreach (var lap in laps)
                {
                    if (lap.EventId != EventId || lap.RunId != RunId)
                    {
                        ResetForNewRace(lap.EventId, lap.RunId);
                    }

                    var currentStint = stints.LastOrDefault();
                    var updatedStint = lapDataTriggers.ProcessLap(lap, currentStint);
                    if (updatedStint != null)
                    {
                        // Apply changes from laps when telemetry is not in use or not available
                        if (!settings.UseTelemetry || !telemetryTriggers.IsTelemetryAvailable)
                        {
                            changed |= ProcessUpdatedStint(updatedStint, currentStint);
                        }
                        // Check for the end of the race, which comes from RH rather than the car's telemetry
                        else if (updatedStint.End.HasValue && (Flag)lap.Flag == Flag.Finish || (Flag)lap.Flag == Flag.Stop)
                        {
                            changed |= ProcessUpdatedStint(updatedStint, currentStint);
                        }
                    }
                }
            }
            return changed;
        }

        public bool ProcessTelemetery(ChannelDataSetDto telemData)
        {
            bool changed = false;
            if (settings.UseTelemetry)
            {
                var currentStint = stints.LastOrDefault();
                var updatedStint = telemetryTriggers.ProcessCarTelemetry(telemData, currentStint);
                if (updatedStint != null)
                {
                    changed = ProcessUpdatedStint(updatedStint, currentStint);
                }
            }

            return changed;
        }

        private bool ProcessUpdatedStint(FuelRangeStint updatedStint, FuelRangeStint currentStint)
        {
            bool changed = false;
            if (updatedStint.End.HasValue && currentStint != null)
            {
                // Make item update atomic
                lock (stints)
                {
                    currentStint.End = updatedStint.End;
                    currentStint.Note = updatedStint.Note;
                }
                changed = true;
            }
            // See if there is a new stint
            else if (updatedStint.Start != default)
            {
                // Make update atomic
                lock (stints)
                {
                    // Tie off old stint if it was missed
                    if (currentStint != null && !currentStint.End.HasValue)
                    {
                        currentStint.End = updatedStint.Start;
                    }
                    updatedStint.TenantId = settings.TenantId;
                    updatedStint.CarId = CarId;
                    updatedStint.EventId = EventId;
                    updatedStint.RunId = RunId;
                    updatedStint.StartingFuelGals = settings.CapacityGals;
                    stints.Add(updatedStint);
                }
                changed = true;
            }
            return changed;
        }

        public void MergeStintUserChanges(IEnumerable<FuelRangeStint> userStintChanges)
        {
            foreach(var us in userStintChanges)
            {
                if (us.CarId == CarId)
                {
                    var ss = stints.FirstOrDefault(s => s.Id == us.Id);
                    if (ss != null)
                    {
                        // Merge override values
                        ss.StartingFuelGalsOverride = us.StartingFuelGalsOverride;
                        ss.StartOverride = us.StartOverride;
                        ss.EndOverride = us.EndOverride;
                        ss.YellowDurationOverrideMins = us.YellowDurationOverrideMins;
                        ss.YellowBonusOverrideMins = us.YellowBonusOverrideMins;
                        ss.RedDurationOverrideMins = us.RedDurationOverrideMins;
                        ss.RedBonusOverrideMins = us.RedBonusOverrideMins;
                        ss.RefuelAmountGal = us.RefuelAmountGal;
                        ss.Note = us.Note;
                    }
                }
            }
        }

        public void ApplyEventFlags(List<EventFlag> flags)
        {
            flagUtils.UpdateStintFlagDurations(flags, ref stints);
        }
    }
}
