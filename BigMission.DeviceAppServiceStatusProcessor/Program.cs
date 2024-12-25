using BigMission.CommandTools;
using BigMission.Database;
using BigMission.ServiceStatusTools;
using BigMission.TestHelpers;
using Microsoft.EntityFrameworkCore;
using NLog.Web;
using StackExchange.Redis;
using Microsoft.Extensions.Caching.Distributed;

namespace BigMission.DeviceAppServiceStatusProcessor;

class Program
{
    async static Task Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        builder.Logging.ClearProviders();
        builder.Logging.AddNLogWeb();

        string redisConn = $"{builder.Configuration["REDIS_SVC"]},password={builder.Configuration["REDIS_PW"]}";
        string sqlConn = builder.Configuration["ConnectionStrings:Default"] ?? throw new ArgumentNullException("SQL Connection");

        builder.Services.AddSingleton<IConnectionMultiplexer>(ConnectionMultiplexer.Connect(redisConn, c => { c.AbortOnConnectFail = false; c.ConnectRetry = 10; c.ConnectTimeout = 10; }));
        builder.Services.AddDbContextFactory<RedMist>(op => op.UseSqlServer(sqlConn));
        builder.Services.AddTransient<IDateTimeHelper, DateTimeHelper>();
        builder.Services.AddSingleton<StartupHealthCheck>();
        builder.Services.AddSingleton<ServiceTracking>();
        builder.Services.AddSingleton<IAppCommandsFactory, AppCommandsFactory>();
        builder.Services.AddHealthChecks()
            .AddCheck<StartupHealthCheck>("Startup", tags: ["startup"])
            .AddSqlServer(sqlConn, tags: ["db", "sql", "sqlserver"])
            .AddRedis(redisConn, tags: ["cache", "redis"])
            .AddProcessAllocatedMemoryHealthCheck(maximumMegabytesAllocated: 1024, name: "Process Allocated Memory", tags: ["memory"]);
        builder.Services.AddControllers();
        builder.Services.AddHostedService<Application>();
        builder.Services.AddHostedService(s => s.GetRequiredService<ServiceTracking>());
        builder.Services.AddStackExchangeRedisCache(options =>
        {
            options.ConfigurationOptions = ConfigurationOptions.Parse(redisConn);
        });
#pragma warning disable EXTEXP0018 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.
        builder.Services.AddHybridCache();
#pragma warning restore EXTEXP0018 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.

        var app = builder.Build();
        builder.AddRedisLogTarget();
        var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Main");
        var assembly = System.Reflection.Assembly.GetExecutingAssembly();
        logger.LogInformation("DeviceAppServiceStatusProcessor Starting...");
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
