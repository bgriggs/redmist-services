using BigMission.Database;
using BigMission.Database.Models;
using Microsoft.Extensions.Configuration;
using System;
using System.Linq;

namespace BigMission.RaceHeroAggregator
{
    /// <summary>
    /// Configuration to simulate input from race hero.
    /// </summary>
    internal class SimulateSettingsService : ISimulateSettingsService
    {
        public SimulationSetting Settings { get { return settings.Value; } }
        private readonly Lazy<SimulationSetting> settings;

        public SimulateSettingsService(IConfiguration config)
        {
            settings = new Lazy<SimulationSetting>(() =>
            {
                using var db = new RedMist(config["ConnectionString"]);
                return db.SimulationSettings.First();
            });
        }
    }

    interface ISimulateSettingsService
    {
        public SimulationSetting Settings { get; }
    }
}
