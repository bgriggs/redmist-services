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

namespace BigMission.FuelStatistics;

/// <summary>
/// Receive car laps that have come in from Race Hero and propagate them to consumers.
/// </summary>
public class LapProcessorService : BackgroundService
{
    private ILogger Logger { get; set; }
    private readonly TimeSpan lapCheckInterval;
    private readonly IDataContext dataContext;
    private readonly IEnumerable<ILapConsumer> lapConsumers;
    private readonly IStartupHealthCheck startup;

    public LapProcessorService(IConfiguration configuration, ILoggerFactory loggerFactory, IDataContext dataContext, IEnumerable<ILapConsumer> lapConsumers, IStartupHealthCheck startup)
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
                var eventIds = lapConsumers.SelectMany(e => e.EventIds).Distinct();
                var lapResults = eventIds.Select(l => dataContext.PopEventLaps(l));
                var laps = await Task.WhenAll(lapResults);
                if (laps.Any())
                {
                    var flat = laps.SelectMany(l => l);
                    var lg = flat.GroupBy(l => l.EventId).ToDictionary(g => g.Key, g => g.ToList());
                    foreach (var consumer in lapConsumers)
                    {
                        foreach (var eid in consumer.EventIds)
                        {
                            if (lg.TryGetValue(eid, out List<Lap> evtLaps))
                            {
                                try
                                {
                                    Logger.LogDebug($"Loaded {evtLaps.Count} laps for event {eid}");
                                    await consumer.UpdateLaps(eid, evtLaps);
                                }
                                catch (Exception ex)
                                {
                                    Logger.LogError(ex, $"Error processing event laps for event={eid}");
                                }
                            }
                        }
                    }
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
