using BigMission.CommandTools;
using BigMission.CommandTools.Models;
using BigMission.DeviceApp.Shared;
using Microsoft.AspNetCore.SignalR;
using System.Security.Claims;

namespace BigMission.ServiceHub.Hubs
{
    public class EdgeDeviceHub : Hub
    {
        private NLog.ILogger Logger { get; }

        public EdgeDeviceHub(NLog.ILogger logger)
        {
            Logger = logger;
        }

        public async override Task OnConnectedAsync()
        {
            var details = GetAuthDetails();
            Logger.Debug($"Connection from ID {details.appId}");
            await Groups.AddToGroupAsync(Context.ConnectionId, details.appId.ToString().ToUpper());
            await base.OnConnectedAsync();
        }

        public async override Task OnDisconnectedAsync(Exception? exception)
        {
            var details = GetAuthDetails();
            Logger.Debug($"Device {details.appId} disconnected.");
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, details.appId.ToString().ToUpper());
            await base.OnDisconnectedAsync(exception);
        }

        private (Guid appId, string apiKey) GetAuthDetails()
        {
            var claim = Context.User?.Claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier);
            if (claim != null)
            {
                var token = claim.Value.Remove(0, 7);
                var authData = KeyUtilities.DecodeToken(token);
                return (authData.appId, authData.apiKey);
            }
            else
            {
                throw new UnauthorizedAccessException("Invalid claim on connection.");
            }
        }

        public async Task<bool> SendCommandV1(Command command, Guid destinationGuid)
        {
            Logger.Debug($"SendCommandV1 {command.CommandType} to {destinationGuid}.");
            var c = Clients.Group(destinationGuid.ToString().ToUpper());
            if (c != null)
            {
                await c.SendAsync("ReceiveCommandV1", command);
                return true;
            }

            return false;
        }

        public void RegisterHeartbeatV1(DeviceAppHeartbeat heartbeat)
        {
            Logger.Debug($"Heartbeat from {heartbeat.DeviceAppId}.");
        }
    }
}
