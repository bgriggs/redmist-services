using BigMission.Shared.SignalR;
using BigMission.Streaming.Shared.Models;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;

namespace BigMission.Streaming.NginxClient;

internal class HubClient : HubClientBase
{
    private ILogger Logger { get; }
    private string nginxConfPath;

    public HubClient(ILoggerFactory loggerFactory, IConfiguration configuration) : base(loggerFactory, configuration)
    {
        Logger = loggerFactory.CreateLogger(GetType().Name);
        nginxConfPath = configuration["NginxConfigFile"] ?? throw new InvalidOperationException("Nginx configuration path is not configured.");
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        Logger.LogInformation("Starting Nginx client.");

        // Check to see if the Nginx configuration file exists and can be read
        try
        {
            await File.ReadAllTextAsync(nginxConfPath, stoppingToken);
        }
        catch (Exception ex)
        {
            Logger.LogCritical(ex, "Failed to read Nginx configuration file.");
            var environment = Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT")?.ToUpper();
            if (environment != "DEVELOPMENT")
            {
                return;
            }
        }

        var hub = StartConnection(stoppingToken);

        hub.On<string>("ReceiveMessage", message =>
        {
            Logger.LogInformation($"Received message: {message}");
        });

        hub.On<NginxStreamPush[]>("SetStreams", async streams =>
        {
            var response = new RequestResponse { RequestId = streams.First().RequestId };
            Logger.LogInformation($"Received SetStreams ID: {response.RequestId}");
            try
            {
                var conf = await File.ReadAllTextAsync(nginxConfPath, stoppingToken);
                var updatedConf = NginxConfiguration.SetStreamDestinations(nginxConfPath, [.. streams]);
                await File.WriteAllTextAsync(nginxConfPath, updatedConf, stoppingToken);
                var exitCode = await RestartNginx(stoppingToken);
                if (exitCode != 0)
                {
                    Logger.LogError($"Failed to restart Nginx. Exit code: {exitCode}");
                    response.Message = "Failed to restart Nginx.";
                }
                else
                {
                    response.Success = true;
                }
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Failed to set streams.");
                response.Message = "Failed to set streams.";
            }
            await hub.SendAsync("ReceiveResponse", response);
        });

        hub.On<string>("GetInfo", async id =>
        {
            var response = new RequestResponse { RequestId = id };
            Logger.LogInformation($"Received GetInfo request: {id}");
            try
            {
                var info = await GetNginxInfo(stoppingToken);
                response.Data = info;
                response.Success = true;
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Failed to get Nginx info.");
                response.Message = "Failed to get Nginx info.";
            }
            await hub.SendAsync("ReceiveResponse", response);
        });
    }

    private async Task<ImmutableArray<NginxStreamPush>> GetStreamDestinations(CancellationToken stoppingToken)
    {
        var conf = await File.ReadAllTextAsync(nginxConfPath, stoppingToken);
        var streams = NginxConfiguration.GetStreams(conf);
        return streams;
    }

    private async Task<NginxInfo> GetNginxInfo(CancellationToken stoppingToken)
    {
        var info = new NginxInfo();
        info.StreamDestinations.AddRange(await GetStreamDestinations(stoppingToken));

        info.HostName = Dns.GetHostName();
        var ips = await Dns.GetHostAddressesAsync(info.HostName, stoppingToken);

        info.IP = ips.FirstOrDefault(ips => ips.AddressFamily == AddressFamily.InterNetwork)?.ToString() ?? "?";

        return info;
    }

    private async static Task<int> RestartNginx(CancellationToken stoppingToken)
    {
        var procInfo = new ProcessStartInfo
        {
            FileName = "/usr/bin/sudo",
            Arguments = "/bin/systemctl restart nginx",
            UseShellExecute = false,
            CreateNoWindow = true
        };

        var p = Process.Start(procInfo);
        if (p == null)
        {
            return -1;
        }
        await p.WaitForExitAsync(stoppingToken);
        return p.ExitCode;
    }
}
