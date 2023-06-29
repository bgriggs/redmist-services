using BigMission.Cache.Models.Flags;
using BigMission.Cache.Models.FuelRange;
using BigMission.Database;
using BigMission.FuelStatistics.FuelRange;
using BigMission.ServiceStatusTools;
using BigMission.TestHelpers;
using HealthChecks.UI.Client;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NLog;
using NLog.Web;
using StackExchange.Redis;
using System;
using System.Threading.Tasks;

namespace BigMission.FuelStatistics
{
    class Program
    {
        static async Task Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);
            builder.Logging.ClearProviders();
            builder.Logging.AddNLogWeb();

            string redisConn = $"{builder.Configuration["REDIS_SVC"]},password={builder.Configuration["REDIS_PW"]}";

            builder.Services.AddSingleton<IConnectionMultiplexer>(ConnectionMultiplexer.Connect(redisConn, c => { c.AbortOnConnectFail = false; c.ConnectRetry = 10; c.ConnectTimeout = 10; }));
            builder.Services.AddDbContextFactory<RedMist>(op => op.UseSqlServer(builder.Configuration["DB_CONN"]));
            builder.Services.AddTransient<IDateTimeHelper, DateTimeHelper>();
            builder.Services.AddSingleton<StartupHealthCheck>();
            builder.Services.AddSingleton<IFuelRangeContext, FuelRangeContext>();
            builder.Services.AddSingleton<IDataContext, DataContext>();
            builder.Services.AddSingleton<IFlagContext, FlagContext>();
            builder.Services.AddSingleton<ILapConsumer, EventService>();
            builder.Services.AddSingleton<ICarTelemetryConsumer>(s =>
            {
                var svs = s.GetServices<ILapConsumer>();
                foreach (var lc in svs)
                {
                    var es = lc as EventService;
                    if (es != null)
                    {
                        return es;
                    }
                }
                throw new InvalidOperationException("Missing dependency");
            });

            builder.Services.AddSingleton<IStintOverrideConsumer>(s =>
            {
                var svs = s.GetServices<ILapConsumer>();
                foreach (var lc in svs)
                {
                    var es = lc as EventService;
                    if (es != null)
                    {
                        return es;
                    }
                }
                throw new InvalidOperationException("Missing dependency");
            });

            // Services
            builder.Services.AddHostedService(s =>
            {
                var svs = s.GetServices<ILapConsumer>();
                foreach (var lc in svs)
                {
                    var es = lc as EventService;
                    if (es != null)
                    {
                        return es;
                    }
                }
                throw new InvalidOperationException("Missing dependency");
            });
            builder.Services.AddHostedService<LapProcessorService>();
            builder.Services.AddHostedService<StintOverrideService>();
            builder.Services.AddHostedService<CarTelemetryService>();
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

            await app.RunAsync();
        }
    }
}
