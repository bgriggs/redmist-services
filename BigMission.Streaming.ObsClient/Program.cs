using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NLog.Extensions.Logging;

namespace BigMission.Streaming.ObsClient;

internal class Program
{
    static async Task Main(string[] args)
    {
        Console.Title = "OBS Client";
        Console.WriteLine("Hello, World!");
        var builder = Host.CreateApplicationBuilder(args);
        builder.Logging.ClearProviders();
        builder.Logging.AddNLog("NLog");
        //builder.Services.AddHostedService<Worker>();

        var host = builder.Build();
        await host.RunAsync();
    }
}
