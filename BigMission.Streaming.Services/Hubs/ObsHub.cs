using BigMission.Streaming.Shared.Models;
using BigMission.TestHelpers;
using Microsoft.AspNetCore.SignalR;
using StackExchange.Redis;

namespace BigMission.Streaming.Services.Hubs;

public class ObsHub : BaseHub
{
    private readonly IHubContext<UIStatusHub> uiHub;

    private ILogger Logger { get; }

    public const string CONNECTION_CACHE_KEY = "ObsConnections";
    public override string ConnectionCacheKey => CONNECTION_CACHE_KEY;

    public const string CONNECTION_NAME_REQUEST = "GetHostName";
    public override string ConnectionNameRequest => CONNECTION_NAME_REQUEST;

    public ObsHub(ILoggerFactory loggerFactory, IConnectionMultiplexer cache, IDateTimeHelper dateTime, 
        IHubContext<UIStatusHub> uiHub) :
        base(cache, dateTime)
    { 
        Logger = loggerFactory.CreateLogger(GetType().Name);
        this.uiHub = uiHub;
    }


    public async Task SendStatus(ObsStatus status)
    {
        Logger.LogDebug($"Received status update from client: {status}");
        await uiHub.Clients.All.SendAsync("UpdateObsStatus", status);
    }
}
