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
        var details = Context.User?.Claims.FirstOrDefault(c => c.Type == ClaimTypes.Email);
        Logger.LogInformation($"Connection from user {details}");
        //await Groups.AddToGroupAsync(Context.ConnectionId, details.appId.ToString().ToUpper());
        await base.OnConnectedAsync();
    }

    public async override Task OnDisconnectedAsync(Exception? exception)
    {
        var details = Context.User?.Claims.FirstOrDefault(c => c.Type == ClaimTypes.Email);
        Logger.LogInformation($"User {details} disconnected.");
        //await Groups.RemoveFromGroupAsync(Context.ConnectionId, details.appId.ToString().ToUpper());
        await base.OnDisconnectedAsync(exception);
    }
}
