using Microsoft.Extensions.Diagnostics.HealthChecks;
using StackExchange.Redis;
using System.Threading;
using System.Threading.Tasks;

namespace BigMission.ServiceStatusTools;

public class StartupHealthCheck : IStartupHealthCheck
{
    private readonly IConnectionMultiplexer cache;
    private readonly ServiceTracking serviceTracking;

    public string ServiceState { get; set; } = Cache.Models.ServiceState.STARTING;
    public ServiceTracking ServiceTracking { get; private set; }

    public StartupHealthCheck(IConnectionMultiplexer cache,ServiceTracking serviceTracking)
    {
        this.cache = cache;
        this.serviceTracking = serviceTracking;
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
    }

    public async Task Start()
    {
        await serviceTracking.Update(Cache.Models.ServiceState.STARTING, string.Empty);
    }

    public async Task SetStarted()
    {
        ServiceState = Cache.Models.ServiceState.ONLINE;
        await serviceTracking.Update(Cache.Models.ServiceState.ONLINE, string.Empty);
    }
}
