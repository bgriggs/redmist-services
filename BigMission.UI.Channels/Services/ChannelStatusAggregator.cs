using BigMission.DeviceApp.Shared;
using BigMission.ServiceStatusTools;
using BigMission.UI.Channels.Hubs;
using Microsoft.AspNetCore.SignalR;
using Newtonsoft.Json;
using StackExchange.Redis;

namespace BigMission.UI.Channels.Services;

public class ChannelStatusAggregator : BackgroundService
{
    private const string streamName = "ch-changes";
    private const string groupName = "web-status";

    private ILogger Logger { get; }
    private readonly IHubContext<StatusHub> statusHub;
    private readonly IConnectionMultiplexer cache;
    private readonly ServiceTracking serviceTracking;

    public ChannelStatusAggregator(ILoggerFactory loggerFactory, IHubContext<StatusHub> statusHub, IConnectionMultiplexer cache, ServiceTracking serviceTracking)
    {
        Logger = loggerFactory.CreateLogger(GetType().Name);
        this.statusHub = statusHub;
        this.cache = cache;
        this.serviceTracking = serviceTracking;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var db = cache.GetDatabase();
        if (!(await db.KeyExistsAsync(streamName)) || (await db.StreamGroupInfoAsync(streamName)).All(x => x.Name != groupName))
        {
            await db.StreamCreateConsumerGroupAsync(streamName, groupName, "0-0", true);
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            var result = await db.StreamReadGroupAsync(streamName, groupName, serviceTracking.ServiceId, ">", 1);
            if (result.Length == 0)
            {
                await Task.Delay(100, stoppingToken);
                continue;
            }

            var statusUpdates = new List<ChannelStatusDto>();
            foreach (var r in result)
            {
                foreach (var nv in r.Values)
                {
                    var channel = JsonConvert.DeserializeObject<ChannelStatusDto>(nv.Value.ToString());
                    if (channel != null)
                    {
                        statusUpdates.Add(channel);
                    }
                }

                _ = db.StreamAcknowledgeAsync(streamName, groupName, r.Id, CommandFlags.FireAndForget)
                    .ContinueWith((t) => Logger.LogError(t.Exception, "Error sending stream ack."), TaskContinuationOptions.OnlyOnFaulted);
            }

            var json = JsonConvert.SerializeObject(statusUpdates);
            await statusHub.Clients.All.SendAsync("ReceiveStatusUpdate", json, stoppingToken);

            await Task.Delay(10, stoppingToken);
        }
    }
}
