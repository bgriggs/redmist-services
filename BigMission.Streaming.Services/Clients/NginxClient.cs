using BigMission.Streaming.Services.Hubs;
using BigMission.Streaming.Services.Models;
using BigMission.Streaming.Shared.Models;
using BigMission.TestHelpers;
using Microsoft.AspNetCore.SignalR;
using StackExchange.Redis;
using static Microsoft.EntityFrameworkCore.DbLoggerCategory.Database;

namespace BigMission.Streaming.Services.Clients;

/// <summary>
/// Provides methods to interact with Nginx servers.
/// </summary>
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
            catch (IOException)
            {
                Logger.LogDebug($"Connection {connectionId} is no longer available. Removing...");
                var db = cache.GetDatabase();
                await RemoveConnection(db, connectionId);
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
        var connections = info.Where(i => string.Equals(i.info.HostName, hostName, StringComparison.OrdinalIgnoreCase));
        if (!connections.Any())
        {
            Logger.LogError($"Failed to find Nginx info for host {hostName}");
            return null;
        }

        foreach (var conn in connections)
        {
            var client = nginxHub.Clients.Client(conn.connectionId);
            try
            {
                var result = await client.InvokeAsync<bool>("SetStreams", streams.ToArray(), default);

                if (result)
                {
                    return await client.InvokeAsync<NginxInfo?>("GetInfo", default);
                }
            }
            catch (IOException)
            {
                Logger.LogDebug($"Connection {conn.connectionId} is no longer available. Removing...");
                var db = cache.GetDatabase();
                await RemoveConnection(db, conn.connectionId);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, $"Failed to update streams for connection {conn.connectionId}");
            }
        }
        return null;
    }

    public static async Task RemoveConnection(IDatabase db, string connectionId)
    {
        await db.HashDeleteAsync("NginxConnections", connectionId);
    }

    /// <summary>
    /// Update the status of all Nginx servers in the cache.
    /// </summary>
    /// <returns></returns>
    public async Task<List<NginxStatus>> UpdateNginxServiceStatus()
    {
        var info = await GetAllServerInfo();
        var result = new List<NginxStatus>();
        var db = cache.GetDatabase();
        foreach (var conn in info)
        {
            var client = nginxHub.Clients.Client(conn.connectionId);
            try
            {
                var isActive = await client.InvokeAsync<bool>("GetIsActive", default);
                result.Add(new NginxStatus { ServerHostName = conn.info.HostName, IsActive = isActive });
            }
            catch (IOException)
            {
                Logger.LogDebug($"Connection {conn.connectionId} is no longer available. Removing...");
                await RemoveConnection(db, conn.connectionId);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, $"Failed to get Nginx status for connection {conn.connectionId}");
            }
        }

        // Save results to hash in cache
        var hashEntries = result.Select(r => new HashEntry(r.ServerHostName, r.IsActive)).ToArray();
        // Save the status to Redis
        await db.HashSetAsync("NginxStatus", hashEntries);
        await db.KeyExpireAsync("NginxStatus", TimeSpan.FromMinutes(1));

        return result;
    }

    /// <summary>
    /// Get the status of all Nginx servers.
    /// </summary>
    /// <returns></returns>
    public async Task<List<NginxStatus>> GetNginxStatus()
    {
        var db = cache.GetDatabase();
        var status = await db.HashGetAllAsync("NginxStatus");
        return status.Select(s => new NginxStatus 
        { 
            ServerHostName = s.Name.ToString(), 
            IsActive = s.Value.ToString() == "1" 
        }).ToList();
    }
}
