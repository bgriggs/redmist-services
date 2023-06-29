using BigMission.Database;
using BigMission.Database.Helpers;
using BigMission.ServiceStatusTools;
using BigMission.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using System.Diagnostics;

namespace BigMission.RaceControlLog.Configuration
{
    internal class ConfigurationService : BackgroundService
    {
        private readonly IDbContextFactory<RedMist> dbFactory;
        private readonly StartupHealthCheck startup;

        private IConfiguration Config { get; }
        private ILogger Logger { get; }
        private ConfigurationContext ConfigurationContext { get; }
        private IDateTimeHelper DateTime { get; }

        public ConfigurationService(IConfiguration config, ILoggerFactory loggerFactory, ConfigurationContext configurationContext, IDateTimeHelper dateTimeHelper, 
            IDbContextFactory<RedMist> dbFactory, StartupHealthCheck startup)
        {
            Config = config;
            Logger = loggerFactory.CreateLogger(GetType().Name);
            ConfigurationContext = configurationContext;
            DateTime = dateTimeHelper;
            this.dbFactory = dbFactory;
            this.startup = startup;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            Logger.LogInformation("Waiting for dependencies...");
            while (!stoppingToken.IsCancellationRequested)
            {
                if (await startup.CheckDependencies())
                    break;
                await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
            }

            while (!stoppingToken.IsCancellationRequested)
            {
                var sw = Stopwatch.StartNew();
                try
                {
                    Logger.LogDebug("Checking service configuration...");

                    var config = new ConfigurationEventData();
                    config.Events = await RaceEventSettings.LoadCurrentEventSettings(dbFactory, DateTime.UtcNow, stoppingToken);
                    Logger.LogInformation($"Loaded {config.Events.Length} event subscriptions.");

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

                    using var db = await dbFactory.CreateDbContextAsync(stoppingToken);

                    // Load cars
                    config.Cars = await db.Cars.Where(c => !c.IsDeleted && carIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, stoppingToken);

                    // Load users
                    config.Users = await db.AbpUsers.Where(u => !u.IsDeleted && u.IsPhoneNumberConfirmed && userIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, stoppingToken);

                    await ConfigurationContext.UpdateConfiguration(config, stoppingToken);
                }
                catch (Exception ex)
                {
                    Logger.LogError(ex, "Error loading configuration settings");
                }
                Logger.LogDebug($"Configuration check in {sw.ElapsedMilliseconds}ms");
                await Task.Delay(TimeSpan.FromSeconds(double.Parse(Config["CONFIGURATIONCHECKRATESECS"])), stoppingToken);
            }
        }
    }
}
