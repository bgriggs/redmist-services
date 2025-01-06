using StackExchange.Redis;

namespace BigMission.Streaming.Services.Hubs;

/// <summary>
/// Holds hub references for access to client cache.
/// </summary>
public class HubConnectionContext
{
    private readonly Dictionary<string, BaseHub> _hubs = [];
    private readonly IConnectionMultiplexer cache;
    private ILogger Logger { get; }

    public HubConnectionContext(IConnectionMultiplexer cache, ILoggerFactory loggerFactory)
    {
        Logger = loggerFactory.CreateLogger(GetType().Name);
        this.cache = cache;
    }

    public void RegisterHub(BaseHub hub)
    {
        _hubs[hub.GetType().Name] = hub;
    }

    public T? GetHub<T>() where T : BaseHub
    {
        if (_hubs.TryGetValue(typeof(T).Name, out var hub))
        {
            return hub as T;
        }
        return null;
    }

    public async Task RemoveConnectionAsync<T>(string connectionId) where T : BaseHub
    {
        var hub = GetHub<T>();
        if (hub != null)
        {
            await hub.RemoveConnection(connectionId);
        }
    }

    public async Task<List<string>> GetConnectionsAsync<T>(IDatabase cache) where T : BaseHub
    {
        var hub = GetHub<T>();
        if (hub != null)
        {
            var connections = await cache.HashGetAllAsync(hub.ConnectionCacheKey);
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
                var result = await client.InvokeCoreAsync<V?>(method, [], default);
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
            catch (ObjectDisposedException)
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
        if (hub == null)
        {
            return string.Empty;
        }

        var connectionNames = await InvokeAllConnections<T, string>(hub.ConnectionNameRequest);
        return connectionNames.FirstOrDefault(c => string.Equals(c.result, connectionName, StringComparison.OrdinalIgnoreCase)).connectionId;
    }
}
