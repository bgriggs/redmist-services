using BigMission.Cache.Models.FuelRange;
using Microsoft.Extensions.Hosting;
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
        private readonly IFuelRangeContext dataContext;
        private readonly IEnumerable<IStintOverrideConsumer> overrideConsumers;


        public StintOverrideService(IFuelRangeContext dataContext, IEnumerable<IStintOverrideConsumer> overrideConsumers)
        {
            this.dataContext = dataContext;
            this.overrideConsumers = overrideConsumers;
        }


        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
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
