using BigMission.Shared.SignalR;
using BigMission.Streaming.Shared.Models;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Net;

namespace BigMission.Streaming.ObsClient;

internal class HubClient : HubClientBase
{
    private HubConnection? hubConnection;
    private readonly ObsClient obsClient;

    private ILogger Logger { get; }

    public HubClient(ILoggerFactory loggerFactory, IConfiguration configuration, ObsClient obsClient) : base(loggerFactory, configuration)
    {
        Logger = loggerFactory.CreateLogger(GetType().Name);
        this.obsClient = obsClient;
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        Logger.LogInformation("Starting streaming server hub connection...");
        hubConnection = StartConnection(stoppingToken);

        // Get scenes from the server
        hubConnection.On("GetScenes", async () => await OnRequestScenesAsync(stoppingToken));

        // Set current program scene
        hubConnection.On<string, bool>("SetProgramScene", async (scene) => await OnSetProgramSceneAsync(scene, stoppingToken));

        // Streaming
        hubConnection.On("StartStreaming", async () => await OnStartStreamingAsync(stoppingToken));
        hubConnection.On("StopStreaming", async () => await OnStopStreamingAsync(stoppingToken));
        hubConnection.On("GetHostName", Dns.GetHostName);

        return Task.CompletedTask;
    }

    public async Task SendStatusAsync(ObsStatus obsStatus, CancellationToken stoppingToken)
    {
        if (hubConnection is null || hubConnection.State != HubConnectionState.Connected)
        {
            Logger.LogWarning("Cannot send status, hub is not connected.");
            return;
        }
        await hubConnection.InvokeAsync("SendStatus", obsStatus, stoppingToken);
    }

    private async Task<string[]> OnRequestScenesAsync(CancellationToken stoppingToken)
    {
        Logger.LogInformation("Received GetScenes");
        if (!obsClient.Obs.IsConnected)
        {
            Logger.LogWarning("OBS is not connected.");
            return [];
        }

        try
        {
            // Get scenes in OBS
            var scenes = await obsClient.Obs.GetSceneListAsync(stoppingToken);
            if (scenes != null)
            {
                return [.. scenes.Scenes.Select(s => s.Name)];
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to set scenes in OBS.");
        }
        return [];
    }

    private async Task<bool> OnSetProgramSceneAsync(string sceneName, CancellationToken stoppingToken)
    {
        Logger.LogInformation("Received SetProgramScene");
        if (!obsClient.Obs.IsConnected)
        {
            Logger.LogWarning("OBS is not connected.");
            return false;
        }

        try
        {
            // Get scenes in OBS
            var result = await obsClient.Obs.SetCurrentProgramSceneAsync(sceneName, stoppingToken);
            return result.RequestStatus.Result;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, $"Failed to set scene in OBS to {sceneName}.");
        }
        return false;
    }

    private async Task<bool> OnStartStreamingAsync(CancellationToken stoppingToken)
    {
        Logger.LogInformation("Received StartStreaming");
        if (!obsClient.Obs.IsConnected)
        {
            Logger.LogWarning("OBS is not connected.");
            return false;
        }

        try
        {
            // Get scenes in OBS
            var result = await obsClient.Obs.StartStreamAsync(stoppingToken);
            return result.RequestStatus.Result;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to start stremaing.");
        }
        return false;
    }

    private async Task<bool> OnStopStreamingAsync(CancellationToken stoppingToken)
    {
        Logger.LogInformation("Received StopStreaming");
        if (!obsClient.Obs.IsConnected)
        {
            Logger.LogWarning("OBS is not connected.");
            return false;
        }

        try
        {
            // Get scenes in OBS
            var result = await obsClient.Obs.StopStreamAsync(stoppingToken);
            return result.RequestStatus.Result;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to stop stremaing.");
        }
        return false;
    }
}
