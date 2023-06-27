using BigMission.ServiceStatusTools;
using BigMission.TestHelpers;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NLog.Web;
using NLog;
using NLog.Config;
using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Collections;

namespace BigMission.AlarmProcessor
{
    /// <summary>
    /// This service monitors channel data for values that will trigger an alarm.  When the 
    /// user conditions are met, the trigger actions will be executed.
    /// </summary>
    class Program
    {
        static async Task Main(string[] args)
        {
            var host = Host.CreateDefaultBuilder(args)
                .ConfigureLogging(c => c.ClearProviders())
                .UseNLog()
                .ConfigureWebHostDefaults(webBuilder =>
                {
                    webBuilder.UseStartup<Startup>();
                }).Build();
            foreach (DictionaryEntry e in System.Environment.GetEnvironmentVariables())
            {
                Console.WriteLine(e.Key + ":" + e.Value);
            }
            //var basePath = Directory.GetCurrentDirectory();
            //var env = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Development";
            //if (env.ToUpper() == "PRODUCTION")
            //{
            //    LogManager.Configuration = new XmlLoggingConfiguration($"{basePath}{Path.DirectorySeparatorChar}nlog.Production.config");
            //}
            var logger = host.Services.GetService<ILoggerFactory>().CreateLogger("Main");
            var assembly = System.Reflection.Assembly.GetExecutingAssembly();
            logger.LogInformation("AlarmProcessor Starting...");
            logger.LogInformation(assembly.ToString());

            //logger.Info($"Starting {env}...");
            //var config = new ConfigurationBuilder()
            //    .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
            //    .AddEnvironmentVariables()
            //    .Build();

            //string redisConn = $"{config["REDIS_MASTER_SERVICE_HOST"]}:{config["REDIS_MASTER_SERVICE_PORT_TCP-REDIS"]},password={config["REDIS_PW"]}";
            //var serviceStatus = new ServiceTracking(new Guid(config["SERVICEID"]), "AlarmProcessor", redisConn, logger);

            //var builder = WebApplication.CreateBuilder(args);
            ////builder.Services.AddSingleton<ILogger>(logger);
            ////builder.Services.AddSingleton<IConfiguration>(config);
            //builder.Services.AddTransient<IDateTimeHelper, DateTimeHelper>();
            //builder.Services.AddSingleton(serviceStatus);
            //builder.Services.AddHostedService<Application>();
            //builder.Services.AddHealthChecks().AddCheck<ServiceHealthCheck>("service");
            //builder.Services.AddControllers();

            //var app = builder.Build();
            //app.UseStaticFiles();
            //app.UseRouting();
            //app.UseEndpoints(endpoints =>
            //{
            //    endpoints.MapHealthChecks("/startup");
            //    endpoints.MapHealthChecks("/liveness");
            //    endpoints.MapHealthChecks("/ready");
            //    endpoints.MapControllers();
            //});


            //serviceStatus.Start();
            await host.RunAsync();
        }
    }
    public class ServiceHealthCheck : IHealthCheck
    {
        //private readonly ServiceTracking serviceTracking;

        public ServiceHealthCheck()
        {
        }

        public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(HealthCheckResult.Healthy());
        }
    }
}
