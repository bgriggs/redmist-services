using Azure.Messaging.EventHubs.Consumer;
using BigMission.CommandTools;
using BigMission.EntityFrameworkCore;
using BigMission.RaceManagement;
using BigMission.ServiceData;
using BigMission.ServiceStatusTools;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using NLog;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace BigMission.CarRealTimeStatusProcessor
{
    /// <summary>
    /// Processes channel status from a device and updates the latest values into a table.
    /// </summary>
    class Application
    {
        private IConfiguration Config { get; }
        private ILogger Logger { get; }
        private ServiceTracking ServiceTracking { get; }
        private readonly EventHubHelpers ehReader;

        private readonly Dictionary<int, ChannelStatus> last = new Dictionary<int, ChannelStatus>();
        private Timer saveTimer;
        private BigMissionDbContext context;
        private readonly ManualResetEvent serviceBlock = new ManualResetEvent(false);


        public Application(IConfiguration config, ILogger logger, ServiceTracking serviceTracking)
        {
            Config = config;
            Logger = logger;
            ServiceTracking = serviceTracking;
            ehReader = new EventHubHelpers(logger);
        }


        public void Run()
        {
            ServiceTracking.Update(ServiceState.STARTING, string.Empty);

            var cf = new BigMissionDbContextFactory();
            context = cf.CreateDbContext(new[] { Config["ConnectionString"] });

            // Process changes from stream and cache them here is the service
            var partitionFilter = EventHubHelpers.GetPartitionFilter(Config["PartitionFilter"]);
            Task receiveStatus = ehReader.ReadEventHubPartitionsAsync(Config["KafkaConnectionString"], Config["KafkaDataTopic"], Config["KafkaConsumerGroup"],
                partitionFilter, EventPosition.Latest, ReceivedEventCallback);

            // Process the cached status and update the database
            saveTimer = new Timer(SaveCallback, null, 2000, 300);

            // Start updating service status
            ServiceTracking.Start();
            Logger.Info("Started");
            receiveStatus.Wait();
            serviceBlock.WaitOne();
        }

        private void ReceivedEventCallback(PartitionEvent receivedEvent)
        {
            try
            {
                var json = Encoding.UTF8.GetString(receivedEvent.Data.Body.ToArray());
                var chDataSet = JsonConvert.DeserializeObject<ChannelDataSet>(json);

                if (chDataSet.Data == null)
                {
                    chDataSet.Data = new ChannelStatus[] { };
                }

                Logger.Trace($"Received log: {chDataSet.DeviceAppId}");

                // Update cached status
                lock (last)
                {
                    foreach (var ch in chDataSet.Data)
                    {
                        if (ch.DeviceAppId == 0)
                        {
                            ch.DeviceAppId = chDataSet.DeviceAppId;
                        }

                        if (last.TryGetValue(ch.ChannelId, out ChannelStatus row))
                        {
                            if (row.Value != ch.Value)
                            {
                                row.Value = ch.Value;
                                row.Timestamp = ch.Timestamp;
                            }
                        }
                        else // Create new row
                        {
                            var cr = new ChannelStatus { DeviceAppId = ch.DeviceAppId, ChannelId = ch.ChannelId, Value = ch.Value, Timestamp = ch.Timestamp };
                            last[ch.ChannelId] = cr;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Unable to process event from event hub partition");
            }
        }

        public void UpdateChanges(ChannelStatus[] rows)
        {
            foreach (var updated in rows)
            {
                var r = context.ChannelStatus.SingleOrDefault(c => c.DeviceAppId == updated.DeviceAppId && c.ChannelId == updated.ChannelId);
                if (r != null)
                {
                    r.Value = updated.Value;
                    r.Timestamp = updated.Timestamp;
                }
                else
                {
                    context.ChannelStatus.Add(updated);
                }
            }

            var sw = Stopwatch.StartNew();
            context.SaveChanges();
            Logger.Trace($"DB Commit in {sw.ElapsedMilliseconds}ms");
        }

        private void SaveCallback(object obj)
        {
            if (Monitor.TryEnter(saveTimer))
            {
                try
                {
                    // Get a copy of the current status as not to block
                    ChannelStatus[] status;
                    lock (last)
                    {
                        status = last.Select(l => l.Value.Clone()).ToArray();
                    }

                    // Commit changes to DB
                    UpdateChanges(status);
                }
                catch (Exception ex)
                {
                    Logger.Error(ex, "Unable to commit status");
                }
                finally
                {
                    Monitor.Exit(saveTimer);
                }
            }
        }
    }
}
