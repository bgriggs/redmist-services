using BigMission.ServiceHub.Hubs;
using BigMission.ServiceHub.Security;
using BigMission.TestHelpers;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllers();
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

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
