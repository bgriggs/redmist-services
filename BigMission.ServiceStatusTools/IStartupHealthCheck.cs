using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace BigMission.ServiceStatusTools;

public interface IStartupHealthCheck : IHealthCheck
{
    string ServiceState { get; set; }
    ServiceTracking ServiceTracking { get; }

    Task<bool> CheckDependencies();
    Task SetStarted();
    Task Start();
}