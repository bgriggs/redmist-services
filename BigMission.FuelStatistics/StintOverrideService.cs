using BigMission.Cache.Models.FuelRange;
using BigMission.ServiceStatusTools;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace BigMission.FuelStatistics
{
    /// <summary>
    /// This service subscribes to updates to know when a user overrides a stint's values.
    /// </summary>
    public class StintOverrideService : BackgroundService
    {
        private ILogger Logger { get; set; }
        private readonly IFuelRangeContext dataContext;
        private readonly IEnumerable<IStintOverrideConsumer> overrideConsumers;
        private readonly StartupHealthCheck startup;

        public StintOverrideService(ILoggerFactory loggerFactory, IFuelRangeContext dataContext, IEnumerable<IStintOverrideConsumer> overrideConsumers, StartupHealthCheck startup)
        {
            this.dataContext = dataContext;
            this.overrideConsumers = overrideConsumers;
            this.startup = startup;
            Logger = loggerFactory.CreateLogger(GetType().Name);
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

            await dataContext.SubscribeToFuelStintOverrides(ProcessStintOverride);
        }

        private async Task ProcessStintOverride(RangeUpdate stint)
        {
            var overrideTasks = overrideConsumers.Select(async (oc) => {
                await oc.ProcessStintOverride(stint);
            });

            await Task.WhenAll(overrideTasks);
        }
    }
}
