using BigMission.Cache.Models;
using Newtonsoft.Json;
using StackExchange.Redis;
using System;
using System.Diagnostics;
using System.Threading;

namespace BigMission.ServiceStatusTools
{
    public class ServiceTracking : IDisposable
    {
        private readonly Guid serviceId;
        private readonly Cache.Models.ServiceStatus cacheStatus;
        private Timer statusTimer;
        private readonly IConnectionMultiplexer cacheMuxer;
        private TimeSpan lastCpu;
        private DateTime lastResourceUpdateTimestamp;


        public ServiceTracking(Guid id, string name, string redisConn)
        {
            if (name == null || redisConn == null)
            {
                throw new ArgumentNullException();
            }
            serviceId = id;
            cacheStatus = new Cache.Models.ServiceStatus { ServiceId = id, Name = name, State = ServiceState.OFFLINE, Note = "Initializing" };
            cacheMuxer = ConnectionMultiplexer.Connect(redisConn);
            Update(cacheStatus.State, cacheStatus.Note);
        }

        public ServiceTracking(Guid id, IConnectionMultiplexer cache)
        {
            serviceId = id;
            var assembly = System.Reflection.Assembly.GetExecutingAssembly();
            cacheStatus = new Cache.Models.ServiceStatus { ServiceId = id, Name = assembly.GetName().Name, State = ServiceState.OFFLINE, Note = "Initializing" };

            cacheMuxer = cache;
        }

        public void Update(string state, string note)
        {
            cacheStatus.State = state;
            cacheStatus.Note = note;
            cacheStatus.Timestamp = DateTime.UtcNow;
            //cacheStatus.LogLevel = Logger.Factory.GlobalThreshold.Name;
            cacheStatus.CpuUsage = UpdateCpuUsage().ToString("0.0");

            var stStr = JsonConvert.SerializeObject(cacheStatus);
            var cache = cacheMuxer.GetDatabase();
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
                    Update(ServiceState.ONLINE, string.Empty);
                    ScanForUpdates();
                }
                catch (Exception)
                {
                    //Logger.Error(ex, "Error updating service status");
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
        private void ScanForUpdates()
        {
            var cache = cacheMuxer.GetDatabase();
            var timeoutThreshold = DateTime.UtcNow - TimeSpan.FromSeconds(30);
            var serviceStatus = cache.HashGetAll(Consts.SERVICE_STATUS);

            foreach (var ss in serviceStatus)
            {
                var st = JsonConvert.DeserializeObject<Cache.Models.ServiceStatus>(ss.Value);
                var timedOut = st.Timestamp < timeoutThreshold;
                if (timedOut)
                {
                    st.State = ServiceState.OFFLINE;
                    st.Note = "Service failed to respond within 30 seconds";
                    st.LogLevel = string.Empty;
                    var stStr = JsonConvert.SerializeObject(st);
                    cache.HashSet(Consts.SERVICE_STATUS, ss.Name, stStr);
                }
            }

            // Check to see if user is overriding the log level
            var desiredKey = string.Format(Consts.SERVICE_LOG_DESIRED_LEVEL, serviceId);
            var desiredLogLevel = cache.StringGet(desiredKey);
            if (desiredLogLevel.HasValue && !string.IsNullOrEmpty(desiredLogLevel))
            {
                //Logger.Trace($"Found desired log level={desiredLogLevel}");
                //var desiredLevel = LogLevel.FromString(desiredLogLevel);
                //if (Logger.Factory.GlobalThreshold != desiredLevel)
                //{
                //    Logger.Factory.GlobalThreshold = desiredLevel;
                //    Logger.Info($"Applied new log level={desiredLevel}");
                //}
            }
        }

        public void Dispose()
        {
            statusTimer?.Dispose();
            cacheMuxer.Dispose();
        }
    }
}
