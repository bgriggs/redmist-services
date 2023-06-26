using BigMission.ServiceStatusTools;
using BigMission.TestHelpers;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using NLog;
using NLog.Config;
using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace BigMission.AlarmProcessor
{
    /// <summary>
    /// This service monitors channel data for values that will trigger an alarm.  When the 
    /// user conditions are met, the trigger actions will be executed.
    /// </summary>
    class Program
    {
        private static Logger logger;

        static async Task Main(string[] args)
        {
            try
            {
                while (!Debugger.IsAttached)
                {
                    Console.WriteLine("Waiting...");
                    Task.Delay(1000).Wait();
                }
                var basePath = Directory.GetCurrentDirectory();
                var env = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Development";
                if (env.ToUpper() == "PRODUCTION")
                {
                    LogManager.Configuration = new XmlLoggingConfiguration($"{basePath}{Path.DirectorySeparatorChar}nlog.Production.config");
                }
                logger = LogManager.GetCurrentClassLogger();

                logger.Info($"Starting {env}...");
                var config = new ConfigurationBuilder()
                    .SetBasePath(basePath)
                    .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
                    .AddEnvironmentVariables()
                    .Build();

                var serviceStatus = new ServiceTracking(new Guid(config["ServiceId"]), "AlarmProcessor", config["RedisConn"], logger);

                var builder = WebApplication.CreateBuilder(args);
                builder.Services.AddSingleton<ILogger>(logger);
                builder.Services.AddSingleton<IConfiguration>(config);
                builder.Services.AddTransient<IDateTimeHelper, DateTimeHelper>();
                builder.Services.AddSingleton(serviceStatus);
                builder.Services.AddHostedService<Application>();
                builder.Services.AddHealthChecks().AddCheck<ServiceHealthCheck>("service");
                builder.Services.AddControllers();

                var app = builder.Build();
                app.UseStaticFiles();
                app.UseRouting();
                app.UseEndpoints(endpoints =>
                {
                    endpoints.MapHealthChecks("/startup");
                    endpoints.MapHealthChecks("/liveness");
                    endpoints.MapHealthChecks("/ready");
                    endpoints.MapControllers();
                });

                try
                {
                    serviceStatus.Start();
                    await app.RunAsync();
                }
                catch (OperationCanceledException)
                {
                    // suppress
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex);
            }
            finally
            {
                LogManager.Shutdown();
            }
        }
    }
    public class ServiceHealthCheck : IHealthCheck
    {
        private readonly ServiceTracking serviceTracking;

        public ServiceHealthCheck(ServiceTracking serviceTracking)
        {
            this.serviceTracking = serviceTracking;
        }

        public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(HealthCheckResult.Healthy());
        }
    }
}
