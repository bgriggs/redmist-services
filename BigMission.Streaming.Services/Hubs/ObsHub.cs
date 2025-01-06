using BigMission.Streaming.Shared.Models;
using BigMission.TestHelpers;
using Microsoft.AspNetCore.SignalR;
using StackExchange.Redis;

namespace BigMission.Streaming.Services.Hubs;

public class ObsHub : BaseHub
{
    private readonly IHubContext<UIStatusHub> uiHub;

    private ILogger Logger { get; }
    public override string ConnectionCacheKey => "ObsConnections";
    public override string ConnectionNameRequest => "GetHostName";

    public ObsHub(ILoggerFactory loggerFactory, IConnectionMultiplexer cache, IDateTimeHelper dateTime, 
        HubConnectionContext connectionContext, IHubContext<UIStatusHub> uiHub) :
        base(loggerFactory, cache, dateTime, connectionContext)
    { 
        Logger = loggerFactory.CreateLogger(GetType().Name);
        this.uiHub = uiHub;
    }


    public async Task SendStatus(ObsStatus status)
    {
        Logger.LogInformation($"Received status update from client: {status}");
        await uiHub.Clients.All.SendAsync("UpdateObsStatus", status);
    }
}
