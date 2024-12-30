using BigMission.Database.Models;
using Microsoft.EntityFrameworkCore;

namespace BigMission.Database.Helpers;

public class RaceEventSettings
{
    public static async Task<RaceEventSetting[]> LoadCurrentEventSettings(IDbContextFactory<RedMist> dbFactory, DateTime utcNow, CancellationToken stoppingToken)
    {
        try
        {
            using var db = await dbFactory.CreateDbContextAsync(stoppingToken);

            var events = await db.RaceEventSettings
                .Where(s => !s.IsDeleted && s.IsEnabled)
                .ToArrayAsync(cancellationToken: stoppingToken);

            // Filter by subscription time
            var activeSubscriptions = new List<RaceEventSetting>();

            foreach (var evt in events)
            {
                // Get the local time zone info if available
                TimeZoneInfo? tz = null;
                if (!string.IsNullOrWhiteSpace(evt.EventTimeZoneId))
                {
                    try
                    {
                        tz = TimeZoneInfo.FindSystemTimeZoneById(evt.EventTimeZoneId);
                    }
                    catch { }
                }

                // Attempt to use local time, otherwise use UTC
                var now = utcNow;
                if (tz != null)
                {
                    now = TimeZoneInfo.ConvertTimeFromUtc(now, tz);
                }

                // The end date should go to the end of the day the that the user specified
                var end = new DateTime(evt.EventEnd.Year, evt.EventEnd.Month, evt.EventEnd.Day, 23, 59, 59);
                if (evt.EventStart <= now && end >= now)
                {
                    activeSubscriptions.Add(evt);
                }
            }

            return [.. activeSubscriptions];
        }
        catch { }
        return [];
    }
}
