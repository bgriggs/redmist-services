using BigMission.ServiceStatusTools;
using BigMission.TestHelpers;
using NLog.Web;
using StackExchange.Redis;

namespace BigMission.KeypadServices;

class Program
{
    async static Task Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        builder.Logging.ClearProviders();
        builder.Logging.AddNLogWeb();

        string redisConn = $"{builder.Configuration["REDIS_SVC"]},password={builder.Configuration["REDIS_PW"]}";

        builder.Services.AddSingleton<IConnectionMultiplexer>(ConnectionMultiplexer.Connect(redisConn, c => { c.AbortOnConnectFail = false; c.ConnectRetry = 10; c.ConnectTimeout = 10; }));
        builder.Services.AddTransient<IDateTimeHelper, DateTimeHelper>();
        builder.Services.AddSingleton<StartupHealthCheck>();
        builder.Services.AddSingleton<ServiceTracking>();
        builder.Services.AddHealthChecks()
            .AddCheck<StartupHealthCheck>("Startup", tags: ["startup"])
            .AddRedis(redisConn, tags: ["cache", "redis"])
            .AddProcessAllocatedMemoryHealthCheck(maximumMegabytesAllocated: 1024, name: "Process Allocated Memory", tags: ["memory"]);
        builder.Services.AddControllers();
        builder.Services.AddHostedService<Application>();
        builder.Services.AddHostedService(s => s.GetRequiredService<ServiceTracking>());

        var app = builder.Build();
        builder.AddRedisLogTarget();
        var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Main");
        var assembly = System.Reflection.Assembly.GetExecutingAssembly();
        logger.LogInformation("KeypadServices Starting...");
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
