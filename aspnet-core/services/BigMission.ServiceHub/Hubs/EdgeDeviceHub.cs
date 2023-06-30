using BigMission.CommandTools;
using BigMission.CommandTools.Models;
using BigMission.DeviceApp.Shared;
using Microsoft.AspNetCore.SignalR;
using NLog.Targets.ServiceHub;
using System.Security.Claims;

namespace BigMission.ServiceHub.Hubs
{
    public class EdgeDeviceHub : Hub
    {
        private ILogger Logger { get; }
        private DataClearinghouse Clearinghouse { get; }

        public EdgeDeviceHub(ILoggerFactory loggerFactory, DataClearinghouse clearinghouse)
        {
            Logger = loggerFactory.CreateLogger(GetType().Name);
            Clearinghouse = clearinghouse;
        }

        public async override Task OnConnectedAsync()
        {
            var details = GetAuthDetails();
            Logger.LogDebug($"Connection from ID {details.appId}");
            await Groups.AddToGroupAsync(Context.ConnectionId, details.appId.ToString().ToUpper());
            await base.OnConnectedAsync();
        }

        public async override Task OnDisconnectedAsync(Exception? exception)
        {
            var details = GetAuthDetails();
            Logger.LogDebug($"Device {details.appId} disconnected.");
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
            Logger.LogDebug($"SendCommandV1 {command.CommandType} to {destinationGuid}.");
            var c = Clients.Group(destinationGuid.ToString().ToUpper());
            if (c != null)
            {
                await c.SendAsync("ReceiveCommandV1", command);
                return true;
            }

            return false;
        }

        /// <summary>
        /// Handle a heartbeat message from a device can app.
        /// </summary>
        /// <param name="heartbeat"></param>
        /// <returns></returns>
        public async Task RegisterHeartbeatV1(DeviceAppHeartbeat heartbeat)
        {
            Logger.LogDebug($"Heartbeat from {heartbeat.DeviceAppId}.");
            await Clearinghouse.PublishHeartbeat(heartbeat);
        }

        /// <summary>
        /// Process a console log message.
        /// </summary>
        /// <param name="message"></param>
        /// <returns></returns>
        public async Task PostLogMessage(LogMessage message)
        {
            Logger.LogTrace($"RX log from: {message.SourceKey}");
            await Clearinghouse.PublishConsoleLog(message);
        }

        public async Task ReceiveChannelStatusV1(ChannelDataSetDto dataSet)
        {
            Logger.LogTrace($"Channel status from {dataSet.DeviceAppId}.");
            await Clearinghouse.PublishChannelStatus(dataSet);
        }

        public async Task ReceiveKeypadStatusV1(KeypadStatusDto keypadStatus)
        {
            Logger.LogTrace($"Keyboard status from {keypadStatus.DeviceAppId}.");
            await Clearinghouse.PublishKeyboardStatus(keypadStatus);
        }
    }
}
