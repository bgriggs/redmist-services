using BigMission.Streaming.Shared.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace BigMission.Streaming.ObsClient;

/// <summary>
/// Responsible for monitoring and sending OBS status, and will attempt to ensure OBS and the Loopy SRT Monitor is running.
/// </summary>
internal class ObsService : BackgroundService
{
    private readonly IConfiguration configuration;
    private readonly ObsClient obsClient;
    private readonly HubClient hubClient;
    private ILogger Logger { get; }
    private CancellationToken StoppingToken { get; set; } = default;

    public ObsService(ILoggerFactory loggerFactory, IConfiguration configuration, ObsClient obsClient, HubClient hubClient)
    {
        Logger = loggerFactory.CreateLogger(GetType().Name);
        this.configuration = configuration;
        this.obsClient = obsClient;
        this.hubClient = hubClient;

        // Subscribe to the OBS events
        obsClient.Obs.Connected += async (e) => await OnChange_ForceStatus();
        obsClient.Obs.Disconnected += async (e) => await OnChange_ForceStatus();
        obsClient.Obs.CurrentProgramSceneChanged += async (e) => await OnChange_ForceStatus();
        obsClient.Obs.StreamStateChanged += async (e) => await OnChange_ForceStatus();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        StoppingToken = stoppingToken;

        // Connect to the OBS server
        await obsClient.StartConnectionAsync(stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            var sw = Stopwatch.StartNew();

            // Get the OBS status
            var result = await TryUpdateObsStatus(stoppingToken);
            if (result.success && result.status != null)
            {
                Logger.LogDebug($"OBS status loop took {sw.ElapsedMilliseconds}ms");

                // Disable for now as this doesn't launch the GUI of the applications correctly
                //// Check if OBS is running and start it if not
                //if (!result.status.IsObsRunning)
                //{
                //    Logger.LogWarning("OBS is not running. Attempting to start OBS...");
                //    bool obsResult = TryStartObsProcess();
                //    if (!obsResult)
                //    {
                //        Logger.LogError("Failed to start OBS.");
                //    }
                //}

                //// Check if Loopy SRT Monitor is running and start it if not
                //if (!result.status.IsSrtMonitorRunning && result.status.IsObsRunning)
                //{
                //    Logger.LogWarning("Loopy SRT Monitor is not running. Attempting to start Loopy SRT Monitor...");
                //    bool loopyMonitorResult = TryStartLoopyMonitorProcess();
                //    if (!loopyMonitorResult)
                //    {
                //        Logger.LogError("Failed to start Loopy SRT Monitor.");
                //    }
                //}
            }
            var delay = 1000 - sw.ElapsedMilliseconds;
            if (delay > 0)
            {
                await Task.Delay((int)delay, stoppingToken);
            }
            else
            {
                Logger.LogDebug("OBS status loop is not keeping up with poll interval.");
            }
        }
    }

    /// <summary>
    /// Attempts to get status from OBS and and to the hub.
    /// </summary>
    /// <returns>true if sucessfully got OBS status and sent to hub</returns>
    private async Task<(bool success, ObsStatus? status)> TryUpdateObsStatus(CancellationToken stoppingToken)
    {
        try
        {
            var status = await obsClient.GetStatusAsync(stoppingToken);
            if (status != null)
            {
                await hubClient.SendStatusAsync(status, stoppingToken);
                return (true, status);
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to get OBS status.");
        }
        return (false, null);
    }

    private async Task OnChange_ForceStatus()
    {
        Logger.LogDebug("Forcing status update from event change...");
        await TryUpdateObsStatus(StoppingToken);
    }

    private bool TryStartObsProcess()
    {
        string? obsPath = configuration["Obs:ExePath"];
        if (string.IsNullOrWhiteSpace(obsPath))
        {
            Logger.LogError("OBS path not configured.");
            return false;
        }
        else
        {
            Logger.LogDebug($"OBS path: {obsPath}");
        }
        return TryStartProcess(obsPath);
    }

    private bool TryStartLoopyMonitorProcess()
    {
        string? loopyMonitorPath = configuration["LoopySrtMonitor:ExePath"];
        if (string.IsNullOrWhiteSpace(loopyMonitorPath))
        {
            Logger.LogError("LoopyMonitor path not configured.");
            return false;
        }
        else
        {
            Logger.LogDebug($"LoopyMonitor path: {loopyMonitorPath}");
        }
        return TryStartProcess(loopyMonitorPath);
    }

    private bool TryStartProcess(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                Logger.LogDebug($"File not found: {path}");
                return false;
            }
            var p = new ProcessStartInfo(path)
            {
                WorkingDirectory = Path.GetDirectoryName(path)
            };
            Process.Start(p);
            return true;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, $"Failed to start process: {path}");
        }
        return false;
    }
}
