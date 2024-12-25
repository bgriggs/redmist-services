using BigMission.Cache.Models;
using BigMission.TestHelpers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using NLog;
using StackExchange.Redis;
using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace BigMission.ServiceStatusTools
{
    public class ServiceTracking : BackgroundService
    {
        public string ServiceId { get; private set; }
        private readonly ServiceStatus cacheStatus;
        private readonly IConnectionMultiplexer cacheMuxer;
        private TimeSpan lastCpu;
        private DateTime lastResourceUpdateTimestamp;
        private Microsoft.Extensions.Logging.ILogger Logger { get; }
        private IDateTimeHelper DateTime { get; }

        public ServiceTracking(IConfiguration config, IConnectionMultiplexer cache, ILoggerFactory loggerFactory, IDateTimeHelper dateTime)
        {
            ServiceId = config["SERVICEID"];
            if (config["ASPNETCORE_ENVIRONMENT"] != "Development")
            {
                ServiceId = config["HOSTNAME"];
            }
            Logger = loggerFactory.CreateLogger(GetType().Name);
            cacheMuxer = cache;
            DateTime = dateTime;

            cacheStatus = new ServiceStatus { ServiceId = ServiceId, Name = AppDomain.CurrentDomain.FriendlyName, State = ServiceState.OFFLINE, Note = "Initializing" };
        }

        public async Task Update(string state, string note)
        {
            cacheStatus.State = state;
            cacheStatus.Note = note;
            cacheStatus.Timestamp = DateTime.UtcNow;
            cacheStatus.LogLevel = LogManager.GlobalThreshold.ToString();
            cacheStatus.CpuUsage = UpdateCpuUsage().ToString("0.0");

            var stStr = JsonConvert.SerializeObject(cacheStatus);
            var cache = cacheMuxer.GetDatabase();
            await cache.HashSetAsync(Consts.SERVICE_STATUS, new RedisValue(cacheStatus.ServiceId.ToString()), stStr);
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

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await Update(ServiceState.ONLINE, string.Empty);
                    await ScanForUpdates();
                }
                catch (Exception ex)
                {
                    Logger.LogError(ex, "Error checking service status");
                }

                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }

        /// <summary>
        /// Look for other service timeouts.
        /// </summary>
        /// <param name="db"></param>
        private async Task ScanForUpdates()
        {
            var cache = cacheMuxer.GetDatabase();
            var timeoutThreshold = DateTime.UtcNow - TimeSpan.FromSeconds(30);
            var serviceStatus = await cache.HashGetAllAsync(Consts.SERVICE_STATUS);

            foreach (var ss in serviceStatus)
            {
                var st = JsonConvert.DeserializeObject<ServiceStatus>(ss.Value);
                var timedOut = st.Timestamp < timeoutThreshold;
                if (timedOut)
                {
                    st.State = ServiceState.OFFLINE;
                    st.Note = "Service failed to respond within 30 seconds";
                    st.LogLevel = string.Empty;
                    var stStr = JsonConvert.SerializeObject(st);
                    await cache.HashSetAsync(Consts.SERVICE_STATUS, ss.Name, stStr);
                }
            }

            // Check to see if user is overriding the log level
            var desiredKey = string.Format(Consts.SERVICE_LOG_DESIRED_LEVEL, ServiceId);
            var desiredLogLevel = await cache.StringGetAsync(desiredKey);
            if (desiredLogLevel.HasValue && !string.IsNullOrEmpty(desiredLogLevel))
            {
                Logger.LogTrace($"Found desired log level={desiredLogLevel}");
                var desiredLevel = NLog.LogLevel.FromString(desiredLogLevel);
                if (LogManager.GlobalThreshold != desiredLevel)
                {
                    LogManager.GlobalThreshold = desiredLevel;
                    LogManager.ReconfigExistingLoggers();
                    Logger.LogInformation($"Applied new log level={desiredLevel}");
                }
            }
        }

    }
}
