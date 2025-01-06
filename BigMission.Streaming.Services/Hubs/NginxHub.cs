using BigMission.TestHelpers;
using StackExchange.Redis;

namespace BigMission.Streaming.Services.Hubs;

/// <summary>
/// SignalR hub for Nginx connections from Nginx Client agents running on Nginx server instances.
/// </summary>
public class NginxHub : BaseHub
{
    public const string CONNECTION_CACHE_KEY = "NginxConnections";
    public override string ConnectionCacheKey => CONNECTION_CACHE_KEY;

    public const string CONNECTION_NAME_REQUEST = "GetHostName";
    public override string ConnectionNameRequest => CONNECTION_NAME_REQUEST;

    public NginxHub(IConnectionMultiplexer cache, IDateTimeHelper dateTime) :
        base(cache, dateTime)
    { }
}
