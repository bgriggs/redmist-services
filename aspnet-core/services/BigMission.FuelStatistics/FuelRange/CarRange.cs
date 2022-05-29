using BigMission.Cache.Models.FuelRange;
using BigMission.Database.Models;
using BigMission.DeviceApp.Shared;
using BigMission.TestHelpers;
using NLog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
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
        private readonly FuelRangeSetting settings;

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

        public ILogger Logger { get; }

        private List<Cache.Models.FuelRange.Stint> stints = new();
        private readonly HashSet<Lap> laps = new();
        private readonly TelemetryTriggers telemetryTriggers;
        private readonly FlagDurationUtils flagUtils;
        private List<Cache.Models.Flags.EventFlag> currentFlags;
        private readonly IFuelRangeContext fuelRangeContext;


        public CarRange(FuelRangeSetting settings, IDateTimeHelper dateTimeHelper, IFuelRangeContext fuelRangeContext, ILogger logger)
        {
            this.settings = settings;
            CarId = settings.CarId;
            telemetryTriggers = new TelemetryTriggers(dateTimeHelper);
            flagUtils = new FlagDurationUtils(dateTimeHelper);
            this.fuelRangeContext = fuelRangeContext;
            Logger = logger;
        }


        public Cache.Models.FuelRange.Stint[] GetStints()
        {
            return stints.ToArray();
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
            stints.Clear();
            laps.Clear();
        }

        /// <summary>
        /// Update calculated values.
        /// </summary>
        /// <returns>true if there is a value change to publish</returns>
        public bool Refresh()
        {
            bool changed = false;
            foreach (var stint in stints)
            {
                changed |= UpdateCalculatedValues(stint);
            }
            return changed;
        }

        public async Task<bool> ProcessLaps(Lap[] laps)
        {
            bool changed = false;
            foreach (var l in laps)
            {
                this.laps.Add(l);
            }

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
                    var updatedStint = LapDataTriggers.ProcessLap(lap, currentStint);
                    if (updatedStint != null)
                    {
                        // Apply changes from laps when telemetry is not in use or not available
                        if (!settings.UseTelemetry || !telemetryTriggers.IsTelemetryAvailable)
                        {
                            changed |= await ProcessUpdatedStint(updatedStint, currentStint);
                        }
                        // Check for the end of the race, which comes from RH rather than the car's telemetry
                        else if (updatedStint.End.HasValue && (Flag)lap.Flag == Flag.Finish || (Flag)lap.Flag == Flag.Stop)
                        {
                            changed |= await ProcessUpdatedStint(updatedStint, currentStint);
                        }
                    }
                }
            }

            // Update calculated values when we get more laps
            foreach (var stint in stints)
            {
                changed |= UpdateCalculatedValues(stint);
            }

            return changed;
        }

        public async Task<bool> ProcessTelemetery(ChannelDataSetDto telemData)
        {
            bool changed = false;
            if (settings.UseTelemetry)
            {
                var currentStint = stints.LastOrDefault();
                var updatedStint = telemetryTriggers.ProcessCarTelemetry(telemData, currentStint);
                if (updatedStint != null)
                {
                    changed = await ProcessUpdatedStint(updatedStint, currentStint);
                }
            }

            return changed;
        }

        private async Task<bool> ProcessUpdatedStint(Cache.Models.FuelRange.Stint updatedStint, Cache.Models.FuelRange.Stint currentStint)
        {
            bool changed = false;
            if (updatedStint.End.HasValue && currentStint != null)
            {
                currentStint.End = updatedStint.End;
                currentStint.Note = updatedStint.Note;
                changed = true;
            }
            // See if there is a new stint
            else if (updatedStint.Start != default)
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
                updatedStint.DefaultRangeMins = settings.RangeMins;
                updatedStint.DefaultPitTimeSecs = settings.PitStopSecs;
                stints.Add(updatedStint);

                var savedStint = await fuelRangeContext.SaveTeamStint(updatedStint);
                updatedStint.Id = savedStint.Id;
                changed = true;
            }
            return changed;
        }

        public bool ApplyEventFlags(List<Cache.Models.Flags.EventFlag> flags)
        {
            currentFlags = flags;
            return flagUtils.UpdateStintFlagDurations(flags, ref stints);
        }

        /// <summary>
        /// Take changes to a stint made by users and update the local stint.
        /// </summary>
        /// <param name="update"></param>
        /// <returns>true if something was udpated</returns>
        public Task<bool> OverrideStint(RangeUpdate update)
        {
            bool changed = false;
            if (update.Action == RangeUpdate.DELETE)
            {
                var count = stints.RemoveAll(st => st.Id == update.Stint.Id);
                if (count > 0)
                {
                    changed = true;
                    Logger.Debug($"Deleting user removed stint ID={update.Stint.Id}");
                }
            }

            if (update.Stint.CarId == CarId)
            {
                if (update.Action == RangeUpdate.OVERRIDE)
                {
                    var st = stints.FirstOrDefault(s => s.Id == update.Stint.Id);
                    if (st != null)
                    {
                        if (update.Stint.StartOverride != null)
                        {
                            st.StartOverride = update.Stint.StartOverride;
                        }
                        if (update.Stint.EndOverride != null)
                        {
                            st.EndOverride = update.Stint.EndOverride;
                        }
                        if (update.Stint.StartingFuelGalsOverride != null)
                        {
                            st.StartingFuelGalsOverride = update.Stint.StartingFuelGalsOverride;
                        }

                        if (update.Stint.YellowDurationOverrideMins != null)
                        {
                            st.YellowDurationOverrideMins = update.Stint.YellowDurationOverrideMins;
                        }
                        if (update.Stint.YellowBonusOverrideMins != null)
                        {
                            st.YellowBonusOverrideMins = update.Stint.YellowBonusOverrideMins;
                        }

                        if (update.Stint.RedDurationOverrideMins != null)
                        {
                            st.RedDurationOverrideMins = update.Stint.RedDurationOverrideMins;
                        }
                        if (update.Stint.RedBonusOverrideMins != null)
                        {
                            st.RedBonusOverrideMins = update.Stint.RedBonusOverrideMins;
                        }

                        st.RefuelAmountGal = update.Stint.RefuelAmountGal;
                        st.PitInMins = update.Stint.PitInMins;
                        st.Note = update.Stint.Note;

                        // Recalculate values
                        UpdateCalculatedValues(st);

                        Logger.Debug($"Updating user changes for stint ID={update.Stint.Id}");
                    }
                    changed = true;
                }
                else if (update.Action == RangeUpdate.ADD)
                {
                    stints.Add(update.Stint);
                    UpdateCalculatedValues(update.Stint);
                    changed = true;
                    Logger.Debug($"Adding user created stint ID={update.Stint.Id}");
                }
            }

            // Realign flag states since user can override the timeframe of stints
            if (changed)
            {
                flagUtils.UpdateStintFlagDurations(currentFlags, ref stints);
            }

            return Task.FromResult(changed);
        }

        /// <summary>
        /// Update range, bonuses, and average lap time.
        /// </summary>
        /// <param name="stint"></param>
        /// <returns>true if changed</returns>
        private bool UpdateCalculatedValues(Cache.Models.FuelRange.Stint stint)
        {
            var changed = false;
            var start = stint.StartOverride ?? stint.Start;
            var end = stint.EndOverride ?? stint.End;
            var startGals = stint.StartingFuelGalsOverride ?? stint.StartingFuelGals;
            var yellow = stint.YellowDurationOverrideMins ?? stint.YellowDurationMins;
            var yellowBonus = stint.YellowBonusOverrideMins ?? stint.YellowBonusMins;
            var red = stint.RedDurationOverrideMins ?? stint.RedDurationMins;
            var redBonus = stint.RedBonusOverrideMins ?? stint.RedBonusMins;

            // Calc bonuses
            if (yellow > 0)
            {
                var yb = (settings.YellowFlagBonusPerc / 100.0) * yellow;
                if (stint.YellowBonusMins != yb)
                {
                    stint.YellowBonusMins = yb;
                    changed = true;
                }
            }
            if (red > 0)
            {
                var rb = red.Value;
                if (stint.RedBonusMins != rb)
                {
                    stint.RedBonusMins = rb;
                    changed = true;
                }
            }

            // Calc fuel range
            if (end.HasValue && stint.RefuelAmountGal.HasValue)
            {
                var duration = end.Value - start;

                // Deduct yellow and red bonus
                if (yellowBonus > 0)
                {
                    duration -= TimeSpan.FromMinutes(yellowBonus.Value);
                }
                if (redBonus > 0)
                {
                    duration -= TimeSpan.FromMinutes(redBonus.Value);
                }

                var cg = stint.RefuelAmountGal.Value / duration.TotalMinutes;
                if (stint.ConsumptionGalMins != cg)
                {
                    stint.ConsumptionGalMins = cg;
                    changed = true;
                }

                if (startGals > 0)
                {
                    var cr = startGals.Value / stint.ConsumptionGalMins;
                    if (stint.CalculatedRange != cr)
                    {
                        stint.CalculatedRange = cr;
                        changed = true;
                    }
                }
                else
                {
                    stint.CalculatedRange = null;
                }
            }

            // Averge lap
            var oalt = stint.AverageLapTime;
            if (end.HasValue)
            {
                var stintLaps = laps.Where(l => l.Timestamp >= start && l.Timestamp < end);
                if (stintLaps.Any())
                {
                    stint.AverageLapTime = stintLaps.Average(l => l.LastLapTimeSeconds);
                }
                else
                {
                    stint.AverageLapTime = null;
                }
            }
            else if (start != default)
            {
                var stintLaps = laps.Where(l => l.Timestamp >= start);
                if (stintLaps.Any())
                {
                    stint.AverageLapTime = stintLaps.Average(l => l.LastLapTimeSeconds);
                }
                else
                {
                    stint.AverageLapTime = null;
                }
            }

            // Update current stint max range from previous stints.
            // Calculated range will change when user updates fuel used on past stints.
            foreach (var ps in stints)
            {
                if (ps.CalculatedRange.HasValue && ps != stint)
                {
                    if (ps.CalculatedRange.Value > (stint.MaxRangeMins ?? 0))
                    {
                        stint.MaxRangeMins = ps.CalculatedRange.Value;
                        changed = true;
                    }
                }
            }

            if (oalt != stint.AverageLapTime)
            {
                changed = true;
            }

            return changed;
        }
    }
}
