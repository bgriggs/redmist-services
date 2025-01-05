using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NLog.Extensions.Logging;

namespace BigMission.Streaming.ObsClient;

internal class Program
{
    static async Task Main(string[] args)
    {
        Console.Title = "OBS Client";
        var builder = Host.CreateApplicationBuilder(args);
        builder.Logging.ClearProviders();
        builder.Logging.AddNLog("NLog");

        builder.Services.AddSingleton<ObsClient>();
        builder.Services.AddSingleton<HubClient>();
        builder.Services.AddHostedService<ObsService>();

        var host = builder.Build();
        await host.RunAsync();
    }
}
