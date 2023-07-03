using BigMission.ServiceStatusTools;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace BigMission.FuelStatistics
{
    /// <summary>
    /// Receive car laps that have come in from Race Hero and propagate them to consumers.
    /// </summary>
    public class LapProcessorService : BackgroundService
    {
        private ILogger Logger { get; set; }
        private readonly TimeSpan lapCheckInterval;
        private readonly IDataContext dataContext;
        private readonly IEnumerable<ILapConsumer> lapConsumers;
        private readonly StartupHealthCheck startup;

        public LapProcessorService(IConfiguration configuration, ILoggerFactory loggerFactory, IDataContext dataContext, IEnumerable<ILapConsumer> lapConsumers, StartupHealthCheck startup)
        {
            Logger = loggerFactory.CreateLogger(GetType().Name);
            this.dataContext = dataContext;
            this.lapConsumers = lapConsumers;
            this.startup = startup;
            lapCheckInterval = TimeSpan.FromMilliseconds(int.Parse(configuration["LAPCHECKMS"]));
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
                try
                {
                    var sw = Stopwatch.StartNew();
                    foreach (var consumer in lapConsumers)
                    {
                        var eventTasks = consumer.EventIds.Select(async (evt) =>
                        {
                            try
                            {
                                // This is a bug if there is ever more than one consumer looking for laps for the same event.. Convert to Data Flow?
                                var laps = await dataContext.PopEventLaps(evt);
                                if (laps.Any())
                                {
                                    Logger.LogDebug($"Loaded {laps.Count} laps for event {evt}");
                                    await consumer.UpdateLaps(evt, laps);
                                }
                            }
                            catch (Exception ex)
                            {
                                Logger.LogError(ex, $"Error processing event laps for event={evt}");
                            }
                        });

                        await Task.WhenAll(eventTasks);
                    }

                    Logger.LogTrace($"Processed lap updates in {sw.ElapsedMilliseconds}ms");
                }
                catch (Exception ex)
                {
                    Logger.LogError(ex, "Error checking for laps to process");
                }

                await Task.Delay(lapCheckInterval, stoppingToken);
            }
        }


    }
}
