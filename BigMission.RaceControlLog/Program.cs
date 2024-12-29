using BigMission.Database;
using BigMission.RaceControlLog.Configuration;
using BigMission.RaceControlLog.EventStatus;
using BigMission.RaceControlLog.LogConnections;
using BigMission.RaceControlLog.LogProcessing;
using BigMission.ServiceStatusTools;
using BigMission.TestHelpers;
using Microsoft.EntityFrameworkCore;
using NLog.Web;
using StackExchange.Redis;

namespace BigMission.RaceControlLog;

internal class Program
{
    static async Task Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        builder.Logging.ClearProviders();
        builder.Logging.AddNLogWeb();

        string redisConn = $"{builder.Configuration["REDIS_SVC"]},password={builder.Configuration["REDIS_PW"]}";
        string sqlConn = builder.Configuration["ConnectionStrings:Default"] ?? throw new InvalidOperationException("SQL Connection string is not valid");

        builder.Services.AddSingleton<IConnectionMultiplexer>(ConnectionMultiplexer.Connect(redisConn, c => { c.AbortOnConnectFail = false; c.ConnectRetry = 10; c.ConnectTimeout = 10; }));
        builder.Services.AddDbContextFactory<RedMist>(op => op.UseSqlServer(sqlConn));
        builder.Services.AddTransient<IDateTimeHelper, DateTimeHelper>();
        builder.Services.AddSingleton<StartupHealthCheck>();
        builder.Services.AddSingleton<ServiceTracking>();
        builder.Services.AddSingleton<IControlLogConnection, GoogleSheetsControlLog>();
        builder.Services.AddSingleton<ILogProcessor, CacheControlLog>();
        builder.Services.AddSingleton<ILogProcessor, SmsNotification>();
        builder.Services.AddSingleton<ConfigurationContext>();
        builder.Services.AddSingleton<IEventStatus, EventRedisStatus>();
        builder.Services.AddHealthChecks()
            .AddCheck<StartupHealthCheck>("Startup", tags: ["startup"])
            .AddSqlServer(sqlConn, tags: ["db", "sql", "sqlserver"])
            .AddRedis(redisConn, tags: ["cache", "redis"])
            .AddProcessAllocatedMemoryHealthCheck(maximumMegabytesAllocated: 1024, name: "Process Allocated Memory", tags: ["memory"]);
        builder.Services.AddControllers();
        builder.Services.AddHostedService<ConfigurationService>();
        builder.Services.AddHostedService<LogPollService>();
        builder.Services.AddHostedService(s => s.GetRequiredService<ServiceTracking>());

        var app = builder.Build();
        builder.AddRedisLogTarget();
        var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Main");
        var assembly = System.Reflection.Assembly.GetExecutingAssembly();
        logger.LogInformation("RaceControlLog Starting...");
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