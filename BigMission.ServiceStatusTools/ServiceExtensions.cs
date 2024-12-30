using HealthChecks.UI.Client;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using NLog;

namespace BigMission.ServiceStatusTools;

public static class ServiceExtensions
{
    /// <summary>
    /// Add Redis as a logging target to the existing configuration.
    /// </summary>
    /// <param name="builder"></param>
    /// <returns></returns>
    public static WebApplicationBuilder AddRedisLogTarget(this WebApplicationBuilder builder)
    {
        var redisParams = (builder.Configuration["REDIS_SVC"] ?? throw new InvalidOperationException("REDIS_SVC is required")).Split(":");
        var redisLogger = new NLog.Targets.Redis.RedisTarget
        {
            Name = "redis",
            Host = redisParams[0],
            Port = redisParams[1],
            Password = builder.Configuration["REDIS_PW"],
            Key = $"servicelog.{builder.Configuration["SERVICEID"]}",
            DataType = NLog.Targets.Redis.RedisDataType.List,
            Layout = "${longdate} ${uppercase:${level}} ${logger} ${message}${exception:format=tostring}"
        };

        LogManager.Configuration.AddTarget("redis", redisLogger);
        LogManager.Configuration.LoggingRules.First(r => r.NameMatches("*")).Targets.Add(redisLogger);
        LogManager.ReconfigExistingLoggers();
        return builder;
    }

    /// <summary>
    /// Add endpoints for startup, liveliness, and ready health checks.
    /// </summary>
    /// <param name="app"></param>
    /// <returns></returns>
    public static WebApplication UseRedMistHealthCheckEndpoints(this WebApplication app)
    {
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
        return app;
    }
}
