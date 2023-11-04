using Microsoft.Extensions.Diagnostics.HealthChecks;
using System.Threading;
using System.Threading.Tasks;

namespace BigMission.ServiceStatusTools
{
    public interface IStartupHealthCheck : IHealthCheck
    {
        string ServiceState { get; set; }
        ServiceTracking ServiceTracking { get; }

        Task<bool> CheckDependencies();
        Task SetStarted();
        Task Start();
    }
}