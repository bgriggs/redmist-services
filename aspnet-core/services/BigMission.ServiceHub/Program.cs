using BigMission.ServiceHub.Hubs;
using BigMission.ServiceHub.Security;
using BigMission.TestHelpers;
using NLog;
using NLog.Config;

var builder = WebApplication.CreateBuilder(args);

var basePath = Directory.GetCurrentDirectory();
var env = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");
if (env.ToUpper() == "PRODUCTION")
{
    LogManager.Configuration = new XmlLoggingConfiguration($"{basePath}{Path.DirectorySeparatorChar}nlog.Production.config");
}
var logger = LogManager.GetCurrentClassLogger();

logger.Info($"Starting {env}...");

// Add services to the container.
builder.Services.AddControllers();
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddSingleton<NLog.ILogger>(logger);

var redisConn = builder.Configuration["RedisConn"] ?? string.Empty;
builder.Services.AddSignalR().AddStackExchangeRedis(redisConn, options => {
    options.Configuration.ChannelPrefix = "svcsr";
});

builder.Services.AddAuthentication(
    options => options.DefaultScheme = "ApiKey")
        .AddScheme<ApiKeyAuthSchemeOptions, ApiKeyAuthHandler>("ApiKey", options => { });

builder.Services.AddTransient<IDateTimeHelper, DateTimeHelper>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.UseAuthorization();
app.UseAuthentication();

app.MapControllers();
app.MapHub<EdgeDeviceHub>("/edgedevhub");

app.Run();
