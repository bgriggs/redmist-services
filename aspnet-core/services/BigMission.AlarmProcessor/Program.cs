using BigMission.Database;
using BigMission.ServiceStatusTools;
using BigMission.TestHelpers;
using HealthChecks.UI.Client;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NLog.Web;
using StackExchange.Redis;
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
        static async Task Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);
            builder.Logging.ClearProviders();
            builder.Logging.AddNLogWeb();

            string redisConn = $"{builder.Configuration["REDIS_SVC"]},password={builder.Configuration["REDIS_PW"]}";

            builder.Services.AddSingleton<IConnectionMultiplexer>(ConnectionMultiplexer.Connect(redisConn));
            builder.Services.AddDbContextFactory<RedMist>(op => op.UseSqlServer(builder.Configuration["DB_CONN"]));
            builder.Services.AddTransient<IDateTimeHelper, DateTimeHelper>();
            builder.Services.AddSingleton<StartupHealthCheck>();
            //builder.Services.AddSingleton(serviceStatus);
            builder.Services.AddHostedService<Application>();
            builder.Services.AddHealthChecks()
                .AddCheck<StartupHealthCheck>("Startup", tags: new[] { "startup" })
                .AddSqlServer(builder.Configuration["DB_CONN"], tags: new[] { "db", "sql", "sqlserver" })
                .AddRedis(redisConn, tags: new[] { "cache", "redis" })
                .AddProcessAllocatedMemoryHealthCheck(maximumMegabytesAllocated: 1024, name: "Process Allocated Memory", tags: new[] { "memory" });
            builder.Services.AddControllers();

            var app = builder.Build();
            var logger = app.Services.GetService<ILoggerFactory>().CreateLogger("Main");
            var assembly = System.Reflection.Assembly.GetExecutingAssembly();
            logger.LogInformation("AlarmProcessor Starting...");
            logger.LogInformation(assembly.ToString());

            if (app.Environment.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }

            app.UseCors(builder => builder
               .AllowAnyHeader()
               .AllowAnyMethod()
               .AllowAnyOrigin());
            app.UseRouting();
            app.MapHealthChecks("/healthz/startup", new HealthCheckOptions
            {
                Predicate = _ => true, // Run all checks
                ResponseWriter = UIResponseWriter.WriteHealthCheckUIResponse
            });
            app.MapHealthChecks("/healthz/live", new HealthCheckOptions
            {
                Predicate = _ => false, // Only check that service is not locked up
                ResponseWriter = UIResponseWriter.WriteHealthCheckUIResponse
            });
            app.MapHealthChecks("/healthz/ready", new HealthCheckOptions
            {
                Predicate = _ => true, // Run all checks
                ResponseWriter = UIResponseWriter.WriteHealthCheckUIResponse
            });
            app.MapControllers();
            

            //string redisConn = $"{config["REDIS_MASTER_SERVICE_HOST"]}:{config["REDIS_MASTER_SERVICE_PORT_TCP-REDIS"]},password={config["REDIS_PW"]}";
            //var serviceStatus = new ServiceTracking(new Guid(config["SERVICEID"]), "AlarmProcessor", redisConn, logger);

            //serviceStatus.Start();
            await app.RunAsync();
        }
    }
}
