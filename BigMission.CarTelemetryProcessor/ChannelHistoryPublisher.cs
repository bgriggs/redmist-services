using BigMission.Cache.Models;
using BigMission.DeviceApp.Shared;
using BigMission.TestHelpers;
using Newtonsoft.Json;
using StackExchange.Redis;

namespace BigMission.CarTelemetryProcessor
{
    internal class ChannelHistoryPublisher : ITelemetryConsumer
    {
        public ILogger Logger { get; }
        public IDateTimeHelper DateTime { get; }

        private readonly IConnectionMultiplexer cacheMuxer;
        private readonly Dictionary<int, ChannelStatusDto> last = new();
        private const int HIST_MAX_LEN = 60;


        public ChannelHistoryPublisher(ILoggerFactory loggerFactory, IConnectionMultiplexer cache, IDateTimeHelper dateTime)
        {
            Logger = loggerFactory.CreateLogger(GetType().Name);
            cacheMuxer = cache;
            DateTime = dateTime;
        }

        public async Task ProcessTelemetryMessage(ChannelDataSetDto receivedTelem)
        {
            Logger.LogTrace($"ChannelHistoryPublisher received log: {receivedTelem.DeviceAppId} Count={receivedTelem.Data.Length}");
            if (!receivedTelem.Data.Any()) { return; }

            var history = new List<KeyValuePair<RedisKey, RedisValue>>();

            foreach (var ch in receivedTelem.Data)
            {
                if (ch.DeviceAppId == 0)
                {
                    ch.DeviceAppId = receivedTelem.DeviceAppId;
                }

                // Append changed value to the moving channel history list
                if (last.TryGetValue(ch.ChannelId, out ChannelStatusDto row))
                {
                    if (row.Value != ch.Value)
                    {
                        row.Value = ch.Value;
                        var p = CreateChannelHistoryCacheEntry(ch);
                        history.Add(p);
                    }

                    // Keep timestamp current when we get an update
                    row.Timestamp = ch.Timestamp;
                }
                else // Create new row
                {
                    var cr = new ChannelStatusDto { DeviceAppId = ch.DeviceAppId, ChannelId = ch.ChannelId, Value = ch.Value, Timestamp = ch.Timestamp };
                    last[ch.ChannelId] = cr;
                    var p = CreateChannelHistoryCacheEntry(ch);
                    history.Add(p);
                }
            }

            if (history.Any())
            {
                var db = cacheMuxer.GetDatabase();
                foreach (var h in history)
                {
                    // Use the head of the list as the newest value
                    var len = db.ListLeftPush(h.Key, h.Value);
                    if (len > HIST_MAX_LEN)
                    {
                        await db.ListTrimAsync(h.Key, 0, HIST_MAX_LEN - 1, flags: CommandFlags.FireAndForget);
                    }
                }
                Logger.LogTrace($"Cached new history for device: {receivedTelem.DeviceAppId}");
            }
        }

        private static KeyValuePair<RedisKey, RedisValue> CreateChannelHistoryCacheEntry(DeviceApp.Shared.ChannelStatusDto ch)
        {
            var v = JsonConvert.SerializeObject(ch);
            var p = new KeyValuePair<RedisKey, RedisValue>(string.Format(Consts.CHANNEL_HIST_KEY, ch.ChannelId), v);
            return p;
        }
    }
}
