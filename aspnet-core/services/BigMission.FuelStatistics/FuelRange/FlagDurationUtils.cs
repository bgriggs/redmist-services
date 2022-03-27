using BigMission.Cache.Models.Flags;
using BigMission.TestHelpers;
using System.Collections.Generic;
using static BigMission.RaceHeroSdk.RaceHeroClient;

namespace BigMission.FuelStatistics.FuelRange
{
    public class FlagDurationUtils
    {
        private readonly IDateTimeHelper dateTimeHelper;

        public FlagDurationUtils(IDateTimeHelper dateTimeHelper)
        {
            this.dateTimeHelper = dateTimeHelper;
        }

        /// <summary>
        /// Determines time spent in yellow and red for an event and applies that time to a car's stints.
        /// </summary>
        /// <param name="eventFlags">List of flags for an event</param>
        /// <param name="carStints">A car's current stints for an event</param>
        /// <returns>true if there was a time that changed, otherwise false</returns>
        public bool UpdateStintFlagDurations(List<EventFlag> eventFlags, ref List<Cache.Models.FuelRange.Stint> carStints)
        {
            bool changed = false;
            foreach (var stint in carStints)
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

                if (stint.YellowDurationMins != yellowDuration)
                {
                    stint.YellowDurationMins = yellowDuration;
                    changed = true;
                }
                if (stint.RedDurationMins != redDuration)
                {
                    stint.RedDurationMins = redDuration;
                    changed = true;
                }
            }

            return changed;
        }

        private double CalcFlagDuration(EventFlag flag, Cache.Models.FuelRange.Stint stint)
        {
            double duration = 0;

            // Set end times to simplify logic
            var stintEnd = stint.End ?? dateTimeHelper.UtcNow;
            if (stint.EndOverride.HasValue)
            {
                stintEnd = stint.EndOverride.Value;
            }
            var flagEnd = flag.End ?? dateTimeHelper.UtcNow;

            var stintStart = stint.Start;
            if (stint.StartOverride.HasValue)
            {
                stintStart = stint.StartOverride.Value;
            }

            // Flag starts during the stint
            if (flag.Start >= stintStart && flag.Start < stintEnd)
            {
                // STINT |--------------------------------|
                // FLAG          |------------|
                if (flagEnd <= stintEnd)
                {
                    duration += (flagEnd - flag.Start).TotalMinutes;
                }
                // STINT |-------------------------|
                // FLAG                 |------------|
                else
                {
                    duration += (stintEnd - flag.Start).TotalMinutes;
                }
            }
            // Flag started before the stint
            else if (flag.Start < stintStart && flagEnd > stintStart)
            {
                // STINT    |--------------|
                // FLAG  |-------------|
                if (flagEnd < stintEnd)
                {
                    duration += (flagEnd - stintStart).TotalMinutes;
                }
                // STINT    |--------------|
                // FLAG  |--------------------|
                else
                {
                    duration += (stintEnd - stintStart).TotalMinutes;
                }
            }

            return duration;
        }
    }
}
