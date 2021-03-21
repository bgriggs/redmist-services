using BigMission.Cache.Models;
using BigMission.ServiceData;
using Newtonsoft.Json;
using NLog;
using StackExchange.Redis;
using System;
using System.Diagnostics;
using System.Threading;

namespace BigMission.ServiceStatusTools
{
    public class ServiceTracking : IDisposable
    {
        //private readonly ServiceStatus status;
        private readonly Cache.Models.ServiceStatus cacheStatus;
        private Timer statusTimer;
        private ILogger Logger { get; }
        private ConnectionMultiplexer cacheMuxer;
        private readonly string redisConn;
        private TimeSpan lastCpu;
        private DateTime lastResourceUpdateTimestamp;


        public ServiceTracking(Guid id, string name, string redisConn, ILogger logger)
        {
            if (name == null || redisConn == null)
            {
                throw new ArgumentNullException();
            }
            cacheStatus = new Cache.Models.ServiceStatus { ServiceId = id, Name = name, State = ServiceState.OFFLINE, Note = "Initializing" };
            this.redisConn = redisConn;
            Logger = logger;
            
            var cache = GetCache();
            Update(cacheStatus.State, cacheStatus.Note, cache);
        }

        public void Update(string state, string note)
        {
            var cache = GetCache();
            Update(state, note, cache);
        }

        private void Update(string state, string note, IDatabase cache)
        {
            cacheStatus.State = state;
            cacheStatus.Note = note;
            cacheStatus.Timestamp = DateTime.UtcNow;
            cacheStatus.LogLevel = Logger.Factory.GlobalThreshold.Name;
            cacheStatus.CpuUsage = UpdateCpuUsage().ToString("0.0");

            var stStr = JsonConvert.SerializeObject(cacheStatus);
            cache.HashSet(Consts.SERVICE_STATUS, new RedisValue(cacheStatus.ServiceId.ToString()), stStr);
        }

        private double UpdateCpuUsage()
        {
            var endTime = DateTime.UtcNow;
            var endCpuUsage = Process.GetCurrentProcess().TotalProcessorTime;
            var cpuUsedMs = (endCpuUsage - lastCpu).TotalMilliseconds;
            var totalMsPassed = (endTime - lastResourceUpdateTimestamp).TotalMilliseconds;
            var cpuUsageTotal = cpuUsedMs / (Environment.ProcessorCount * totalMsPassed);

            lastCpu = endCpuUsage;
            lastResourceUpdateTimestamp = endTime;

            return cpuUsageTotal * 100;
        }

        /// <summary>
        /// Updates service status on a frequency while the service is running.
        /// </summary>
        public void Start()
        {
            if (statusTimer != null)
            {
                throw new InvalidOperationException("Already running.");
            }

            UpdateCpuUsage();
            statusTimer = new Timer(UpdateCallback, null, 100, 5000);
        }

        /// <summary>
        /// Update service status timestamp in the database.
        /// </summary>
        /// <param name="obj"></param>
        private void UpdateCallback(object obj)
        {
            if (Monitor.TryEnter(statusTimer))
            {
                try
                {
                    var cache = GetCache();
                    Update(ServiceState.ONLINE, string.Empty, cache);
                    ScanForUpdates(cache);
                }
                catch (Exception ex)
                {
                    Logger.Error(ex, "Error updating service status");
                }
                finally
                {
                    Monitor.Exit(statusTimer);
                }
            }
        }

        /// <summary>
        /// Look for other service timeouts.
        /// </summary>
        /// <param name="db"></param>
        private void ScanForUpdates(IDatabase cache)
        {
            var timeoutThreshold = DateTime.UtcNow - TimeSpan.FromSeconds(30);
            var serviceStatus = cache.HashGetAll(Consts.SERVICE_STATUS);

            foreach (var ss in serviceStatus)
            {
                var st = JsonConvert.DeserializeObject<Cache.Models.ServiceStatus>(ss.Value);
                var timedout = st.Timestamp < timeoutThreshold;
                if (timedout)
                {
                    st.State = ServiceState.OFFLINE;
                    st.Note = "Service failed to respond within 30 seconds";
                    st.LogLevel = string.Empty;
                    var stStr = JsonConvert.SerializeObject(st);
                    cache.HashSet(Consts.SERVICE_STATUS, ss.Name, stStr);
                }

                if (st.ServiceId == cacheStatus.ServiceId && !string.IsNullOrWhiteSpace(st.DesiredLogLevel))
                {
                    var desiredLevel = LogLevel.FromString(st.DesiredLogLevel);
                    if (Logger.Factory.GlobalThreshold != desiredLevel)
                    {
                        Logger.Factory.GlobalThreshold = desiredLevel;
                    }
                }
            }
        }

        private IDatabase GetCache()
        {
            if (cacheMuxer == null || !cacheMuxer.IsConnected)
            {
                cacheMuxer = ConnectionMultiplexer.Connect(redisConn);
            }
            return cacheMuxer.GetDatabase();
        }

        public void Dispose()
        {
            if (statusTimer != null)
            {
                statusTimer.Dispose();
            }

            cacheMuxer.Dispose();
        }
    }
}
