﻿using BigMission.Cache.Models.Flags;
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
            builder.Services.AddDbContextFactory<RedMist>(op => op.UseSqlServer(builder.Configuration["ConnectionStrings:Default"]));
            builder.Services.AddTransient<IDateTimeHelper, DateTimeHelper>();
            builder.Services.AddSingleton<IStartupHealthCheck, StartupHealthCheck>();
            builder.Services.AddSingleton<ServiceTracking>();
            builder.Services.AddSingleton<IFuelRangeContext, FuelRangeContext>();
            builder.Services.AddSingleton<IDataContext, DataContext>();
            builder.Services.AddSingleton<IFlagContext, FlagContext>();
            builder.Services.AddSingleton<ILapConsumer, EventService>();
            builder.Services.AddSingleton<ICarTelemetryConsumer>(s =>
            {
                var svs = s.GetServices<ILapConsumer>();
                foreach (var lc in svs)
                {
                    if (lc is EventService es)
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
                    if (lc is EventService es)
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
                    if (lc is EventService es)
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
                //.AddCheck<StartupHealthCheck>("Startup", tags: new[] { "startup" })
                .AddSqlServer(builder.Configuration["ConnectionStrings:Default"], tags: new[] { "db", "sql", "sqlserver" })
                .AddRedis(redisConn, tags: new[] { "cache", "redis" })
                .AddProcessAllocatedMemoryHealthCheck(maximumMegabytesAllocated: 1024, name: "Process Allocated Memory", tags: new[] { "memory" });
            builder.Services.AddControllers();
            builder.Services.AddHostedService(s => s.GetRequiredService<ServiceTracking>());

            var app = builder.Build();
            builder.AddRedisLogTarget();
            var logger = app.Services.GetService<ILoggerFactory>().CreateLogger("Main");
            var assembly = System.Reflection.Assembly.GetExecutingAssembly();
            logger.LogInformation("FuelStatistics Starting...");
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

            app.UseRedMistHealthCheckEndpoints();
            app.MapControllers();

            await app.RunAsync();
        }
    }
}
