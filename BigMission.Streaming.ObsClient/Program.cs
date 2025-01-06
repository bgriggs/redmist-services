using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NLog.Extensions.Logging;

namespace BigMission.Streaming.ObsClient;

internal class Program
{
    static async Task Main(string[] args)
    {
        var environment = Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT")?.ToUpper();
        if (environment == "DEVELOPMENT")
        {
            Console.Title = "OBS Client";
        }
        var builder = Host.CreateApplicationBuilder(args);
        builder.Services.AddWindowsService(options =>
        {
            options.ServiceName = "Red Mist OBS Client";
        });
        builder.Logging.ClearProviders();
        builder.Logging.AddNLog("NLog");

        builder.Services.AddSingleton<ObsClient>();
        builder.Services.AddSingleton<HubClient>();
        builder.Services.AddHostedService<ObsService>();
        builder.Services.AddHostedService(s => s.GetRequiredService<HubClient>());
        
        var host = builder.Build();
        await host.RunAsync();
    }
}
