using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NLog.Extensions.Logging;

namespace BigMission.Streaming.NginxClient;

internal class Program
{
    static async Task Main(string[] args)
    {
        Console.Title = "Nginx Client";
        var builder = Host.CreateApplicationBuilder(args);
        builder.Logging.ClearProviders();
        builder.Logging.AddNLog("NLog");

        builder.Services.AddHostedService<HubClient>();
        var host = builder.Build();

        await host.RunAsync();
    }
}
