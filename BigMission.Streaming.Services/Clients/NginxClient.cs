using BigMission.Streaming.Services.Hubs;
using BigMission.Streaming.Services.Models;
using BigMission.Streaming.Shared.Models;
using BigMission.TestHelpers;
using Microsoft.AspNetCore.Connections;
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
    private readonly Hubs.HubConnectionContext connectionContext;

    private ILogger Logger { get; }
    public IDateTimeHelper DateTime { get; }

    public NginxClient(ILoggerFactory loggerFactory, IConnectionMultiplexer cache, IDateTimeHelper dateTime,
        IHubContext<NginxHub> nginxHub, Hubs.HubConnectionContext connectionContext)
    {
        Logger = loggerFactory.CreateLogger(GetType().Name);
        this.cache = cache;
        DateTime = dateTime;
        this.nginxHub = nginxHub;
        this.connectionContext = connectionContext;
    }

    public async Task<List<(string connectionId, NginxInfo info)>> GetAllServerInfo()
    {
        return await connectionContext.InvokeAllConnections<NginxHub, NginxInfo>("GetInfo");
    }
    //public async Task<List<(string connectionId, NginxInfo info)>> GetAllServerInfo()
    //{
    //    var info = new List<(string, NginxInfo)>();
    //    var connections = await connectionContext.GetConnectionsAsync<NginxHub>(cache.GetDatabase());
    //    foreach (var connectionId in connections)
    //    {
    //        try
    //        {
    //            var client = nginxHub.Clients.Client(connectionId);
    //            var nginxInfo = await client.InvokeAsync<NginxInfo?>("GetInfo", default);
    //            if (nginxInfo != null)
    //            {
    //                info.Add((connectionId, nginxInfo));
    //            }
    //        }
    //        catch (IOException)
    //        {
    //            Logger.LogDebug($"Connection {connectionId} is no longer available. Removing...");
    //            await connectionContext.RemoveConnectionAsync<NginxHub>(connectionId);
    //        }
    //        catch (Exception ex)
    //        {
    //            Logger.LogError(ex, $"Failed to get Nginx info for connection {connectionId}");
    //        }
    //    }
    //    return info;
    //}



    public async Task<NginxInfo?> UpdateServerStreams(string hostName, List<NginxStreamPush> streams)
    {
        var connectionId = await connectionContext.RequestConnectionIdByNameAsync<NginxHub>(hostName);
        if (connectionId != null)
        {
            var client = nginxHub.Clients.Client(connectionId);
            var result = await client.InvokeAsync<bool>("SetStreams", streams.ToArray(), default);

            if (result)
            {
                return await client.InvokeAsync<NginxInfo?>("GetInfo", default);
            }
        }
        return null;
        //// Get nginx info to resolve the host name
        //var info = await connectionContext.InvokeAllConnections<NginxHub, NginxInfo>("GetInfo");
        //var connections = info.Where(i => string.Equals(i.result.HostName, hostName, StringComparison.OrdinalIgnoreCase));
        //if (!connections.Any())
        //{
        //    Logger.LogError($"Failed to find Nginx info for host {hostName}");
        //    return null;
        //}

        //foreach (var conn in connections)
        //{
        //    var client = nginxHub.Clients.Client(conn.connectionId);
        //    try
        //    {
        //        var result = await client.InvokeAsync<bool>("SetStreams", streams.ToArray(), default);

        //        if (result)
        //        {
        //            return await client.InvokeAsync<NginxInfo?>("GetInfo", default);
        //        }
        //    }
        //    catch (IOException)
        //    {
        //        Logger.LogDebug($"Connection {conn.connectionId} is no longer available. Removing...");
        //        await connectionContext.RemoveConnectionAsync<NginxHub>(conn.connectionId);
        //    }
        //    catch (Exception ex)
        //    {
        //        Logger.LogError(ex, $"Failed to update streams for connection {conn.connectionId}");
        //    }
        //}
        //return null;
    }

    /// <summary>
    /// Update the status of all Nginx servers in the cache.
    /// </summary>
    /// <returns></returns>
    public async Task<List<NginxStatus>> UpdateNginxServiceStatus()
    {
        var info = await connectionContext.InvokeAllConnections<NginxHub, NginxInfo>("GetInfo");
        var result = new List<NginxStatus>();
        foreach (var conn in info)
        {
            var client = nginxHub.Clients.Client(conn.connectionId);
            try
            {
                var isActive = await client.InvokeAsync<bool>("GetIsActive", default);
                result.Add(new NginxStatus { ServerHostName = conn.result.HostName, IsActive = isActive });
            }
            catch (IOException)
            {
                Logger.LogDebug($"Connection {conn.connectionId} is no longer available. Removing...");
                await connectionContext.RemoveConnectionAsync<NginxHub>(conn.connectionId);
            }
            catch (ObjectDisposedException)
            {
                Logger.LogDebug($"Connection {conn.connectionId} is no longer available. Removing...");
                await connectionContext.RemoveConnectionAsync<NginxHub>(conn.connectionId);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, $"Failed to get Nginx status for connection {conn.connectionId}");
            }
        }

        // Save results to hash in cache
        var hashEntries = result.Select(r => new HashEntry(r.ServerHostName, r.IsActive)).ToArray();
        // Save the status to Redis
        var db = cache.GetDatabase();
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
