using BigMission.RaceControlLog.Configuration;
using BigMission.RaceControlLog.LogConnections;
using BigMission.RaceControlLog.LogProcessing;
using BigMission.ServiceStatusTools;
using StackExchange.Redis;
using System.Diagnostics;

namespace BigMission.RaceControlLog;

/// <summary>
/// Polls race control log for changes.
/// </summary>
internal class LogPollService : BackgroundService
{
    private readonly StartupHealthCheck startup;

    private IConfiguration Config { get; }
    private ILogger Logger { get; }
    private ConfigurationContext ConfigurationContext { get; }
    private Dictionary<string, IControlLogConnection> LogConnections { get; }
    private IEnumerable<ILogProcessor> LogProcessors { get; }


    public LogPollService(IConfiguration config, ILoggerFactory loggerFactory, ConfigurationContext configurationContext, 
        IEnumerable<IControlLogConnection> logConnections, IEnumerable<ILogProcessor> logProcessors, StartupHealthCheck startup)
    {
        Config = config;
        Logger = loggerFactory.CreateLogger(GetType().Name);
        ConfigurationContext = configurationContext;
        LogConnections = logConnections.ToDictionary(k => k.Type);
        LogProcessors = logProcessors;
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
        await startup.Start();
        await startup.SetStarted();

        while (!stoppingToken.IsCancellationRequested)
        {
            var sw = Stopwatch.StartNew();
            try
            {
                var config = await ConfigurationContext.GetConfiguration(stoppingToken);
                var processingTasks = new List<Task>();
                // Consolidate the event to a singular control log request across all teams/tenants
                var eventTargets = config.Events.GroupBy(e => (e.ControlLogType, e.ControlLogParameter));
                foreach (var eventTarget in eventTargets)
                {
                    if (LogConnections.TryGetValue(eventTarget.Key.ControlLogType ?? string.Empty, out var logConnection))
                    {
                        // Load the latest control log
                        var controlLog = await logConnection.LoadControlLogAsync(eventTarget.Key.ControlLogParameter ?? string.Empty);
                        
                        // Process the log update on a per event basis
                        foreach (var evt in eventTarget)
                        {
                            var tasks = LogProcessors.Select(p => p.Process(evt, controlLog, config));
                            processingTasks.AddRange(tasks);
                        }
                    }
                    else
                    {
                        Logger.LogWarning($"Unsupported ControlLogType: '{eventTarget.Key.ControlLogType}'");
                    }
                }
                await Task.WhenAll(processingTasks);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Error polling control log.");
            }
            Logger.LogDebug($"Log poll in {sw.ElapsedMilliseconds}ms");
            await Task.Delay(int.Parse(Config["LOGPOLLRATEMS"] ?? throw new InvalidOperationException("LOGPOLLRATEMS is required")), stoppingToken);
        }
    }
}
