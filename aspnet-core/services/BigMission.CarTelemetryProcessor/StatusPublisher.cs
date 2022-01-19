using BigMission.Cache.Models;
using BigMission.DeviceApp.Shared;
using BigMission.TestHelpers;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using NLog;
using StackExchange.Redis;

namespace BigMission.CarTelemetryProcessor
{
    internal class StatusPublisher : ITelemetryConsumer
    {
        public ILogger Logger { get; }
        public IDateTimeHelper DateTime { get; }

        private readonly ConnectionMultiplexer cacheMuxer;


        public StatusPublisher(ILogger logger, IConfiguration config, IDateTimeHelper dateTime)
        {
            Logger = logger;
            DateTime = dateTime;
            cacheMuxer = ConnectionMultiplexer.Connect(config["RedisConn"]);
        }


        public async Task ProcessTelemetryMessage(ChannelDataSetDto receivedTelem)
        {
            Logger.Trace($"StatusPublisher received log: {receivedTelem.DeviceAppId} Count={receivedTelem.Data.Length}");
            if (!receivedTelem.Data.Any()) { return; }

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
            Logger.Trace($"Cached new status for device: {receivedTelem.DeviceAppId}");
        }
    }
}
