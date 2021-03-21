using Azure.Messaging.EventHubs.Consumer;
using BigMission.CommandTools;
using BigMission.EntityFrameworkCore;
using BigMission.RaceManagement;
using BigMission.ServiceData;
using BigMission.ServiceStatusTools;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using NLog;
using StackExchange.Redis;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace BigMission.CarRealTimeStatusProcessor
{
    /// <summary>
    /// Processes channel status from a device and updates the latest values into a table.
    /// </summary>
    class Application
    {
        private IConfiguration Config { get; }
        private ILogger Logger { get; }
        private ServiceTracking ServiceTracking { get; }
        private readonly EventHubHelpers ehReader;

        private readonly Dictionary<int, ChannelStatus> last = new Dictionary<int, ChannelStatus>();
        private Timer saveTimer;
        private BigMissionDbContext context;
        private readonly ManualResetEvent serviceBlock = new ManualResetEvent(false);

        private volatile bool useCache;
        private ConnectionMultiplexer cacheMuxer;
        private const int HIST_MAX_LEN = 60;


        public Application(IConfiguration config, ILogger logger, ServiceTracking serviceTracking)
        {
            Config = config;
            Logger = logger;
            ServiceTracking = serviceTracking;
            ehReader = new EventHubHelpers(logger);
        }


        public void Run()
        {
            ServiceTracking.Update(ServiceState.STARTING, string.Empty);

            // Attempt to connect to redis cache
            useCache = !string.IsNullOrEmpty(Config["RedisConn"]);
            Logger.Info($"Cache available={useCache}");
            if (useCache)
            {
                GetCache();

                // Pre-load device channels for web host API and other status consumers
                InitializeDeviceChannelCache();
            }

            // Process changes from stream and cache them here is the service
            var partitionFilter = EventHubHelpers.GetPartitionFilter(Config["PartitionFilter"]);
            Task receiveStatus = ehReader.ReadEventHubPartitionsAsync(Config["KafkaConnectionString"], Config["KafkaDataTopic"], Config["KafkaConsumerGroup"],
                partitionFilter, EventPosition.Latest, ReceivedEventCallback);

            if (!useCache)
            {
                // Process the cached status and update the SQL database
                saveTimer = new Timer(SaveCallback, null, 2000, 300);
            }

            // Start updating service status
            ServiceTracking.Start();
            Logger.Info("Started");
            receiveStatus.Wait();
            serviceBlock.WaitOne();
        }

        private BigMissionDbContext GetDbContext()
        {
            if (context != null)// && context.Database.)
            {
                return context;
            }
            else
            {
                var cf = new BigMissionDbContextFactory();
                context = cf.CreateDbContext(new[] { Config["ConnectionString"] });
                return context;
            }
        }

        private void ReceivedEventCallback(PartitionEvent receivedEvent)
        {
            try
            {
                var sw = Stopwatch.StartNew();
                var json = Encoding.UTF8.GetString(receivedEvent.Data.Body.ToArray());
                var chDataSet = JsonConvert.DeserializeObject<ChannelDataSet>(json);

                if (chDataSet.Data == null)
                {
                    chDataSet.Data = new ChannelStatus[] { };
                }

                Logger.Trace($"Received log: {chDataSet.DeviceAppId} Count={chDataSet.Data.Length}");
                if (!chDataSet.Data.Any()) { return; }

                if (useCache)
                {
                    var kvps = new List<KeyValuePair<RedisKey, RedisValue>>();
                    foreach (var ch in chDataSet.Data)
                    {
                        var cs = new CacheModels.ChannelStatus { Value = ch.Value, Timestamp = ch.Timestamp, DeviceId = ch.DeviceAppId };
                        var v = JsonConvert.SerializeObject(cs);
                        var kvp = new KeyValuePair<RedisKey, RedisValue>(string.Format(CacheModels.Consts.CHANNEL_KEY, ch.ChannelId), v);
                        kvps.Add(kvp);
                    }
                    var db = GetCache();
                    if (db != null)
                    {
                        db.StringSet(kvps.ToArray(), flags: CommandFlags.FireAndForget);
                        Logger.Trace($"Cached new status for device: {chDataSet.DeviceAppId}");
                    }
                    else
                    {
                        Logger.Warn("Cache was not available, failed to update status.");
                    }
                }

                // Update in-memory status locally
                var history = new List<KeyValuePair<RedisKey, RedisValue>>();
                lock (last)
                {
                    foreach (var ch in chDataSet.Data)
                    {
                        if (ch.DeviceAppId == 0)
                        {
                            ch.DeviceAppId = chDataSet.DeviceAppId;
                        }

                        // Append changed value to the moving channel history list
                        if (last.TryGetValue(ch.ChannelId, out ChannelStatus row))
                        {
                            if (row.Value != ch.Value)
                            {
                                row.Value = ch.Value;
                                var p = CreateCacheEntry(ch);
                                history.Add(p);
                            }

                            // Keep timestamp current when we get an update
                            row.Timestamp = ch.Timestamp;
                        }
                        else // Create new row
                        {
                            var cr = new ChannelStatus { DeviceAppId = ch.DeviceAppId, ChannelId = ch.ChannelId, Value = ch.Value, Timestamp = ch.Timestamp };
                            last[ch.ChannelId] = cr;
                            var p = CreateCacheEntry(ch);
                            history.Add(p);
                        }
                    }
                }

                if (useCache && history.Any())
                {
                    var db = GetCache();
                    if (db != null)
                    {
                        foreach (var h in history)
                        {
                            // Use the head of the list as the newest value
                            var len = db.ListLeftPush(h.Key, h.Value);
                            if (len > HIST_MAX_LEN)
                            {
                                db.ListTrim(h.Key, 0, HIST_MAX_LEN - 1, flags: CommandFlags.FireAndForget);
                            }
                        }
                        Logger.Trace($"Cached new history for device: {chDataSet.DeviceAppId}");
                    }
                }

                Logger.Trace($"Processed status in {sw.ElapsedMilliseconds}ms");
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Unable to process event from event hub partition");
            }
        }

        /// <summary>
        /// Take care of processing status updates to SQL on another thread.
        /// </summary>
        private void SaveCallback(object obj)
        {
            if (Monitor.TryEnter(saveTimer))
            {
                try
                {
                    // Get a copy of the current status as not to block
                    ChannelStatus[] status;
                    lock (last)
                    {
                        status = last.Select(l => l.Value.Clone()).ToArray();
                    }

                    // Commit changes to DB
                    UpdateChanges(status);
                }
                catch (Exception ex)
                {
                    Logger.Error(ex, "Unable to commit status");
                }
                finally
                {
                    Monitor.Exit(saveTimer);
                }
            }
        }

        /// <summary>
        /// Save changes to SQL server.
        /// </summary>
        /// <param name="rows"></param>
        public void UpdateChanges(ChannelStatus[] rows)
        {
            var db = GetDbContext();
            foreach (var updated in rows)
            {
                var r = db.ChannelStatus.SingleOrDefault(c => c.DeviceAppId == updated.DeviceAppId && c.ChannelId == updated.ChannelId);
                if (r != null)
                {
                    r.Value = updated.Value;
                    r.Timestamp = updated.Timestamp;
                }
                else
                {
                    db.ChannelStatus.Add(updated);
                }
            }

            var sw = Stopwatch.StartNew();
            db.SaveChanges();
            Logger.Trace($"DB Commit in {sw.ElapsedMilliseconds}ms");
        }

        private IDatabase GetCache()
        {
            if (cacheMuxer == null || !cacheMuxer.IsConnected)
            {
                cacheMuxer = ConnectionMultiplexer.Connect(Config["RedisConn"]);
            }
            return cacheMuxer.GetDatabase();
        }

        private static KeyValuePair<RedisKey, RedisValue> CreateCacheEntry(ChannelStatus ch)
        {
            var st = new CacheModels.ChannelStatus { Value = ch.Value, Timestamp = ch.Timestamp, DeviceId = ch.DeviceAppId };
            var v = JsonConvert.SerializeObject(st);
            var p = new KeyValuePair<RedisKey, RedisValue>(string.Format(CacheModels.Consts.CHANNEL_HIST_KEY, ch.ChannelId), v);
            return p;
        }

        /// <summary>
        /// Loads channels by assigned device and caches them.
        /// </summary>
        private void InitializeDeviceChannelCache()
        {
            var db = GetDbContext();
            var channelMappings = db.ChannelMappings.ToArray().GroupBy(g => g.DeviceAppId);
            Logger.Info($"Initialize device channel cache with {channelMappings.Count()} devices...");
            var map = new List<KeyValuePair<RedisKey, RedisValue>>();
            foreach (var dg in channelMappings)
            {
                var channels = dg.Select(c => c.Id).ToArray();
                var chstr = JsonConvert.SerializeObject(channels);
                map.Add(new KeyValuePair<RedisKey, RedisValue>(string.Format(CacheModels.Consts.DEVICE_CHANNELS, dg.Key), chstr));
            }
            var cache = GetCache();
            cache.StringSet(map.ToArray(), flags: CommandFlags.FireAndForget);
            Logger.Info($"Device cache loaded.");
        }
    }
}
