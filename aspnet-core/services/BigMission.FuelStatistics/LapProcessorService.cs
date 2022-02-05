using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using NLog;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace BigMission.FuelStatistics
{
    /// <summary>
    /// Receive car laps that have come in from Race Hero and propigate them to consumers.
    /// </summary>
    public class LapProcessorService : BackgroundService
    {
        private ILogger Logger { get; set; }
        private readonly TimeSpan lapCheckInterval;
        private readonly IDataContext dataContext;
        private readonly IEnumerable<ILapConsumer> lapConsumers;


        public LapProcessorService(IConfiguration configuration, ILogger logger, IDataContext dataContext, IEnumerable<ILapConsumer> lapConsumers)
        {
            Logger = logger;
            this.dataContext = dataContext;
            this.lapConsumers = lapConsumers;
            lapCheckInterval = TimeSpan.FromMilliseconds(int.Parse(configuration["LapCheckMs"]));
        }


        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var sw = Stopwatch.StartNew();
                    foreach (var consumer in lapConsumers)
                    {
                        var eventTasks = consumer.EventIds.Select(async (evt) =>
                        {
                            try
                            {
                                var laps = await dataContext.PopEventLaps(evt);
                                if (laps.Any())
                                {
                                    Logger.Debug($"Loaded {laps.Count} laps for event {evt}");
                                    await consumer.UpdateLaps(evt, laps);
                                }
                            }
                            catch (Exception ex)
                            {
                                Logger.Error(ex, $"Error processing event laps for event={evt}");
                            }
                        });

                        await Task.WhenAll(eventTasks);
                    }

                    Logger.Trace($"Processed lap updates in {sw.ElapsedMilliseconds}ms");
                }
                catch (Exception ex)
                {
                    Logger.Error(ex, "Error checking for laps to process");
                }

                await Task.Delay(lapCheckInterval, stoppingToken);
            }
        }


    }
}
