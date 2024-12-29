using BigMission.Database;
using BigMission.Database.Models;
using Microsoft.EntityFrameworkCore;

namespace BigMission.RaceHeroAggregator;

/// <summary>
/// Configuration to simulate input from race hero.
/// </summary>
internal class SimulateSettingsService : ISimulateSettingsService
{
    public SimulationSetting Settings { get { return settings.Value; } }
    private readonly Lazy<SimulationSetting> settings;

    public SimulateSettingsService(IDbContextFactory<RedMist> dbFactory)
    {
        settings = new Lazy<SimulationSetting>(() =>
        {
            using var db = dbFactory.CreateDbContext();
            return db.SimulationSettings.First();
        });
    }
}

interface ISimulateSettingsService
{
    public SimulationSetting Settings { get; }
}
