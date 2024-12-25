using BigMission.Cache.Models;
using BigMission.DeviceApp.Shared;
using BigMission.TestHelpers;
using Newtonsoft.Json;
using StackExchange.Redis;

namespace BigMission.CarTelemetryProcessor;

/// <summary>
/// Update redis cache with new channel values and publish changes to clients.
/// </summary>
internal class StatusPublisher : ITelemetryConsumer
{
    private ILogger Logger { get; }
    private IDateTimeHelper DateTime { get; }

    private readonly IConnectionMultiplexer cacheMuxer;
    private const string DTO_LAST_TIMESTAMP = "dtolast.{0}";
    public const string StreamName = "ch-changes";
    public const string GroupName = "web-status";

    public StatusPublisher(ILoggerFactory loggerFactory, IConnectionMultiplexer cache, IDateTimeHelper dateTime)
    {
        Logger = loggerFactory.CreateLogger(GetType().Name);
        cacheMuxer = cache;
        DateTime = dateTime;
    }


    public async Task ProcessTelemetryMessage(ChannelDataSetDto receivedTelem)
    {
        try
        {
            Logger.LogTrace($"StatusPublisher received data: {receivedTelem.DeviceAppId} Count={receivedTelem.Data.Length}");
            if (!receivedTelem.Data.Any()) { return; }

            var db = cacheMuxer.GetDatabase();

            // Track last DTO timestamp and ensure the data is not older than the last received.
            if (await IsDataStale(receivedTelem, db))
            {
                Logger.LogInformation($"Received stale data for device: {receivedTelem.DeviceAppId}. Skipping.");
                return;
            }

            var kvps = new List<(RedisKey key, RedisValue chJson, float fval)>();
            foreach (var ch in receivedTelem.Data)
            {
                // Reset time to server time to prevent timeouts when data is being updated.
                ch.Timestamp = DateTime.UtcNow;
                var v = JsonConvert.SerializeObject(ch);
                var p = (string.Format(Consts.CHANNEL_KEY, ch.ChannelId), v, ch.Value);
                kvps.Add(p);
            }

            var changes = new List<(RedisKey k, RedisValue v)>();

            foreach (var kvp in kvps)
            {
                var oldJson = await db.StringSetAndGetAsync(kvp.key, kvp.chJson, expiry: TimeSpan.FromMinutes(1));

                // If the channel value has changed, add it to the changes stream for immediate push to clients.
                if (oldJson.HasValue)
                {
                    var oldCh = JsonConvert.DeserializeObject<ChannelStatusDto>(oldJson);
                    if (oldCh.Value != kvp.fval)
                    {
                        changes.Add((kvp.key, kvp.chJson));
                    }
                }
            }
            Logger.LogTrace($"Cached new status for device: {receivedTelem.DeviceAppId}");

            if (changes.Count > 0)
            {
                var nves = changes.Select(ch => new NameValueEntry(ch.k.ToString(), ch.v)).ToArray();
                Logger.LogDebug($"Adding stream update for {nves.Length} changed channels...");
                _ = db.StreamAddAsync(StreamName, nves)
                    .ContinueWith((t) => Logger.LogError(t.Exception, "Error updating stream"), TaskContinuationOptions.OnlyOnFaulted);
            }

            // Save last timestamp for this device updated
            _ = UpdateLastTimestamp(receivedTelem, db)
                .ContinueWith((t) => Logger.LogError(t.Exception, "Error updating timestamp"), TaskContinuationOptions.OnlyOnFaulted);
        }
        catch(Exception ex)
        {
            Logger.LogError(ex, "Error processing telemetry message.");
        }
    }

    private static async Task<bool> IsDataStale(ChannelDataSetDto receivedTelem, IDatabase db)
    {
        var key = string.Format(DTO_LAST_TIMESTAMP, receivedTelem.DeviceAppId);
        var last = await db.StringGetAsync(key);
        if (last.HasValue)
        {
            var lastTime = JsonConvert.DeserializeObject<DateTime>(last);
            return lastTime > receivedTelem.Timestamp;
        }
        return false;
    }

    private static async Task UpdateLastTimestamp(ChannelDataSetDto receivedTelem, IDatabase db)
    {
        var key = string.Format(DTO_LAST_TIMESTAMP, receivedTelem.DeviceAppId);
        await db.StringSetAsync(key, JsonConvert.SerializeObject(receivedTelem.Timestamp), expiry: TimeSpan.FromSeconds(30), flags: CommandFlags.FireAndForget);
    }
}
