using BigMission.Streaming.Services.Clients;
using BigMission.TestHelpers;
using Microsoft.AspNetCore.SignalR;
using StackExchange.Redis;

namespace BigMission.Streaming.Services.Hubs;

/// <summary>
/// SignalR hub for Nginx connections from Nginx Client agents running on Nginx server instances.
/// </summary>
public class NginxHub : Hub
{
    private readonly IConnectionMultiplexer cache;

    private ILogger Logger { get; }
    public IDateTimeHelper DateTime { get; }

    public NginxHub(ILoggerFactory loggerFactory, IConnectionMultiplexer cache, IDateTimeHelper dateTime)
    {
        Logger = loggerFactory.CreateLogger(GetType().Name);
        this.cache = cache;
        DateTime = dateTime;
    }

    public async override Task OnConnectedAsync()
    {
        await base.OnConnectedAsync();
        await SaveConnection(Context.ConnectionId);
    }

    public async override Task OnDisconnectedAsync(Exception? exception)
    {
        await base.OnDisconnectedAsync(exception);
        var db = cache.GetDatabase();
        await NginxClient.RemoveConnection(db, Context.ConnectionId);
    }

    private async Task SaveConnection(string connectionId)
    {
        var db = cache.GetDatabase();
        var hashEntries = new HashEntry[] { new(connectionId, DateTime.UtcNow.ToString()) };
        await db.HashSetAsync("NginxConnections", hashEntries);
    }
}
