using BigMission.CommandTools;
using BigMission.CommandTools.Models;
using Microsoft.AspNetCore.SignalR;
using System.Security.Claims;

namespace BigMission.ServiceHub.Hubs
{
    public class EdgeDeviceHub : Hub
    {
        public async override Task OnConnectedAsync()
        {
            var details = GetAuthDetails();
            await Groups.AddToGroupAsync(Context.ConnectionId, details.apiKey.ToString());
            await base.OnConnectedAsync();
        }

        public async override Task OnDisconnectedAsync(Exception? exception)
        {
            var details = GetAuthDetails();
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, details.apiKey.ToString());
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
            var c = Clients.Group(destinationGuid.ToString());
            if (c != null)
            {
                await c.SendAsync("ReceiveCommandV1", command);
                return true;
            }

            return false;
        }
    }
}
