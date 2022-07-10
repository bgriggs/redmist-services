using BigMission.Database;
using BigMission.Database.Helpers;
using BigMission.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using NLog;
using System.Diagnostics;

namespace BigMission.RaceControlLog.Configuration
{
    internal class ConfigurationService : BackgroundService
    {
        private IConfiguration Config { get; }
        private ILogger Logger { get; }
        private ConfigurationContext ConfigurationContext { get; }
        private IDateTimeHelper DateTime { get; }

        public ConfigurationService(IConfiguration config, ILogger logger, ConfigurationContext configurationContext, IDateTimeHelper dateTimeHelper)
        {
            Config = config;
            Logger = logger;
            ConfigurationContext = configurationContext;
            DateTime = dateTimeHelper;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                var sw = Stopwatch.StartNew();
                try
                {
                    Logger.Debug("Checking service configuration...");
                    var connStr = Config["ConnectionString"];

                    var config = new ConfigurationEventData();
                    config.Events = await RaceEventSettings.LoadCurrentEventSettings(connStr, DateTime.UtcNow);
                    Logger.Info($"Loaded {config.Events.Length} event subscriptions.");

                    var carIds = new HashSet<long>();
                    var userIds = new HashSet<long>();
                    foreach (var evt in config.Events)
                    {
                        foreach (var cid in ConfigurationEventData.GetIds(evt.CarIds ?? string.Empty))
                        {
                            carIds.Add(cid);
                        }
                        foreach (var uid in ConfigurationEventData.GetIds(evt.ControlLogSmsUserSubscriptions ?? string.Empty))
                        {
                            userIds.Add(uid);
                        }
                    }

                    using var db = new RedMist(connStr);

                    // Load cars
                    config.Cars = await db.Cars.Where(c => !c.IsDeleted && carIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, stoppingToken);

                    // Load users
                    config.Users = await db.AbpUsers.Where(u => !u.IsDeleted && u.IsPhoneNumberConfirmed && userIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, stoppingToken);

                    await ConfigurationContext.UpdateConfiguration(config, stoppingToken);
                }
                catch (Exception ex)
                {
                    Logger.Error(ex, "Error loading configuration settings");
                }
                Logger.Debug($"Configuration check in {sw.ElapsedMilliseconds}ms");
                await Task.Delay(TimeSpan.FromSeconds(double.Parse(Config["ConfigurationCheckRateSecs"])), stoppingToken);
            }
        }
    }
}
