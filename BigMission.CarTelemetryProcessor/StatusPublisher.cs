using BigMission.Cache.Models;
using BigMission.DeviceApp.Shared;
using BigMission.TestHelpers;
using Newtonsoft.Json;
using StackExchange.Redis;

namespace BigMission.CarTelemetryProcessor;

internal class StatusPublisher : ITelemetryConsumer
{
    private ILogger Logger { get; }
    private IDateTimeHelper DateTime { get; }

    private readonly IConnectionMultiplexer cacheMuxer;
    private const string DTO_LAST_TIMESTAMP = "dtolast.{0}";


    public StatusPublisher(ILoggerFactory loggerFactory, IConnectionMultiplexer cache, IDateTimeHelper dateTime)
    {
        Logger = loggerFactory.CreateLogger(GetType().Name);
        cacheMuxer = cache;
        DateTime = dateTime;
    }


    public async Task ProcessTelemetryMessage(ChannelDataSetDto receivedTelem)
    {
        Logger.LogTrace($"StatusPublisher received log: {receivedTelem.DeviceAppId} Count={receivedTelem.Data.Length}");
        if (!receivedTelem.Data.Any()) { return; }

        // Track last DTO timestamp and ensure the data is not older than the last received.
        if (await IsDataStale(receivedTelem))
        {
            Logger.LogInformation($"Received stale data for device: {receivedTelem.DeviceAppId}");
            return;
        }

        var kvps = new List<KeyValuePair<RedisKey, RedisValue>>();
        foreach (var ch in receivedTelem.Data)
        {
            // Reset time to server time to prevent timeouts when data is being updated.
            ch.Timestamp = DateTime.UtcNow;
            var v = JsonConvert.SerializeObject(ch);
            var p = new KeyValuePair<RedisKey, RedisValue>(string.Format(Consts.CHANNEL_KEY, ch.ChannelId), v);
            kvps.Add(p);
        }

        var db = cacheMuxer.GetDatabase();
        foreach (var kvp in kvps)
        {
            await db.StringSetAsync(kvp.Key, kvp.Value, expiry: TimeSpan.FromMinutes(1), flags: CommandFlags.FireAndForget);
        }
        Logger.LogTrace($"Cached new status for device: {receivedTelem.DeviceAppId}");

        // Save last timestamp for this device.
        await UpdateLastTimestamp(receivedTelem);
    }

    private async Task<bool> IsDataStale(ChannelDataSetDto receivedTelem)
    {
        var db = cacheMuxer.GetDatabase();
        var key = string.Format(DTO_LAST_TIMESTAMP, receivedTelem.DeviceAppId);
        var last = await db.StringGetAsync(key);
        if (last.HasValue)
        {
            var lastTime = JsonConvert.DeserializeObject<DateTime>(last);
            return lastTime > receivedTelem.Timestamp;
        }
        return false;
    }

    private async Task UpdateLastTimestamp(ChannelDataSetDto receivedTelem)
    {
        var db = cacheMuxer.GetDatabase();
        var key = string.Format(DTO_LAST_TIMESTAMP, receivedTelem.DeviceAppId);
        await db.StringSetAsync(key, JsonConvert.SerializeObject(receivedTelem.Timestamp), expiry: TimeSpan.FromSeconds(30), flags: CommandFlags.FireAndForget);
    }
}
