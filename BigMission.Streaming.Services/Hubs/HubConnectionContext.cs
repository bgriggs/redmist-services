using Microsoft.AspNetCore.SignalR;
using StackExchange.Redis;

namespace BigMission.Streaming.Services.Hubs;

/// <summary>
/// Holds hub references for access to client cache.
/// </summary>
public class HubConnectionContext
{
    private readonly Dictionary<string, IHubContext> _hubs = [];
    private readonly Dictionary<string, string> _connectionKeys = [];
    private readonly Dictionary<string, string> _connectionNameRequest = [];
    private readonly IConnectionMultiplexer cache;
    private ILogger Logger { get; }

    public HubConnectionContext(IConnectionMultiplexer cache, ILoggerFactory loggerFactory, 
        IHubContext<NginxHub> nginxContext, IHubContext<ObsHub> obsContext)
    {
        Logger = loggerFactory.CreateLogger(GetType().Name);
        this.cache = cache;

        _hubs[nameof(NginxHub)] = (IHubContext)nginxContext;
        _hubs[nameof(ObsHub)] = (IHubContext)obsContext;

        _connectionKeys[nameof(NginxHub)] = NginxHub.CONNECTION_CACHE_KEY;
        _connectionKeys[nameof(ObsHub)] = ObsHub.CONNECTION_CACHE_KEY;

        _connectionNameRequest[nameof(NginxHub)] = NginxHub.CONNECTION_NAME_REQUEST;
        _connectionNameRequest[nameof(ObsHub)] = ObsHub.CONNECTION_NAME_REQUEST;
    }

    public IHubContext? GetHub<T>() where T : BaseHub
    {
        if (_hubs.TryGetValue(typeof(T).Name, out var hub))
        {
            return hub;
        }
        return null;
    }

    public async Task RemoveConnectionAsync<T>(string connectionId) where T : BaseHub
    {
        if (_connectionKeys.TryGetValue(typeof(T).Name, out var key))
        {
            var db = cache.GetDatabase();
            await db.HashDeleteAsync(key, connectionId);
        }
        else
        {
            Logger.LogWarning($"No connection key found for {typeof(T).Name}");
        }
    }

    public async Task<List<string>> GetConnectionsAsync<T>(IDatabase cache) where T : BaseHub
    {
        if (_connectionKeys.TryGetValue(typeof(T).Name, out var key))
        {
            var connections = await cache.HashGetAllAsync(key);
            return [.. connections.Select(c => c.Name.ToString())];
        }
        return [];
    }

    /// <summary>
    /// Uses all stored connections to invoke a method on the client waiting for the result.
    /// </summary>
    /// <typeparam name="T">hub type</typeparam>
    /// <typeparam name="V">result type</typeparam>
    /// <param name="method">name of the request to invoke</param>
    /// <returns></returns>
    public async Task<List<(string connectionId, V result)>> InvokeAllConnections<T, V>(string method) where T : BaseHub
    {
        var hub = GetHub<T>();
        if (hub == null)
        {
            return [];
        }
        var c = cache.GetDatabase();
        var results = new List<(string, V)>();
        var connections = await GetConnectionsAsync<T>(c);
        foreach (var connectionId in connections)
        {
            try
            {
                var client = hub.Clients.Client(connectionId);
                var result = await client.InvokeAsync<V?>(method, default);
                if (result != null)
                {
                    results.Add((connectionId, result));
                }
            }
            catch (IOException)
            {
                Logger.LogDebug($"Connection {connectionId} is no longer available. Removing...");
                await RemoveConnectionAsync<NginxHub>(connectionId);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, $"Failed to get {method} info for connection {connectionId}");
            }
        }
        return results;
    }

    /// <summary>
    /// Makes a hard request to each client for their connection name and returns the connectionId for the matching name.
    /// </summary>
    /// <typeparam name="T">hub type</typeparam>
    /// <param name="connectionName">connection name like dns host name</param>
    /// <returns>corresponding connection ID</returns>
    public async Task<string> RequestConnectionIdByNameAsync<T>(string connectionName) where T : BaseHub
    {
        var hub = GetHub<T>();
        if (_connectionNameRequest.TryGetValue(typeof(T).Name, out var methodName))
        {
            var connectionNames = await InvokeAllConnections<T, string>(methodName);
            return connectionNames.FirstOrDefault(c => string.Equals(c.result, connectionName, StringComparison.OrdinalIgnoreCase)).connectionId;
        }
        return string.Empty;
    }
}
