using BigMission.TestHelpers;
using StackExchange.Redis;

namespace BigMission.Streaming.Services.Hubs;

/// <summary>
/// SignalR hub for Nginx connections from Nginx Client agents running on Nginx server instances.
/// </summary>
public class NginxHub : BaseHub
{
    public override string ConnectionCacheKey => "NginxConnections";

    public override string ConnectionNameRequest => "GetHostName";

    public NginxHub(ILoggerFactory loggerFactory, IConnectionMultiplexer cache, IDateTimeHelper dateTime, HubConnectionContext connectionContext) :
        base(loggerFactory, cache, dateTime, connectionContext)
    { }
}
