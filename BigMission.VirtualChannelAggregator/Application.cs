using BigMission.Cache.Models;
using BigMission.CommandTools;
using BigMission.CommandTools.Models;
using BigMission.Database;
using BigMission.Database.Models;
using BigMission.DeviceApp.Shared;
using BigMission.ServiceStatusTools;
using BigMission.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using StackExchange.Redis;
using System.Diagnostics;

namespace BigMission.VirtualChannelAggregator;

/// <summary>
/// Manages the sending of virtual channels to CAN device Apps.  This reads the Virtual channel
/// configuration and initializes from the Channel DB status.  Then it receives channel data 
/// updates from the cardata channel.  The services originating the Virtual channel data are 
/// responsible for publishing that data to the cardata channel.  The StatusProcessor will
/// make sure that data get pushed to the DB as well.
/// </summary>
class Application : BackgroundService
{
    private IConfiguration Config { get; }
    private ILogger Logger { get; }
    public IDateTimeHelper DateTime { get; }

    private readonly Dictionary<int, Tuple<AppCommands, DeviceAppConfig, IEnumerable<int>>> deviceCommandClients = [];
    private readonly IConnectionMultiplexer cacheMuxer;
    private readonly ILoggerFactory loggerFactory;
    private readonly StartupHealthCheck startup;
    private readonly IDbContextFactory<RedMist> dbFactory;

    public Application(IConfiguration config, IConnectionMultiplexer cacheMuxer, ILoggerFactory loggerFactory, StartupHealthCheck startup,
        IDateTimeHelper dateTimeHelper, IDbContextFactory<RedMist> dbFactory)
    {
        Config = config;
        Logger = loggerFactory.CreateLogger(GetType().Name);
        DateTime = dateTimeHelper;
        this.dbFactory = dbFactory;
        this.cacheMuxer = cacheMuxer;
        this.loggerFactory = loggerFactory;
        this.startup = startup;
    }


    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        Logger.LogInformation("Waiting for dependencies...");
        while (!stoppingToken.IsCancellationRequested)
        {
            if (await startup.CheckDependencies())
                break;
            await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
        }
        await startup.Start();

        await InitDeviceClients();

        var sub = cacheMuxer.GetSubscriber();

        // Watch for changes in device app configuration such as channels
        await sub.SubscribeAsync(RedisChannel.Literal(Consts.CAR_CONFIG_CHANGED_SUB), async (channel, message) =>
        {
            Logger.LogInformation("Car device app configuration notification received");
            await InitDeviceClients();
        });

        // Process changes from stream and cache them here in the service
        await sub.SubscribeAsync(RedisChannel.Literal(Consts.CAR_TELEM_SUB), async (channel, message) =>
        {
            await HandleTelemetry(message);
        });

        await startup.SetStarted();

        // Start loop for sending full updates
        while (!stoppingToken.IsCancellationRequested)
        {
            var sw = Stopwatch.StartNew();
            await SendFullUpdate();
            Logger.LogDebug($"Sent full update in {sw.ElapsedMilliseconds}ms");
            await Task.Delay(int.Parse(Config["FULLUPDATEFREQUENCYMS"] ?? throw new InvalidOperationException("FULLUPDATEFREQUENCYMS is required")), stoppingToken);
        }
    }

    private async Task HandleTelemetry(RedisValue value)
    {
        if (value.IsNullOrEmpty)
        {
            Logger.LogWarning("Received empty telemetry message");
            return;
        }
        var telemetryData = JsonConvert.DeserializeObject<ChannelDataSetDto>(value!);
        if (telemetryData != null)
        {
            Logger.LogDebug($"Received status from: '{telemetryData.DeviceAppId}'");

            // Only process virtual channels
            if (telemetryData.IsVirtual)
            {
                await SendChannelStatus(telemetryData.Data);
            }
        }
    }

    private async Task InitDeviceClients()
    {
        Logger.LogDebug("Loading device channel mappings");
        deviceCommandClients.Clear();

        try
        {
            using var context = await dbFactory.CreateDbContextAsync();
            var devices = await context.DeviceAppConfigs.Where(d => !d.IsDeleted).ToListAsync();
            var deviceIds = devices.Select(d => d.Id);
            var channels = await context.ChannelMappings.Where(c => c.IsVirtual).ToListAsync();

            var serviceId = new Guid(Config["SERVICEID"] ?? throw new InvalidOperationException("FULLUPDATEFREQUENCYMS is required"));
            foreach (var d in devices)
            {
                var devVirtChs = channels.Where(c => c.DeviceAppId == d.Id).Select(c => c.Id);
                var t = new Tuple<AppCommands, DeviceAppConfig, IEnumerable<int>>(
                    new AppCommands(serviceId, Config["APIKEY"], Config["AESKEY"], Config["APIURL"], loggerFactory), d, devVirtChs);
                deviceCommandClients[d.Id] = t;
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Could not initialize car app devices.");
        }
    }

    /// <summary>
    /// On a regular frequency send a full status update of all virtual channels to each device.
    /// </summary>
    private async Task SendFullUpdate()
    {
        try
        {
            Logger.LogInformation("Sending full status update");

            var deviceIds = deviceCommandClients.Keys.ToArray();
            var channelIds = deviceCommandClients.Values.SelectMany(v => v.Item3).ToArray();

            // Load current status
            var cache = cacheMuxer.GetDatabase();
            var rks = channelIds.Select(i => new RedisKey(string.Format(Consts.CHANNEL_KEY, i))).ToArray();
            var channelStatusStrs = await cache.StringGetAsync(rks);
            var channelStatus = ConvertToDeviceChStatus(channelIds, channelStatusStrs);
            await SendChannelStatus([.. channelStatus]);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error sending full updates to devices.");
        }
    }

    private List<ChannelStatusDto> ConvertToDeviceChStatus(int[] channelIds, RedisValue[] values)
    {
        var css = new List<ChannelStatusDto>();
        for (int i = 0; i < channelIds.Length; i++)
        {
            var chstr = values[i];
            if (!chstr.IsNullOrEmpty)
            {
                var chmod = JsonConvert.DeserializeObject<ChannelStatusDto>(chstr!);
                if (chmod == null)
                {
                    Logger.LogWarning($"Could not deserialize channel status for channel {channelIds[i]}");
                    continue;
                }
                css.Add(chmod);
            }
        }

        return css;
    }

    private async Task SendChannelStatus(ChannelStatusDto[] status)
    {
        var deviceStatus = status.GroupBy(s => s.DeviceAppId);
        var tasks = deviceStatus.Select(async ds =>
        {
            try
            {
                var hasDevice = deviceCommandClients.TryGetValue(ds.Key, out Tuple<AppCommands, DeviceAppConfig, IEnumerable<int>>? client);
                if (hasDevice)
                {
                    Logger.LogTrace($"Sending virtual status to device {ds.Key}");
                    var dataSet = new ChannelDataSetDto { DeviceAppId = ds.Key, IsVirtual = true, Timestamp = DateTime.UtcNow, Data = [.. ds] };
                    if (client == null)
                    {
                        Logger.LogWarning($"No client for device {ds.Key}");
                        return;
                    }
                    var cmd = new Command
                    {
                        DestinationId = client.Item2.DeviceAppKey.ToString(),
                        CommandType = CommandTypes.SEND_CAN,
                        Timestamp = DateTime.UtcNow
                    };
                    AppCommands.EncodeCommandData(dataSet, cmd);

                    await client.Item1.SendCommandAsync(cmd, new Guid(cmd.DestinationId));
                }
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Unable to send status");
            }
        });

        await Task.WhenAll(tasks);
    }
}
