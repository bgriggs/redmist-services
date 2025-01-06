using Microsoft.AspNetCore.SignalR;

namespace BigMission.Streaming.Services.Hubs;

public class UIStatusHub : Hub
{
    private ILogger Logger { get; }

    public UIStatusHub(ILoggerFactory loggerFactory)
    {
        Logger = loggerFactory.CreateLogger(GetType().Name);
    }

    public override Task OnConnectedAsync()
    {
        Logger.LogInformation($"Client connected to UIStatusHub {Context.ConnectionId}");
        base.OnConnectedAsync();
        return Task.CompletedTask;
    }
}
