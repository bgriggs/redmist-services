using BigMission.Database;
using BigMission.ServiceHub.Hubs;
using BigMission.ServiceHub.Security;
using BigMission.ServiceStatusTools;
using BigMission.TestHelpers;
using HealthChecks.UI.Client;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using NLog.Web;
using StackExchange.Redis;

namespace BigMission.ServiceHub;

class Program
{
    static async Task Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        builder.Logging.ClearProviders();
        builder.Logging.AddNLogWeb();

        string redisConn = $"{builder.Configuration["REDIS_SVC"]},password={builder.Configuration["REDIS_PW"]}";
        string sqlConn = builder.Configuration["ConnectionStrings:Default"] ?? throw new ArgumentNullException("SQL Connection");

        builder.Services.AddControllers();
        builder.Services.AddSingleton<IConnectionMultiplexer>(ConnectionMultiplexer.Connect(redisConn, c => { c.AbortOnConnectFail = false; c.ConnectRetry = 10; c.ConnectTimeout = 10; }));
        builder.Services.AddDbContextFactory<RedMist>(op => op.UseSqlServer(sqlConn));
        builder.Services.AddTransient<IDateTimeHelper, DateTimeHelper>();
        builder.Services.AddSingleton<StartupHealthCheck>();
        builder.Services.AddSingleton<ServiceTracking>();
        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddSingleton<DataClearinghouse>();
        builder.Services.AddSwaggerGen();
        builder.Services.AddHealthChecks()
            .AddCheck<StartupHealthCheck>("Startup", tags: ["startup"])
            .AddSqlServer(sqlConn, tags: ["db", "sql", "sqlserver"])
            .AddRedis(redisConn, tags: ["cache", "redis"])
            .AddProcessAllocatedMemoryHealthCheck(maximumMegabytesAllocated: 1024, name: "Process Allocated Memory", tags: ["memory"]);

        builder.Services.AddSignalR().AddStackExchangeRedis(redisConn, options =>
        {
            options.Configuration.ChannelPrefix = RedisChannel.Literal("svcsr");
        });

        builder.Services.AddAuthentication(
            options => options.DefaultScheme = "ApiKey")
                .AddScheme<ApiKeyAuthSchemeOptions, ApiKeyAuthHandler>("ApiKey", options => { });
        builder.Services.AddControllers();
        builder.Services.AddHostedService(s => s.GetRequiredService<ServiceTracking>());

        var app = builder.Build();
        builder.AddRedisLogTarget();
        var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Main");
        var assembly = System.Reflection.Assembly.GetExecutingAssembly();
        logger.LogInformation("ServiceHub Starting...");
        logger.LogInformation(assembly.ToString());

        if (app.Environment.IsDevelopment())
        {
            Console.Title = "ServiceHub";
            app.UseCors(builder => builder
               .AllowAnyHeader()
               .AllowAnyMethod()
               .AllowAnyOrigin());
            app.UseDeveloperExceptionPage();
            app.UseSwagger();
            app.UseSwaggerUI();
        }

        app.UseRouting();

        app.MapHealthChecks("/healthz/startup", new HealthCheckOptions
        {
            Predicate = _ => true, // Run all checks
            ResponseWriter = UIResponseWriter.WriteHealthCheckUIResponse
        }).AllowAnonymous();
        app.MapHealthChecks("/healthz/live", new HealthCheckOptions
        {
            Predicate = _ => false, // Only check that service is not locked up
            ResponseWriter = UIResponseWriter.WriteHealthCheckUIResponse
        }).AllowAnonymous();
        app.MapHealthChecks("/healthz/ready", new HealthCheckOptions
        {
            Predicate = _ => true, // Run all checks
            ResponseWriter = UIResponseWriter.WriteHealthCheckUIResponse
        }).AllowAnonymous();

        app.UseHttpsRedirection();

        app.UseAuthorization();
        app.UseAuthentication();

        app.MapControllers();
        app.MapHub<EdgeDeviceHub>("/edgedevhub");

        var startup = app.Services.GetRequiredService<StartupHealthCheck>();
        await startup.Start();
        logger.LogInformation("Waiting for dependencies...");
        while (!await startup.CheckDependencies())
        {
            await Task.Delay(TimeSpan.FromSeconds(1));
        }
        await startup.SetStarted();
        logger.LogInformation("Started");
        await app.RunAsync();
    }
}