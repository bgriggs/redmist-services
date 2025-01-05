using BigMission.Streaming.Shared.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using ObsStrawket;
using ObsStrawket.DataTypes.Predefineds;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Net;
using System.Net.Sockets;

namespace BigMission.Streaming.ObsClient;

internal class ObsClient
{
    private ObsClientSocket obs;
    public ObsClientSocket Obs => obs;

    private readonly IConfiguration configuration;
    private ILogger Logger { get; }
    private readonly SemaphoreSlim connectSemaphore = new(1);
    private readonly SemaphoreSlim statusSemaphore = new(1);

    private string hostName = string.Empty;
    private string hostIp = string.Empty;


    public ObsClient(ILoggerFactory loggerFactory, IConfiguration configuration)
    {
        Logger = loggerFactory.CreateLogger(GetType().Name);
        this.configuration = configuration;
        obs = new ObsClientSocket(Logger);
        obs.Connected += (e) =>
        {
            Logger.LogInformation($"Connected to OBS at {e}");
        };
        obs.Disconnected += async (e) =>
        {
            Logger.LogWarning("Disconnected from OBS. Starting reconnect...");
            await StartConnectionAsync();
        };
    }

    /// <summary>
    /// Connects to the OBS server with ongoing retries.
    /// </summary>
    public async Task StartConnectionAsync(CancellationToken stoppingToken = default)
    {
        var url = configuration["Obs:Url"] ?? throw new InvalidOperationException("OBS URL is not configured.");
        var password = configuration["Obs:Password"] ?? throw new InvalidOperationException("OBS password is not configured.");

        Logger.LogDebug($"OBS URL: {url}");
        Logger.LogDebug($"OBS Password: {new string('*', password.Length)}");

        // Get hostname and IP
        hostName = Dns.GetHostName();
        var ips = await Dns.GetHostAddressesAsync(hostName, stoppingToken);
        foreach (var ip in ips)
        {
            Logger.LogTrace($"IP: {ip}");
        }
        hostIp = ips.FirstOrDefault(ip => ip.AddressFamily == AddressFamily.InterNetwork && !ip.ToString().StartsWith("127."))?.ToString() ?? "?";

        _ = Task.Run(async () =>
        {
            bool connectAcquired = false;
            try
            {
                connectAcquired = await connectSemaphore.WaitAsync(0, stoppingToken);

                if (!connectAcquired)
                {
                    Logger.LogDebug("Connection already in progress, skipping.");
                    return;
                }

                while (!stoppingToken.IsCancellationRequested)
                {
                    try
                    {
                        if (!obs.IsConnected)
                        {
                            Logger.LogDebug("Connecting to OBS...");
                            await obs.ConnectAsync(new Uri(url), password, cancellation: stoppingToken);
                            if (obs.IsConnected)
                            {
                                Logger.LogInformation("Connected to OBS.");
                                return;
                            }
                            else
                            {
                                Logger.LogError("Failed to connect to OBS.");
                            }
                        }
                        else
                        {
                            return;
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.LogError(ex, $"Error connecting to OBS: {ex.Message}");
                    }

                    await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
                }
            }
            finally
            {
                if (connectAcquired)
                {
                    connectSemaphore.Release();
                }
            }
        }, stoppingToken);
    }

    /// <summary>
    /// Get current status from the OBS server.
    /// </summary>
    /// <param name="stoppingToken"></param>
    /// <returns>Status or null if request was already in progress.</returns>
    public async Task<ObsStatus?> GetStatusAsync(CancellationToken stoppingToken = default)
    {
        bool lockAcquired = false;
        try
        {
            lockAcquired = await statusSemaphore.WaitAsync(0, stoppingToken);
            if (!lockAcquired)
            {
                Logger.LogDebug("Status already in progress, skipping.");
                return null;
            }

            var status = new ObsStatus
            {
                HostName = hostName,
                HostIp = hostIp
            };

            Task<GetStatsResponse>? statsTask = null;
            Task<GetStreamStatusResponse>? streamTask = null;
            Task<GetCurrentProgramSceneResponse>? sceneTask = null;
            Task<GetInputSettingsResponse>? inputSettingsTask = null;
            if (obs.IsConnected)
            {
                statsTask = obs.GetStatsAsync(stoppingToken);
                streamTask = obs.GetStreamStatusAsync(stoppingToken);
                sceneTask = obs.GetCurrentProgramSceneAsync(stoppingToken);

                string? videoInputName = configuration["Obs:VideoInputName"];
                if (!string.IsNullOrWhiteSpace(videoInputName))
                {
                    inputSettingsTask = obs.GetInputSettingsAsync(videoInputName, stoppingToken);
                }
                else
                {
                    Logger.LogDebug("Video input name is not configured.");
                }
            }

            // Check for OBS process
            try
            {
                var obsProcesses = Process.GetProcessesByName("obs64");
                status.IsObsRunning = obsProcesses.Length > 0;
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Error checking for OBS process.");
            }

            // Check for SRT Monitor process
            try
            {
                var looperProcesses = Process.GetProcessesByName("loopy_srt_monitor");
                status.IsSrtMonitorRunning = looperProcesses.Length > 0;
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Error checking for SRT monitor process.");
            }

            status.WebsocketConnected = obs.IsConnected;

            // Get CPU and memory usage
            if (statsTask is not null)
            {
                var stats = await statsTask;
                status.CpuUsage = stats.CpuUsage;
                status.MemoryUsage = stats.MemoryUsage;
            }

            // Get stream stats
            if (streamTask is not null)
            {
                var streamStatus = await streamTask;
                status.IsStreaming = streamStatus.OutputActive;
                status.StreamingBitrate = streamStatus.OutputBytes;
                status.StreamDurationMs = streamStatus.OutputDuration;
            }

            // Get current scene
            if (sceneTask is not null)
            {
                var scene = await sceneTask;
                status.CurrentScene = scene.CurrentProgramSceneName;
            }

            // Get video input settings
            if (inputSettingsTask is not null)
            {
                var inputSettings = await inputSettingsTask;
                if (inputSettings.InputSettings.TryGetValue("input", out object? input))
                {
                    status.VideoSrtSource = input?.ToString() ?? "?";
                }
                else
                {
                    Logger.LogTrace("Video input settings not found in OBS response.");
                }
            }

            return status;
        }
        finally
        {
            if (lockAcquired)
            {
                statusSemaphore.Release();
            }
        }
    }
}
