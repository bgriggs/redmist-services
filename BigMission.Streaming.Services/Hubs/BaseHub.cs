using BigMission.TestHelpers;
using Microsoft.AspNetCore.SignalR;
using StackExchange.Redis;

namespace BigMission.Streaming.Services.Hubs;

public abstract class BaseHub : Hub
{
    private readonly IConnectionMultiplexer cache;
    public IDateTimeHelper DateTime { get; }
    public abstract string ConnectionCacheKey { get; }
    public abstract string ConnectionNameRequest { get; }

    public BaseHub(IConnectionMultiplexer cache, IDateTimeHelper dateTime)
    {
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
        await RemoveConnection(Context.ConnectionId);
    }

    protected virtual async Task SaveConnection(string connectionId)
    {
        var db = cache.GetDatabase();
        var hashEntries = new HashEntry[] { new(connectionId, DateTime.UtcNow.ToString()) };
        await db.HashSetAsync(ConnectionCacheKey, hashEntries);
    }

    public async Task RemoveConnection(string connectionId)
    {
        var db = cache.GetDatabase();
        await db.HashDeleteAsync(ConnectionCacheKey, connectionId);
    }
}
