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

namespace BigMission.ServiceHub
{
    class Program
    {
        static async Task Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);
            builder.Logging.ClearProviders();
            builder.Logging.AddNLogWeb();

            string redisConn = $"{builder.Configuration["REDIS_SVC"]},password={builder.Configuration["REDIS_PW"]}";

            builder.Services.AddControllers();
            builder.Services.AddSingleton<IConnectionMultiplexer>(ConnectionMultiplexer.Connect(redisConn, c => { c.AbortOnConnectFail = false; c.ConnectRetry = 10; c.ConnectTimeout = 10; }));
            builder.Services.AddDbContextFactory<RedMist>(op => op.UseSqlServer(builder.Configuration["DB_CONN"]));
            builder.Services.AddTransient<IDateTimeHelper, DateTimeHelper>();
            builder.Services.AddSingleton<StartupHealthCheck>();
            builder.Services.AddEndpointsApiExplorer();
            builder.Services.AddSingleton<DataClearinghouse>();
            builder.Services.AddSwaggerGen();
            builder.Services.AddHealthChecks()
                .AddCheck<StartupHealthCheck>("Startup", tags: new[] { "startup" })
                .AddSqlServer(builder.Configuration["DB_CONN"], tags: new[] { "db", "sql", "sqlserver" })
                .AddRedis(redisConn, tags: new[] { "cache", "redis" })
                .AddProcessAllocatedMemoryHealthCheck(maximumMegabytesAllocated: 1024, name: "Process Allocated Memory", tags: new[] { "memory" });
           
            builder.Services.AddSignalR().AddStackExchangeRedis(redisConn, options => {
                options.Configuration.ChannelPrefix = "svcsr";
            });

            builder.Services.AddAuthentication(
                options => options.DefaultScheme = "ApiKey")
                    .AddScheme<ApiKeyAuthSchemeOptions, ApiKeyAuthHandler>("ApiKey", options => { });
            builder.Services.AddControllers();

            var app = builder.Build();
            var logger = app.Services.GetService<ILoggerFactory>().CreateLogger("Main");
            var assembly = System.Reflection.Assembly.GetExecutingAssembly();
            logger.LogInformation("ServiceHub Starting...");
            logger.LogInformation(assembly.ToString());

            if (app.Environment.IsDevelopment())
            {
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

            app.UseHttpsRedirection();

            app.UseAuthorization();
            app.UseAuthentication();

            app.MapControllers();
            app.MapHub<EdgeDeviceHub>("/edgedevhub");
            await app.RunAsync();
        }
    }
}

//var builder = WebApplication.CreateBuilder(args);

//var basePath = Directory.GetCurrentDirectory();
//var env = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");
//if (env?.ToUpper() == "PRODUCTION")
//{
//    LogManager.Configuration = new XmlLoggingConfiguration($"{basePath}{Path.DirectorySeparatorChar}nlog.Production.config");
//}
//var logger = LogManager.GetCurrentClassLogger();

//logger.Info($"Starting {env}...");

//// Add services to the container.
//builder.Services.AddControllers();
//// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
//builder.Services.AddEndpointsApiExplorer();
//builder.Services.AddSwaggerGen();
//builder.Services.AddSingleton<NLog.ILogger>(logger);

//var redisConn = builder.Configuration["RedisConn"] ?? string.Empty;
//builder.Services.AddSignalR().AddStackExchangeRedis(redisConn, options => {
//    options.Configuration.ChannelPrefix = "svcsr";
//});

//builder.Services.AddAuthentication(
//    options => options.DefaultScheme = "ApiKey")
//        .AddScheme<ApiKeyAuthSchemeOptions, ApiKeyAuthHandler>("ApiKey", options => { });

//builder.Services.AddTransient<IDateTimeHelper, DateTimeHelper>();
//builder.Services.AddSingleton<DataClearinghouse>();
//var app = builder.Build();

//// Configure the HTTP request pipeline.
//if (app.Environment.IsDevelopment())
//{
//    app.UseCors(builder => builder
//       .AllowAnyHeader()
//       .AllowAnyMethod()
//       .AllowAnyOrigin()
//    );
//    app.UseSwagger();
//    app.UseSwaggerUI();
//}

//app.UseHttpsRedirection();

//app.UseAuthorization();
//app.UseAuthentication();

//app.MapControllers();
//app.MapHub<EdgeDeviceHub>("/edgedevhub");

//app.Run();
