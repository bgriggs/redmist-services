using BigMission.Database;
using BigMission.Database.Models;
using BigMission.DeviceApp.Shared;
using Microsoft.EntityFrameworkCore;
using System.Diagnostics;

namespace BigMission.CarTelemetryProcessor
{
    internal class ChannelLogging : ITelemetryConsumer
    {
        private readonly IDbContextFactory<RedMist> dbFactory;

        public ILogger Logger { get; }


        public ChannelLogging(ILoggerFactory loggerFactory, IDbContextFactory<RedMist> dbFactory)
        {
            Logger = loggerFactory.CreateLogger(GetType().Name);
            this.dbFactory = dbFactory;
        }

        public async Task ProcessTelemetryMessage(ChannelDataSetDto receivedTelem)
        {
            try
            {
                Logger.LogTrace($"ChannelLogging received log: {receivedTelem.DeviceAppId} Count={receivedTelem.Data.Length}");
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

                    using var db = await dbFactory.CreateDbContextAsync();
                    db.ChannelLogs.AddRange(logs);
                    var sw = Stopwatch.StartNew();
                    await db.SaveChangesAsync();
                    Logger.LogTrace($"Device source {receivedTelem.DeviceAppId} DB Commit in {sw.ElapsedMilliseconds}ms");
                }
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Unable to save logs");
            }
        }
    }
}
