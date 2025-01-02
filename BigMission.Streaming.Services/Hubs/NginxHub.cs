using BigMission.Streaming.Shared.Models;
using BigMission.TestHelpers;
using Microsoft.AspNetCore.SignalR;
using StackExchange.Redis;

namespace BigMission.Streaming.Services.Hubs;

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
        var info = await Clients.Caller.InvokeAsync<NginxInfo?>("GetInfo", default);
        if (info == null)
        {
            Logger.LogError("Failed to get Nginx info.");
        }
        else
        {
            await SaveConnection(Context.ConnectionId);
            Logger.LogInformation($"Connected to Nginx {info.HostName}");
        }
        Context.Items.Add($"info", info);
    }

    public async override Task OnDisconnectedAsync(Exception? exception)
    {
        await base.OnDisconnectedAsync(exception);
        await RemoveConnection(Context.ConnectionId);
    }

    

    private async Task SaveConnection(string connectionId)
    {
        var db = cache.GetDatabase();
        var hashEntries = new HashEntry[] { new(connectionId, DateTime.UtcNow.ToString()) };
        await db.HashSetAsync("NginxConnections", hashEntries);
    }

    private async Task RemoveConnection(string connectionId)
    {
        var db = cache.GetDatabase();
        await db.HashDeleteAsync("NginxConnections", connectionId);
    }
}
