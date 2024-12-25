using BigMission.Database;
using BigMission.Database.V2;
using BigMission.ServiceStatusTools;
using BigMission.TestHelpers;
using BigMission.UI.Channels.Hubs;
using BigMission.UI.Channels.Services;
using HealthChecks.UI.Client;
using Keycloak.AuthServices.Authentication;
using Keycloak.AuthServices.Authorization;
using Keycloak.AuthServices.Common;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi.Models;
using NLog.Extensions.Logging;
using StackExchange.Redis;

namespace BigMission.UI.Channels;

public class Program
{
    public static async Task Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        builder.Logging.ClearProviders();
        builder.Logging.AddNLog("NLog");

        builder.Services.AddCors(options =>
        {
            options.AddDefaultPolicy(policy => {
                policy.WithOrigins("http://localhost:4200");
                policy.AllowAnyHeader();
            });
        });

        builder.Services.AddKeycloakWebApiAuthentication(builder.Configuration);
        builder.Services.AddAuthorization().AddKeycloakAuthorization(options =>
        {
            options.EnableRolesMapping = RolesClaimTransformationSource.Realm;
            // Note, this should correspond to role configured with KeycloakAuthenticationOptions
            options.RoleClaimType = KeycloakConstants.RoleClaimType;
        });
        builder.Services.AddControllers();
        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddSwaggerGen(c =>
        {
            c.SwaggerDoc("v1", new OpenApiInfo { Title = "Channel Status", Version = "v1" });

            c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme()
            {
                Name = "Authorization",
                Type = SecuritySchemeType.ApiKey,
                Scheme = "Bearer",
                BearerFormat = "JWT",
                In = ParameterLocation.Header,
                Description = "JWT Authorization header using the Bearer scheme."

            });
            c.AddSecurityRequirement(new OpenApiSecurityRequirement
            {
                {
                      new OpenApiSecurityScheme
                      {
                          Reference = new OpenApiReference
                          {
                              Type = ReferenceType.SecurityScheme,
                              Id = "Bearer"
                          }
                      },
                     Array.Empty<string>()
                }
            });
        });

        string sqlConn = builder.Configuration["ConnectionStrings:Default"] ?? throw new ArgumentNullException("SQL Connection");
        builder.Services.AddDbContextFactory<RedMist>(op => op.UseSqlServer(sqlConn));
        builder.Services.AddDbContextFactory<ContextV2>(op => op.UseSqlServer(sqlConn));

        string redisConn = $"{builder.Configuration["REDIS_SVC"]},password={builder.Configuration["REDIS_PW"]}";
        builder.Services.AddSingleton<IConnectionMultiplexer>(ConnectionMultiplexer.Connect(redisConn, c => { c.AbortOnConnectFail = false; c.ConnectRetry = 10; c.ConnectTimeout = 10; }));

        builder.Services.AddSingleton<IDateTimeHelper, DateTimeHelper>();

        builder.Services.AddHealthChecks()
            .AddCheck<StartupHealthCheck>("Startup", tags: ["startup"])
            .AddSqlServer(sqlConn, tags: ["db", "sql", "sqlserver"])
            .AddRedis(redisConn, tags: ["cache", "redis"])
            .AddProcessAllocatedMemoryHealthCheck(maximumMegabytesAllocated: 1024, name: "Process Allocated Memory", tags: ["memory"]);

        builder.Services.AddSignalR().AddStackExchangeRedis(redisConn, options =>
        {
            options.Configuration.ChannelPrefix = RedisChannel.Literal("channel-status-sr");
        });

        builder.Services.AddTransient<IDateTimeHelper, DateTimeHelper>();
        builder.Services.AddSingleton<StartupHealthCheck>();
        builder.Services.AddSingleton<ServiceTracking>();

        builder.Services.AddHostedService<ChannelStatusAggregator>();
        builder.Services.AddHostedService<CarConnectionStatusAggregator>();

        var app = builder.Build();

        // Configure the HTTP request pipeline.
        if (app.Environment.IsDevelopment())
        {
            Console.Title = "UI.Channels";
            app.UseDeveloperExceptionPage();
            app.UseSwagger();
            app.UseSwaggerUI();
        }

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
        app.UseCors();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapControllers();
        app.MapHub<StatusHub>("/status-hub");

        //string json = "{\"id\":0,\"version\":0,\"lastUpdated\":\"2024-12-01T21:36:54.778Z\",\"columns\":[{\"id\":0,\"header\":\"OilTemp\",\"order\":1,\"channelName\":\"OilTemp\",\"decimalPlaces\":1,\"widthPx\":100}],\"columnOverrides\":[]}";
        ////var config = JsonConvert.DeserializeObject<Configuration>(json);
        //var config = System.Text.Json.JsonSerializer.Deserialize<Configuration>(json);
        await app.RunAsync();
    }
}
