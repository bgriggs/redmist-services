using static BigMission.RaceHeroSdk.RaceHeroClient;

namespace BigMission.FuelStatistics.FuelRange;

/// <summary>
/// Tracks race hero lap data to determine when 
/// a car stops and starts a new stint.
/// </summary>
public class LapDataTriggers
{
    public static Cache.Models.FuelRange.Stint ProcessLap(Lap lap, Cache.Models.FuelRange.Stint? currentStint)
    {
        var stintUpdates = new Cache.Models.FuelRange.Stint();

        // Check for start of race
        if (currentStint == null)
        {
            if (lap.CurrentLap > 0)
            {
                stintUpdates.Start = lap.Timestamp - TimeSpan.FromSeconds(lap.LastLapTimeSeconds);
                stintUpdates.Note += "Start triggered from lap data. ";
            }
        }
        else if (currentStint.End.HasValue)
        {
            // Look for lap after pit
            if (lap.CurrentLap > lap.LastPitLap)
            {
                stintUpdates.Start = lap.Timestamp - TimeSpan.FromSeconds(lap.LastLapTimeSeconds);
                stintUpdates.Note += "Start triggered from lap data. ";
            }
        }

        // Check for end of stint
        if (lap.CurrentLap > 0 && (lap.CurrentLap == lap.LastPitLap || (Flag)lap.Flag == Flag.Finish || (Flag)lap.Flag == Flag.Stop))
        {
            stintUpdates.End = lap.Timestamp;
            stintUpdates.Note += "End triggered by lap pit data. ";
        }

        return stintUpdates;
    }
}
