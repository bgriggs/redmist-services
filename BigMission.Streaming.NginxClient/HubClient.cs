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
    private readonly string nginxConfPath;

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

        hub.On<NginxStreamPush[], bool>("SetStreams", async (streams) =>
        {
            Logger.LogInformation($"Received SetStreams");
            try
            {
                var conf = await File.ReadAllTextAsync(nginxConfPath, stoppingToken);
                var updatedConf = NginxConfiguration.SetStreamDestinations(conf, [.. streams]);
                Logger.LogDebug("Writing new Nginx conf...");
                await File.WriteAllTextAsync(nginxConfPath, updatedConf, stoppingToken);
                Logger.LogDebug("Restarting Nginx...");
                var exitCode = await RestartNginx(stoppingToken);
                if (exitCode != 0)
                {
                    Logger.LogError($"Failed to restart Nginx. Exit code: {exitCode}");
                }
                else
                {
                    Logger.LogInformation("Nginx restarted successfully.");
                    return true;
                }
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Failed to set streams.");
            }
            return false;
        });

        hub.On("GetInfo", async () =>
        {
            Logger.LogInformation($"Received GetInfo request");
            try
            {
                return await GetNginxInfo(stoppingToken);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Failed to get Nginx info.");
            }
            return null;
        });

        hub.On("GetIsActive", async () =>
        {
            Logger.LogInformation($"Received GetIsActive request");
            try
            {
                return await IsNginxActive(stoppingToken);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Failed to get Nginx status.");
            }
            return false;
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
        foreach(var ip in ips)
        {
            Logger.LogTrace($"IP: {ip}");
        }
        info.IP = ips.FirstOrDefault(ips => ips.AddressFamily == AddressFamily.InterNetwork && !ips.ToString().StartsWith("127."))?.ToString() ?? "?";

        return info;
    }

    private async Task<int> RestartNginx(CancellationToken stoppingToken)
    {
        var procInfo = new ProcessStartInfo
        {
            FileName = "/usr/bin/sudo",
            Arguments = "/bin/systemctl restart nginx",
            UseShellExecute = false,
            CreateNoWindow = true
        };

        Logger.LogDebug($"Restarting Nginx: {procInfo.FileName} {procInfo.Arguments}");
        var p = Process.Start(procInfo);
        if (p == null)
        {
            Logger.LogError("Nginx process handle is null");
            return -1;
        }
        Logger.LogDebug($"Waiting for Nginx process to restart...");
        await p.WaitForExitAsync(stoppingToken);
        Logger.LogDebug($"Nginx process restart exited with code {p.ExitCode}");
        return p.ExitCode;
    }

    public async Task<bool> IsNginxActive(CancellationToken stoppingToken)
    {
        var procInfo = new ProcessStartInfo
        {
            FileName = "/usr/bin/sudo",
            Arguments = "/bin/systemctl is-active nginx",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true
        };
        Logger.LogDebug($"Checking Nginx status: {procInfo.FileName} {procInfo.Arguments}");
        var p = Process.Start(procInfo);
        if (p == null)
        {
            Logger.LogError("Nginx process handle is null");
            return false;
        }
        var output = await p.StandardOutput.ReadToEndAsync();
        Logger.LogDebug($"Nginx status: {output}");
        return string.Equals(output.Trim(), "active", StringComparison.OrdinalIgnoreCase);
    }
}
