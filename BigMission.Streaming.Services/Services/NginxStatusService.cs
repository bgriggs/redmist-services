
using BigMission.Streaming.Services.Clients;
using BigMission.Streaming.Services.Hubs;
using BigMission.Streaming.Services.Models;
using Microsoft.AspNetCore.SignalR;
using System.Diagnostics;

namespace BigMission.Streaming.Services.Services;

/// <summary>
/// Checks with the Nginx server for its status of the systemd service.
/// </summary>
public class NginxStatusService : BackgroundService
{
    private readonly NginxClient nginxClient;
    private readonly IHubContext<UIStatusHub> uiHub;

    private ILogger Logger { get; }

    public NginxStatusService(ILoggerFactory loggerFactory, NginxClient nginxClient, IHubContext<UIStatusHub> uiHub)
    {
        Logger = loggerFactory.CreateLogger(GetType().Name);
        this.nginxClient = nginxClient;
        this.uiHub = uiHub;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Check the status of the Nginx server every 5 seconds.
        while (!stoppingToken.IsCancellationRequested)
        {
            var sw = Stopwatch.StartNew();
            try
            {
                Logger.LogDebug("Checking Nginx service status...");

                Logger.LogTrace("Getting last Nginx service status...");
                var lastStatus = await nginxClient.GetNginxStatus();

                Logger.LogTrace("Getting new Nginx service status...");
                var currentStatus = await nginxClient.UpdateNginxServiceStatus();

                // Find changes in status
                var statusChanged = FindChanges(lastStatus, currentStatus);
                await uiHub.Clients.All.SendAsync("NginxStatusChanged", statusChanged, cancellationToken: stoppingToken);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Failed to check Nginx service status.");
            }

            Logger.LogTrace($"Nginx service status check took {sw.ElapsedMilliseconds}ms.");

            var delay = TimeSpan.FromSeconds(5) - sw.Elapsed;
            if (delay > TimeSpan.Zero)
            {
                await Task.Delay(delay, stoppingToken);
            }
            else
            {
                Logger.LogWarning("Nginx service status check took longer than 5 seconds.");
            }
        }
    }

    /// <summary>
    /// Find changes in status between the last status and the current status.
    /// </summary>
    private static List<NginxStatus> FindChanges(List<NginxStatus> lastStatus, List<NginxStatus> currentStatus)
    {
        var changes = new List<NginxStatus>();
        foreach (var status in currentStatus)
        {
            var last = lastStatus.FirstOrDefault(i => i.ServerHostName == status.ServerHostName);
            if (last == null || last.IsActive != status.IsActive)
            {
                changes.Add(status);
            }

            // Find status that is no longer active
            foreach (var ls in lastStatus)
            {
                if (!currentStatus.Any(i => i.ServerHostName == ls.ServerHostName))
                {
                    changes.Add(new NginxStatus
                    {
                        ServerHostName = ls.ServerHostName,
                        IsActive = false
                    });
                }
            }
        }
        return changes;
    }
}
