using BigMission.Cache.Models;
using BigMission.DeviceApp.Shared;
using BigMission.TestHelpers;
using Newtonsoft.Json;
using NLog.Targets.ServiceHub;
using StackExchange.Redis;

namespace BigMission.ServiceHub
{
    /// <summary>
    /// Facilitates the propigation of data from service hub to microservices.
    /// </summary>
    public class DataClearinghouse
    {
        public NLog.ILogger Logger { get; }
        private IDateTimeHelper DateTime { get; }

        private readonly ConnectionMultiplexer cacheMuxer;
        private readonly int maxLogListLength = 100;
        private readonly TimeSpan logLengthTrim = TimeSpan.FromSeconds(30);
        private readonly Dictionary<Guid, DateTime> logLastTrims = new Dictionary<Guid, DateTime>();


        public DataClearinghouse(IConfiguration config, NLog.ILogger logger, IDateTimeHelper dateTime)
        {
            cacheMuxer = ConnectionMultiplexer.Connect(config["RedisConn"]);
            Logger = logger;
            DateTime = dateTime;
        }


        public async Task PublishHeartbeat(DeviceAppHeartbeat heartbeat)
        {
            var hbjson = JsonConvert.SerializeObject(heartbeat);
            var pub = cacheMuxer.GetSubscriber();
            await pub.PublishAsync(Consts.HEARTBEAT_CH, hbjson);
        }

        public async Task PublishLog(LogMessage message)
        {
            var cacheKey = string.Format(Consts.DEVICEAPP_LOG, message.SourceKey);
            var cache = cacheMuxer.GetDatabase();
            await cache.ListLeftPushAsync(cacheKey, message.Message, flags: CommandFlags.FireAndForget);

            if (maxLogListLength > 0)
            {
                if (logLastTrims.TryGetValue(message.SourceKey, out var lastTrim))
                {
                    if ((DateTime.UtcNow - lastTrim) > logLengthTrim)
                    {
                        await cache.ListTrimAsync(cacheKey, 0, maxLogListLength, flags: CommandFlags.FireAndForget);
                        logLastTrims[message.SourceKey] = DateTime.UtcNow;
                        Logger.Trace($"Trimed logs for: {message.SourceKey}");
                    }
                }
                else
                {
                    logLastTrims[message.SourceKey] = DateTime.UtcNow;
                }
            }
        }
    }
}
