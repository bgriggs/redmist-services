using BigMission.Database;
using BigMission.Database.Models;
using BigMission.DeviceApp.Shared;
using Microsoft.Extensions.Configuration;
using NLog;
using System.Diagnostics;

namespace BigMission.CarTelemetryProcessor
{
    internal class ChannelLogging : ITelemetryConsumer
    {
        public ILogger Logger { get; }
        public IConfiguration Config { get; }


        public ChannelLogging(ILogger logger, IConfiguration config)
        {
            Logger = logger;
            Config = config;
        }

        public async Task ProcessTelemetryMessage(ChannelDataSetDto receivedTelem)
        {
            try
            {
                Logger.Trace($"ChannelLogging received log: {receivedTelem.DeviceAppId} Count={receivedTelem.Data.Length}");
                if (receivedTelem.Data?.Length > 0)
                {
                    var logs = new List<ChannelLog>();
                    foreach (var l in receivedTelem.Data)
                    {
                        if (l.DeviceAppId == 0)
                        {
                            l.DeviceAppId = receivedTelem.DeviceAppId;
                        }
                        var dblog = new ChannelLog { DeviceAppId = l.DeviceAppId, ChannelId = l.ChannelId, Timestamp = l.Timestamp, Value = l.Value };
                        logs.Add(dblog);
                    }

                    using var db = new RedMist(Config["ConnectionString"]);
                    db.ChannelLogs.AddRange(logs);
                    var sw = Stopwatch.StartNew();
                    await db.SaveChangesAsync();
                    Logger.Trace($"Device source {receivedTelem.DeviceAppId} DB Commit in {sw.ElapsedMilliseconds}ms");
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Unable to save logs");
            }
        }
    }
}
