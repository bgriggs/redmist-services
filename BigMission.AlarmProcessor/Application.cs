using BigMission.Cache.Models;
using BigMission.Database;
using BigMission.DeviceApp.Shared;
using BigMission.ServiceStatusTools;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using StackExchange.Redis;
using System.Diagnostics;

namespace BigMission.AlarmProcessor;

/// <summary>
/// Processes channel status from a device and look for alarm conditions to be met.
/// </summary>
class Application : BackgroundService
{
    private ILogger Logger { get; }
    private readonly IConnectionMultiplexer cacheMuxer;
    private readonly IDbContextFactory<RedMist> dbFactory;
    private readonly StartupHealthCheck startup;

    /// <summary>
    /// Alarms by their group
    /// </summary>
    private readonly Dictionary<string, List<AlarmStatus>> alarmStatus = [];
    private static readonly SemaphoreSlim alarmStatusLock = new(1, 1);
    private readonly Dictionary<int, int[]> deviceToChannelMappings = [];
    private static readonly SemaphoreSlim deviceToChannelMappingsLock = new(1, 1);


    public Application(ILoggerFactory loggerFactory, IConnectionMultiplexer cache, IDbContextFactory<RedMist> dbFactory, StartupHealthCheck startup)
    {
        Logger = loggerFactory.CreateLogger(GetType().Name);
        cacheMuxer = cache;
        this.dbFactory = dbFactory;
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

        await LoadAlarmConfiguration(stoppingToken);
        await LoadDeviceChannels(stoppingToken);
        
        var sub = cacheMuxer.GetSubscriber();
        await sub.SubscribeAsync(RedisChannel.Literal(Consts.CAR_TELEM_SUB), async (channel, message) =>
        {
            await Task.Run(() => ProcessTelemetryForAlarms(message, stoppingToken));
        });

        // Watch for changes in device app configuration such as channels
        await sub.SubscribeAsync(RedisChannel.Literal(Consts.CAR_CONFIG_CHANGED_SUB), async (channel, message) =>
        {
            Logger.LogInformation("Car device app configuration notification received");
            await LoadDeviceChannels(stoppingToken);
        });

        await sub.SubscribeAsync(RedisChannel.Literal(Consts.CAR_ALARM_CONFIG_CHANGED_SUB), async (channel, message) =>
        {
            Logger.LogInformation("Alarm configuration notification received");
            await LoadAlarmConfiguration(stoppingToken);
            await LoadDeviceChannels(stoppingToken);
        });

        Logger.LogInformation("Started");
        await startup.SetStarted();
    }

    private async Task ProcessTelemetryForAlarms(RedisValue value, CancellationToken stoppingToken)
    {
        var sw = Stopwatch.StartNew();
        var telemetryData = JsonConvert.DeserializeObject<ChannelDataSetDto>(value!);
        if (telemetryData != null)
        {
            telemetryData.Data ??= Array.Empty<ChannelStatusDto>();
            Logger.LogDebug($"Received telemetry from: '{telemetryData.DeviceAppId}'");

            Dictionary<string, List<AlarmStatus>> alarms;
            await alarmStatusLock.WaitAsync(stoppingToken);
            try
            {
                alarms = alarmStatus.ToDictionary(x => x.Key, x => x.Value);
            }
            finally
            {
                alarmStatusLock.Release();
            }

            var alarmTasks = alarms.Values.Select(async ag =>
            {
                try
                {
                    // Order the alarms by priority and check the highest priority first
                    var orderedAlarms = ag.OrderBy(a => a.Priority);
                    bool channelAlarmActive = false;
                    foreach (var alarm in orderedAlarms)
                    {
                        Logger.LogTrace($"Processing alarm: {alarm.Alarm.Name}");
                        // If the an alarm is already active on the channel, skip it
                        if (!channelAlarmActive)
                        {
                            // Run the check on the alarm and preform triggers
                            channelAlarmActive = await alarm.CheckConditions(telemetryData.Data);
                        }
                        else // Alarm for channel is active, turn off lower priority alarms
                        {
                            await alarm.Supersede();
                            Logger.LogTrace($"Superseded alarm {alarm.Alarm.Name} due to higher priority being active");
                        }
                    }
                }
                catch (Exception ex)
                {
                    Logger.LogError(ex, "Unable to process alarm status update");
                }
            });
            await Task.WhenAll(alarmTasks);
        }
        Logger.LogTrace($"Processed channels in {sw.ElapsedMilliseconds}ms");
    }


    #region Alarm Configuration

    private async Task LoadAlarmConfiguration(CancellationToken stoppingToken)
    {
        try
        {
            using var context = await dbFactory.CreateDbContextAsync(stoppingToken);
            var alarmConfig = await context.CarAlarms
                .Include(a => a.AlarmConditions)
                .Include(a => a.AlarmTriggers)
                .ToListAsync(cancellationToken: stoppingToken);

            Logger.LogInformation($"Loaded {alarmConfig.Count} Alarms");

            var delKeys = new List<RedisKey>();
            foreach (var ac in alarmConfig)
            {
                var alarmKey = string.Format(Consts.ALARM_STATUS, ac.Id);
                delKeys.Add(alarmKey);
            }
            Logger.LogInformation($"Clearing {delKeys.Count} alarm status");

            var deviceAppIds = await context.DeviceAppConfigs.Where(d => !d.IsDeleted).Select(d => d.Id).ToListAsync(stoppingToken);
            foreach (var dai in deviceAppIds)
            {
                delKeys.Add(string.Format(Consts.ALARM_CH_CONDS, dai));
            }
            Logger.LogInformation($"Clearing {deviceAppIds.Count} alarm channel status");

            var cache = cacheMuxer.GetDatabase();
            await cache.KeyDeleteAsync(delKeys.ToArray(), CommandFlags.FireAndForget);

            // Filter down to active alarms
            alarmConfig = alarmConfig.Where(a => !a.IsDeleted && a.IsEnabled).ToList();

            Logger.LogDebug($"Found {alarmConfig.Count} enabled alarms");
            var als = new List<AlarmStatus>();
            foreach (var ac in alarmConfig)
            {
                var a = new AlarmStatus(ac, dbFactory, Logger, cacheMuxer, GetDeviceAppId);
                als.Add(a);
            }

            // Group the alarms by the targeted channel for alarm progression support, e.g. info, warning, error
            var grps = als.GroupBy(a => a.AlarmGroup);

            await alarmStatusLock.WaitAsync(stoppingToken);
            try
            {
                alarmStatus.Clear();
                foreach (var chGrp in grps)
                {
                    if (!alarmStatus.TryGetValue(chGrp.Key, out List<AlarmStatus>? channelAlarms))
                    {
                        channelAlarms = [];
                        alarmStatus[chGrp.Key] = channelAlarms;
                    }

                    channelAlarms.AddRange(chGrp);
                }
            }
            finally
            {
                alarmStatusLock.Release();
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Unable to initialize alarms");
        }
    }

    private async Task LoadDeviceChannels(CancellationToken stoppingToken)
    {
        try
        {
            Logger.LogInformation($"Loading device channels...");
            using var context = await dbFactory.CreateDbContextAsync(stoppingToken);
            var chMappings = await (from dev in context.DeviceAppConfigs
                                    join alarm in context.CarAlarms on dev.CarId equals alarm.CarId
                                    join ch in context.ChannelMappings on dev.Id equals ch.DeviceAppId
                                    where !dev.IsDeleted && !alarm.IsDeleted && alarm.IsEnabled
                                    select new { did = dev.Id, chid = ch.Id }).ToListAsync(cancellationToken: stoppingToken);

            Logger.LogInformation($"Loaded {chMappings.Count} channels");
            var grps = chMappings.GroupBy(g => g.did);

            await deviceToChannelMappingsLock.WaitAsync(stoppingToken);
            try
            {
                deviceToChannelMappings.Clear();
                foreach (var g in grps)
                {
                    deviceToChannelMappings[g.Key] = g.Select(c => c.chid).ToArray();
                }
            }
            finally
            {
                deviceToChannelMappingsLock.Release();
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Unable to load device channels");
        }
    }

    private async Task<int> GetDeviceAppId(int channelId)
    {
        await deviceToChannelMappingsLock.WaitAsync();
        try
        {
            foreach (var kvp in deviceToChannelMappings)
            {
                if (kvp.Value.Contains(channelId))
                {
                    return kvp.Key;
                }
            }
        }
        finally
        {
            deviceToChannelMappingsLock.Release();
        }
        return 0;
    }

    #endregion
}
