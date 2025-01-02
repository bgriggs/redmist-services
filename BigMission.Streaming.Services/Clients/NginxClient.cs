using BigMission.Streaming.Services.Hubs;
using BigMission.Streaming.Shared.Models;
using BigMission.TestHelpers;
using Microsoft.AspNetCore.SignalR;
using StackExchange.Redis;
using static Microsoft.EntityFrameworkCore.DbLoggerCategory.Database;

namespace BigMission.Streaming.Services.Clients;

public class NginxClient
{
    private readonly IConnectionMultiplexer cache;
    private readonly IHubContext<NginxHub> nginxHub;

    private ILogger Logger { get; }
    public IDateTimeHelper DateTime { get; }

    public NginxClient(ILoggerFactory loggerFactory, IConnectionMultiplexer cache, IDateTimeHelper dateTime, IHubContext<NginxHub> nginxHub)
    {
        Logger = loggerFactory.CreateLogger(GetType().Name);
        this.cache = cache;
        DateTime = dateTime;
        this.nginxHub = nginxHub;
    }

    public async Task<List<(string connectionId, NginxInfo info)>> GetAllServerInfo()
    {
        var info = new List<(string, NginxInfo)>();
        var connections = await GetConnections();
        foreach (var connectionId in connections)
        {
            try
            {
                var client = nginxHub.Clients.Client(connectionId);
                var nginxInfo = await client.InvokeAsync<NginxInfo?>("GetInfo", default);
                if (nginxInfo != null)
                {
                    info.Add((connectionId, nginxInfo));
                }
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, $"Failed to get Nginx info for connection {connectionId}");
            }
        }
        return info;
    }

    private async Task<List<string>> GetConnections()
    {
        var db = cache.GetDatabase();
        var connections = await db.HashGetAllAsync("NginxConnections");
        return [.. connections.Select(c => c.Name.ToString())];
    }

    public async Task<NginxInfo?> UpdateServerStreams(string hostName, List<NginxStreamPush> streams)
    {
        // Get nginx info to resolve the host name
        var info = await GetAllServerInfo();
        var nginxInfo = info.FirstOrDefault(i => string.Equals(i.info.HostName, hostName, StringComparison.OrdinalIgnoreCase));

        if (nginxInfo == default)
        {
            Logger.LogError($"Failed to find Nginx info for host {hostName}");
            return null;
        }

        var client = nginxHub.Clients.Client(nginxInfo.connectionId);
        var result = await client.InvokeAsync<bool>("SetStreams", streams.ToArray(), default);

        if (result)
        {
            return await client.InvokeAsync<NginxInfo?>("GetInfo", default);
        }
        return null;
    }
}
