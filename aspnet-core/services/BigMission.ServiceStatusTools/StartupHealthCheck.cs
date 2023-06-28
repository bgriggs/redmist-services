using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using StackExchange.Redis;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace BigMission.ServiceStatusTools
{
    public class StartupHealthCheck : IHealthCheck
    {
        private readonly IConnectionMultiplexer cache;

        public string ServiceState { get; set; } = Cache.Models.ServiceState.STARTING;
        public ServiceTracking ServiceTracking { get; private set; }

        public StartupHealthCheck(IConnectionMultiplexer cache, IConfiguration config)
        {
            Guid.TryParse(config["SERVICEID"], out Guid id);
            ServiceTracking = new ServiceTracking(id, cache);
            this.cache = cache;
        }

        public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
        {
            if (ServiceState == Cache.Models.ServiceState.STARTING)
            {
                return Task.FromResult(HealthCheckResult.Unhealthy("Service starting."));
            }

            return Task.FromResult(HealthCheckResult.Healthy("Service started"));
        }

        public Task<bool> CheckDependencies()
        {
            return Task.FromResult(cache.IsConnected);
            //var db = cache.GetDatabase();
            //var result = await db.PingAsync();
            //return result > default(TimeSpan);
        }

        public void Start()
        {
            ServiceTracking.Start();
            ServiceTracking.Update(Cache.Models.ServiceState.STARTING, string.Empty);
        }

        public void SetStarted()
        {
            ServiceState = Cache.Models.ServiceState.ONLINE;
            ServiceTracking.Update(Cache.Models.ServiceState.ONLINE, string.Empty);
        }
    }
}
