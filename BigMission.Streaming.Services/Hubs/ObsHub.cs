using Microsoft.AspNetCore.SignalR;
using System.Security.Claims;

namespace BigMission.Streaming.Services.Hubs;

public class ObsHub : Hub
{
    private ILogger Logger { get; }

    public ObsHub(ILoggerFactory loggerFactory)
    {
        Logger = loggerFactory.CreateLogger(GetType().Name);
    }

    public async override Task OnConnectedAsync()
    {
        await base.OnConnectedAsync();
    }

    public async override Task OnDisconnectedAsync(Exception? exception)
    {
        await base.OnDisconnectedAsync(exception);
    }
}
