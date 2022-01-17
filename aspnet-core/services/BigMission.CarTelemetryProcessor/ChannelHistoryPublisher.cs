using BigMission.Cache;
using BigMission.DeviceApp.Shared;
using BigMission.TestHelpers;
using Microsoft.Extensions.Configuration;
using NLog;
using StackExchange.Redis;

namespace BigMission.CarTelemetryProcessor
{
    internal class ChannelHistoryPublisher : ITelemetryConsumer
    {
        public ILogger Logger { get; }
        public IDateTimeHelper DateTime { get; }

        private readonly ConnectionMultiplexer cacheMuxer;
        private readonly Dictionary<int, ChannelStatusDto> last = new();
        private const int HIST_MAX_LEN = 60;


        public ChannelHistoryPublisher(ILogger logger, IConfiguration config, IDateTimeHelper dateTime)
        {
            Logger = logger;
            DateTime = dateTime;
            cacheMuxer = ConnectionMultiplexer.Connect(config["RedisConn"]);
        }

        public async Task ProcessTelemetryMessage(ChannelDataSetDto receivedTelem)
        {
            Logger.Trace($"ChannelHistoryPublisher received log: {receivedTelem.DeviceAppId} Count={receivedTelem.Data.Length}");
            if (!receivedTelem.Data.Any()) { return; }

            var history = new List<KeyValuePair<RedisKey, RedisValue>>();

            foreach (var ch in receivedTelem.Data)
            {
                if (ch.DeviceAppId == 0)
                {
                    ch.DeviceAppId = receivedTelem.DeviceAppId;
                }

                // Append changed value to the moving channel history list
                if (last.TryGetValue(ch.ChannelId, out ChannelStatusDto? row))
                {
                    if (row.Value != ch.Value)
                    {
                        row.Value = ch.Value;
                        var p = ChannelContext.CreateChannelHistoryCacheEntry(ch);
                        history.Add(p);
                    }

                    // Keep timestamp current when we get an update
                    row.Timestamp = ch.Timestamp;
                }
                else // Create new row
                {
                    var cr = new ChannelStatusDto { DeviceAppId = ch.DeviceAppId, ChannelId = ch.ChannelId, Value = ch.Value, Timestamp = ch.Timestamp };
                    last[ch.ChannelId] = cr;
                    var p = ChannelContext.CreateChannelHistoryCacheEntry(ch);
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
                Logger.Trace($"Cached new history for device: {receivedTelem.DeviceAppId}");
            }
        }
    }
}
