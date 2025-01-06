using BigMission.Streaming.Services.Hubs;
using Microsoft.AspNetCore.SignalR;

namespace BigMission.Streaming.Services.Clients;

public class ObsClient
{
    private ILogger Logger { get; }
    private readonly IHubContext<ObsHub> obsHub;
    private readonly Hubs.HubConnectionContext connectionContext;

    public ObsClient(ILoggerFactory loggerFactory, IHubContext<ObsHub> obsHub, Hubs.HubConnectionContext connectionContext)
    {
        Logger = loggerFactory.CreateLogger(GetType().Name);
        this.obsHub = obsHub;
        this.connectionContext = connectionContext;
    }

    public async Task<string[]> GetScenes(string hostName)
    {
        var client = await GetClientProxyByName(hostName);
        if (client == null)
        {
            return [];
        }
        return await client.InvokeAsync<string[]>("GetScenes", default);
    }

    public async Task<bool> SetProgramScene(string hostName, string scene)
    {
        var client = await GetClientProxyByName(hostName);
        if (client == null)
        {
            return false;
        }
        return await client.InvokeAsync<bool>("SetProgramScene", scene, default);
    }

    public async Task<bool> StartStreaming(string hostName)
    {
        var client = await GetClientProxyByName(hostName);
        if (client == null)
        {
            return false;
        }
        return await client.InvokeAsync<bool>("StartStreaming", default);
    }

    public async Task<bool> StopStreaming(string hostName)
    {
        var client = await GetClientProxyByName(hostName);
        if (client == null)
        {
            return false;
        }
        return await client.InvokeAsync<bool>("StopStreaming", default);
    }

    private async Task<ISingleClientProxy?> GetClientProxyByName(string hostName)
    {
        var connectionId = await connectionContext.RequestConnectionIdByNameAsync<ObsHub>(hostName);
        if (connectionId == null)
        {
            Logger.LogWarning($"No connection found for host {hostName}");
            return null;
        }
        return obsHub.Clients.Client(connectionId);
    }
}
