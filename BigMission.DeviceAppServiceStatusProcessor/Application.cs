using BigMission.Backend.Shared.Models;
using BigMission.Cache.Models;
using BigMission.CommandTools;
using BigMission.CommandTools.Models;
using BigMission.Database;
using BigMission.ServiceStatusTools;
using BigMission.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Newtonsoft.Json;
using StackExchange.Redis;

namespace BigMission.DeviceAppServiceStatusProcessor;

/// <summary>
/// Processes application status from the in car apps. (not channel status)
/// </summary>
class Application : BackgroundService
{
    private ILogger Logger { get; }
    private IDateTimeHelper DateTime { get; }
    private readonly IConnectionMultiplexer cacheMuxer;
    private readonly IDbContextFactory<RedMist> dbFactory;
    private readonly StartupHealthCheck startup;
    private readonly IAppCommandsFactory commandsFactory;
    private readonly HybridCache hybridCache;
    private readonly ServiceTracking serviceTracking;

    public Application(ILoggerFactory loggerFactory, IDateTimeHelper dateTime, IConnectionMultiplexer cache, IDbContextFactory<RedMist> dbFactory,
        StartupHealthCheck startup, IAppCommandsFactory commandsFactory, HybridCache hybridCache, ServiceTracking serviceTracking)
    {
        Logger = loggerFactory.CreateLogger(GetType().Name);
        DateTime = dateTime;
        this.dbFactory = dbFactory;
        this.startup = startup;
        this.commandsFactory = commandsFactory;
        this.hybridCache = hybridCache;
        this.serviceTracking = serviceTracking;
        cacheMuxer = cache;
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

        // Ensure the consumer group exists for device app heartbeat status
        var db = cacheMuxer.GetDatabase();
        if (!(await db.KeyExistsAsync(Backend.Shared.Consts.HEARTBEAT_TELEM)) || (await db.StreamGroupInfoAsync(Backend.Shared.Consts.HEARTBEAT_TELEM)).All(x => x.Name != Backend.Shared.Consts.DEV_APP_PROC_GRP))
        {
            await db.StreamCreateConsumerGroupAsync(Backend.Shared.Consts.HEARTBEAT_TELEM, Backend.Shared.Consts.DEV_APP_PROC_GRP, "0-0", true);
        }

        // Watch for changes in device app configuration such as channels
        var sub = cacheMuxer.GetSubscriber();
        var commands = commandsFactory.CreateAppCommands();
        await sub.SubscribeAsync(RedisChannel.Literal(Consts.CAR_CONFIG_CHANGED_SUB), async (channel, message) =>
        {
            if (int.TryParse(message, out int deviceId))
            {
                Logger.LogInformation("Car device app configuration notification received.  Sending command to restart car device application...");
                using var db = await dbFactory.CreateDbContextAsync(stoppingToken);
                var deviceApp = db.DeviceAppConfigs.FirstOrDefault(d => d.Id == deviceId);
                if (deviceApp != null)
                {
                    var cmd = new Command
                    {
                        CommandType = CommandTypes.RESTART,
                        DestinationId = deviceApp.DeviceAppKey.ToString(),
                        Timestamp = DateTime.UtcNow
                    };
                    await commands.SendCommandAsync(cmd, new Guid(cmd.DestinationId));
                }
                else
                {
                    Logger.LogWarning($"Unable to prompt device app configuration because of missing device ID for {deviceId}");
                }

                // Invalidate cache of car status
                await ClearCarStatusCacheElements();
            }
            else
            {
                Logger.LogWarning($"Unable to prompt device app configuration because of missing device ID: {message}");
            }
        });

        Logger.LogInformation("Started");
        await startup.SetStarted();

        while (!stoppingToken.IsCancellationRequested)
        {
            var result = await db.StreamReadGroupAsync(Backend.Shared.Consts.HEARTBEAT_TELEM, Backend.Shared.Consts.DEV_APP_PROC_GRP, serviceTracking.ServiceId, ">", 1);
            if (result.Length == 0)
            {
                await Task.Delay(100, stoppingToken);
                continue;
            }

            Logger.LogDebug($"Received {result.Length} heartbeat.");
            foreach (var r in result)
            {
                foreach (var nv in r.Values)
                {
                    await HandleHeartbeat(nv.Value, stoppingToken);
                }

                await db.StreamAcknowledgeAsync(Backend.Shared.Consts.HEARTBEAT_TELEM, Backend.Shared.Consts.DEV_APP_PROC_GRP, r.Id);
            }
        }
    }

    private async Task ClearCarStatusCacheElements()
    {
        var cache = cacheMuxer.GetDatabase();
        await cache.KeyDeleteAsync(CarConnectionCacheConst.CAR_STATUS);

        await foreach (var key in GetCacheKeysAsync(string.Format(CarConnectionCacheConst.DEVICE_ASSIGNED_CAR, "*")))
        {
            await hybridCache.RemoveAsync(key);
        }
        await foreach (var key in GetCacheKeysAsync(string.Format(CarConnectionCacheConst.CAR_DEVICES_LOOKUP, "*")))
        {
            await hybridCache.RemoveAsync(key);
        }
    }

    private async Task HandleHeartbeat(RedisValue value, CancellationToken stoppingToken)
    {
        if (value.IsNullOrEmpty)
        {
            Logger.LogInformation($"Received empty heartbeat");
            return;
        }

        var heartbeatData = JsonConvert.DeserializeObject<DeviceApp.Shared.DeviceAppHeartbeat>(value!);
        Logger.LogDebug($"Received HB from: '{heartbeatData?.DeviceAppId}'");

        if (heartbeatData?.DeviceAppId == 0)
        {
            // Get the device ID for the device key
            Logger.LogDebug($"Device ID not found for key {heartbeatData.DeviceKey}. Attempting to resolve.");

            var deviceId = await hybridCache.GetOrCreateAsync($"deviceKeyId-{heartbeatData.DeviceKey}",
                async cancel => await LoadDeviceId(heartbeatData.DeviceKey, cancel),
                cancellationToken: stoppingToken);

            if (deviceId > 0)
            {
                heartbeatData.DeviceAppId = deviceId;
                Logger.LogDebug($"Device ID found for key {heartbeatData.DeviceKey}: {deviceId}");
            }
            else
            {
                Logger.LogDebug($"No app configured with key {heartbeatData.DeviceKey}");
            }
        }

        if (heartbeatData?.DeviceAppId > 0)
        {
            // Update car connection status
            var commitCar = CommitCarConnectionStatus(heartbeatData, stoppingToken);

            // Update heartbeat
            var hb = CommitHeartbeat(heartbeatData, value!);

            // Check log level
            var logs = CheckDeviceAppLogLevel(heartbeatData);

            await Task.WhenAll(commitCar, hb, logs);
        }
    }

    /// <summary>
    /// Save the latest timestamp update to the database.
    /// </summary>
    /// <param name="hb"></param>
    /// <param name="db"></param>
    private async Task CommitHeartbeat(DeviceApp.Shared.DeviceAppHeartbeat hb, string hbjson)
    {
        Logger.LogTrace($"Saving heartbeat: {hb.DeviceAppId}...");

        var cache = cacheMuxer.GetDatabase();
        await cache.HashSetAsync(Consts.DEVICEAPP_STATUS, new RedisValue(hb.DeviceAppId.ToString()), hbjson);
    }

    /// <summary>
    /// Determine if there is a user log level override set for the device.  If so, send it to the device.
    /// </summary>
    /// <param name="hb"></param>
    /// <param name="deviceAppKey"></param>
    private async Task CheckDeviceAppLogLevel(DeviceApp.Shared.DeviceAppHeartbeat hb)
    {
        var cache = cacheMuxer.GetDatabase();
        var key = string.Format(Consts.DEVICEAPP_LOG_DESIRED_LEVEL, hb.DeviceKey);
        var rv = await cache.StringGetAsync(key);
        if (rv.HasValue)
        {
            //LogLevel desiredLevel;
            //try
            //{
            //    // This will throw ArgumentException if value is not valid and bail out
            //    desiredLevel = LogLevel.FromString(rv.ToString());
            //    var currentLevel = LogLevel.FromString(hb.LogLevel);
            //    if (desiredLevel != currentLevel)
            //    {
            //        Logger.LogDebug($"Sending log level update for device {hb.DeviceKey}");
            //        var cmd = new Command
            //        {
            //            CommandType = CommandTypes.SET_LOG_LEVEL,
            //            Data = desiredLevel.Name,
            //            DestinationId = hb.DeviceKey.ToString(),
            //            Timestamp = DateTime.UtcNow
            //        };
            //        await Commands.SendCommandAsync(cmd, new Guid(cmd.DestinationId));
            //    }
            //}
            //catch (ArgumentException) { }
        }
    }

    /// <summary>
    /// Update car connection status for the received device heartbeat.
    /// </summary>
    private async Task CommitCarConnectionStatus(DeviceApp.Shared.DeviceAppHeartbeat hb, CancellationToken stoppingToken)
    {
        Logger.LogTrace($"Saving car connection status: {hb.DeviceAppId}...");

        // Get the car ID for the device
        var carId = await hybridCache.GetOrCreateAsync(string.Format(CarConnectionCacheConst.DEVICE_ASSIGNED_CAR, hb.DeviceAppId),
            async cancel => await LoadAssignedCar(hb.DeviceAppId, cancel),
            cancellationToken: stoppingToken);

        if (carId == null)
        {
            Logger.LogInformation($"No car assigned to device {hb.DeviceAppId} but receiving status");
            return;
        }

        // Get all the devices for the car
        var carDevices = await hybridCache.GetOrCreateAsync(string.Format(CarConnectionCacheConst.CAR_DEVICES_LOOKUP, carId),
            async cancel => await LoadCarDevices(carId.Value, cancel),
            cancellationToken: stoppingToken);

        // Load existing car connection status
        var cache = cacheMuxer.GetDatabase();
        var ccsJson = cache.HashGet(CarConnectionCacheConst.CAR_STATUS, carId.Value);
        CarConnectionStatus? ccs = null;
        if (!ccsJson.IsNullOrEmpty)
        {
            ccs = JsonConvert.DeserializeObject<CarConnectionStatus>(ccsJson!);
        }
        if (ccs == null)
        {
            ccs = new CarConnectionStatus { CarId = carId.Value };
            foreach (var did in carDevices)
            {
                ccs.DeviceConnectionStatuses.Add(new DeviceConnectionStatus { DeviceAppId = did });
            }
        }

        var dcs = ccs.DeviceConnectionStatuses.FirstOrDefault(d => d.DeviceAppId == hb.DeviceAppId);
        if (dcs == null)
        {
            Logger.LogWarning($"Device {hb.DeviceAppId} not found in car {carId}");
            dcs = new DeviceConnectionStatus { DeviceAppId = hb.DeviceAppId };
            ccs.DeviceConnectionStatuses.Add(dcs);
        }

        // Use the server time for the last heartbeat incase the device time is off
        dcs.LastThreeHeartbeats.Add(DateTime.UtcNow);
        if (dcs.LastThreeHeartbeats.Count > 3)
        {
            dcs.LastThreeHeartbeats.RemoveAt(0);
        }

        // Make sure the interval values are correct
        foreach (var ds in ccs.DeviceConnectionStatuses)
        {
            if (ds.HeartbeatIntervalMs == 0)
            {
                using var db = await dbFactory.CreateDbContextAsync(stoppingToken);
                ds.HeartbeatIntervalMs = await db.DeviceAppConfigs.Where(d => d.Id == ds.DeviceAppId)
                    .Join(db.CanAppConfigs, d => d.Id, c => c.DeviceAppId, (d, c) => c.HeartbeatFrequencyMs)
                    .FirstOrDefaultAsync(cancellationToken: stoppingToken);
            }
        }

        var json = JsonConvert.SerializeObject(ccs);
        await cache.StreamAddAsync(CarConnectionCacheConst.CAR_CONN_STATUS_SUBSCRIPTION, [new NameValueEntry(ccs.CarId, json)]);
        await cache.HashSetAsync(CarConnectionCacheConst.CAR_STATUS, carId.Value, json);
    }

    private async Task<int?> LoadAssignedCar(int deviceId, CancellationToken stoppingToken)
    {
        using var db = await dbFactory.CreateDbContextAsync(stoppingToken);
        return await db.DeviceAppConfigs.Where(d => d.Id == deviceId).Select(d => d.CarId).FirstOrDefaultAsync(cancellationToken: stoppingToken);
    }

    private async Task<List<int>> LoadCarDevices(int carId, CancellationToken stoppingToken)
    {
        using var db = await dbFactory.CreateDbContextAsync(stoppingToken);
        return await db.DeviceAppConfigs.Where(c => c.CarId == carId).Select(c => c.Id).ToListAsync(cancellationToken: stoppingToken);
    }

    private async Task<int> LoadDeviceId(Guid deviceKey, CancellationToken stoppingToken)
    {
        using var db = await dbFactory.CreateDbContextAsync(stoppingToken);
        return await db.DeviceAppConfigs.Where(d => d.DeviceAppKey == deviceKey).Select(d => d.Id).FirstOrDefaultAsync(cancellationToken: stoppingToken);
    }

    private async IAsyncEnumerable<string> GetCacheKeysAsync(string pattern)
    {
        if (string.IsNullOrWhiteSpace(pattern))
            throw new ArgumentException("Value cannot be null or whitespace.", nameof(pattern));

        foreach (var endpoint in cacheMuxer.GetEndPoints())
        {
            var server = cacheMuxer.GetServer(endpoint);
            await foreach (var key in server.KeysAsync(pattern: pattern))
            {
                yield return key.ToString();
            }
        }
    }
}
