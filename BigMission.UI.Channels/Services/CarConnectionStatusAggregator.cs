using BigMission.Backend.Shared.Models;
using BigMission.ServiceStatusTools;
using BigMission.UI.Channels.Hubs;
using Microsoft.AspNetCore.SignalR;
using Newtonsoft.Json;
using StackExchange.Redis;

namespace BigMission.UI.Channels.Services;

public class CarConnectionStatusAggregator : BackgroundService
{
    private ILogger Logger { get; }
    private readonly IHubContext<StatusHub> statusHub;
    private readonly IConnectionMultiplexer cache;
    private readonly ServiceTracking serviceTracking;

    public CarConnectionStatusAggregator(ILoggerFactory loggerFactory, IHubContext<StatusHub> statusHub, IConnectionMultiplexer cache, ServiceTracking serviceTracking)
    {
        Logger = loggerFactory.CreateLogger(GetType().Name);
        this.statusHub = statusHub;
        this.cache = cache;
        this.serviceTracking = serviceTracking;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var db = cache.GetDatabase();
        if (!(await db.KeyExistsAsync(CarConnectionCacheConst.CAR_STATUS_SUBSCRIPTION)) || (await db.StreamGroupInfoAsync(CarConnectionCacheConst.CAR_STATUS_SUBSCRIPTION)).All(x => x.Name != CarConnectionCacheConst.GROUP_NAME))
        {
            await db.StreamCreateConsumerGroupAsync(CarConnectionCacheConst.CAR_STATUS_SUBSCRIPTION, CarConnectionCacheConst.GROUP_NAME, "0-0", true);
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            var result = await db.StreamReadGroupAsync(CarConnectionCacheConst.CAR_STATUS_SUBSCRIPTION, CarConnectionCacheConst.GROUP_NAME, serviceTracking.ServiceId, ">", 1);
            if (result.Length == 0)
            {
                await Task.Delay(100, stoppingToken);
                continue;
            }

            Logger.LogDebug($"Received {result.Length} car connection status updates.");
            var statusUpdates = new List<CarConnectionStatus>();
            foreach (var r in result)
            {
                foreach (var nv in r.Values)
                {
                    var carConnStatus = JsonConvert.DeserializeObject<CarConnectionStatus>(nv.Value.ToString());
                    if (carConnStatus != null)
                    {
                        statusUpdates.Add(carConnStatus);
                    }
                }

                _ = db.StreamAcknowledgeAsync(CarConnectionCacheConst.CAR_STATUS_SUBSCRIPTION, CarConnectionCacheConst.GROUP_NAME, r.Id, CommandFlags.FireAndForget)
                    .ContinueWith((t) => Logger.LogError(t.Exception, $"Error sending stream ack: {CarConnectionCacheConst.CAR_STATUS_SUBSCRIPTION}"), TaskContinuationOptions.OnlyOnFaulted);
            }

            var json = JsonConvert.SerializeObject(statusUpdates);
            await statusHub.Clients.All.SendAsync("ReceiveCarConnectionUpdate", json, stoppingToken);

            await Task.Delay(10, stoppingToken);
        }
    }
}
