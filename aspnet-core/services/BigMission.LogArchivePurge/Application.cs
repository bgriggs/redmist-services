using Azure.Storage.Blobs;
using BigMission.EntityFrameworkCore;
using BigMission.Logging;
using BigMission.RaceManagement;
using BigMission.ServiceData;
using BigMission.ServiceStatusTools;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using NLog;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

namespace BigMission.LogArchivePurge
{
    class Application
    {
        private IConfiguration Config { get; }
        private ILogger Logger { get; }
        private ServiceTracking ServiceTracking { get; }
        private readonly ManualResetEvent serviceBlock = new ManualResetEvent(false);
        private Timer runTimer;
        private const string BLOB_FORMAT = "yyyyMMddHH";

        public Application(IConfiguration config, ILogger logger, ServiceTracking serviceTracking)
        {
            Config = config;
            Logger = logger;
            ServiceTracking = serviceTracking;
        }

        public void Run()
        {
            ServiceTracking.Update(ServiceState.STARTING, string.Empty);

            var interval = TimeSpan.FromSeconds(double.Parse(Config["CheckTimerIntervalSecs"]));
            runTimer = new Timer(CheckToRun, null, 1000, (int)interval.TotalMilliseconds);

            // Start updating service status
            ServiceTracking.Start();
            serviceBlock.WaitOne();
        }

        /// <summary>
        /// Load settings from the database and perform operations if in timeframe or manually overriden.
        /// </summary>
        private void CheckToRun(object obj)
        {
            try
            {
                if (Monitor.TryEnter(runTimer))
                {
                    try
                    {
                        Logger.Info("Checking log settings");

                        var cf = new BigMissionDbContextFactory();
                        using var db = cf.CreateDbContext(new[] { Config["ConnectionString"] });
                        var settings = db.ArchivePurgeSettings.FirstOrDefault();
                        if (settings != null)
                        {
                            using var conn = new SqlConnection(Config["ConnectionString"]);
                            conn.Open();

                            // Run Audit log purge
                            // Check the time window for when this service is allowed to run
                            var inTimeWindow = IsInTimeWindow(DateTime.UtcNow, settings.RunStart, settings.RunEnd);
                            if (settings.RunAuditMaintenance || inTimeWindow)
                            {
                                PurgeAuditLog(conn, int.Parse(Config["AuditLogRetentionDays"]));
                                ResetAuditMaintenanceFlag(conn);
                            }

                            // Run archive purge on channel log
                            inTimeWindow = IsInTimeWindow(DateTime.UtcNow, settings.RunStart, settings.RunEnd);
                            if (settings.RunChannelMaintenance || inTimeWindow)
                            {
                                ArchiveChannelLogs(db, conn);
                                ResetChannelMaintenanceFlag(conn);
                            }
                        }
                        else
                        {
                            Logger.Error("No settings found in database.");
                        }
                    }
                    finally
                    {
                        Monitor.Exit(runTimer);
                    }
                }
                else
                {
                    Logger.Trace("Check log settings skipped");
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error checking log settings.");
            }
        }

        /// <summary>
        /// Delete logs from the API web host service.
        /// </summary>
        private void PurgeAuditLog(SqlConnection conn, int retentionDays)
        {
            Logger.Info($"Running PurgeAuditLog...");
            var ret = DateTime.UtcNow - TimeSpan.FromDays(retentionDays);
            var c = $"DELETE FROM AbpAuditLogs WHERE ExecutionTime < '{ret}'";
            using var cmd = new SqlCommand(c, conn);
            int rows = cmd.ExecuteNonQuery();
            Logger.Info($"Deleted {rows} from AbpAuditLogs");
        }

        /// <summary>
        /// Move channel logs from DB to blob storage
        /// </summary>
        /// <param name="db"></param>
        /// <param name="conn"></param>
        private void ArchiveChannelLogs(BigMissionDbContext db, SqlConnection conn)
        {
            Logger.Info($"Running ArchiveChannelLog...");

            var teamRetention = LoadTeamRetentionSettings(db);
            Logger.Info($"Loaded {teamRetention.Count()} team settings.");
            foreach (var tr in teamRetention)
            {
                var teamDeviceIds = LoadTeamDeviceIds(db, tr.TenantId);
                Logger.Info($"Loaded {teamDeviceIds.Count()} devices for team {tr.TenantId}.");
                foreach (var dev in teamDeviceIds)
                {
                    // Perform archive on DB channel logs
                    var oldestRecord = LoadOldestChannelLog(dev, conn);
                    if (oldestRecord != null)
                    {
                        Logger.Info($"Device {dev} oldest record found={oldestRecord}");
                        var ots = oldestRecord.Value;

                        // Transfer in chunks of an hour
                        var inc = TimeSpan.FromHours(1);
                        var chunkTs = new DateTime(ots.Year, ots.Month, ots.Day, ots.Hour, 0, 0);
                        while (chunkTs < DateTime.UtcNow)
                        {
                            var logs = LoadChannelLogs(db, dev, chunkTs, chunkTs + inc);
                            if (logs.Any())
                            {
                                SaveChannelLogArchive(dev, logs, chunkTs);
                                DeleteChannelLogs(dev, chunkTs, chunkTs + inc, conn);
                            }
                            chunkTs += inc;
                        }
                    }
                    else
                    {
                        Logger.Info($"Device {dev} has no records.");
                    }

                    // Perform purge on archived data
                    var offsetDt = DateTime.UtcNow - TimeSpan.FromDays(tr.PurgeDays);
                    var purgeDate = new DateTime(offsetDt.Year, offsetDt.Month, offsetDt.Day, offsetDt.Hour, 0, 0);
                    PurgeChannelArchive(dev, purgeDate);
                }
            }
        }

        /// <summary>
        /// Retention settings are set according to the team's edition.
        /// </summary>
        private TeamRetentionPolicy[] LoadTeamRetentionSettings(BigMissionDbContext db)
        {
            // Keep deleted row in the list so old data does not stay in the DB
            return db.TeamRetentionPolicies.ToArray();
            //Where(t => !t.IsDeleted)
        }

        /// <summary>
        /// Get the devices in use by a team.  Data is associated with the device ID.
        /// </summary>
        private int[] LoadTeamDeviceIds(BigMissionDbContext db, int teamId)
        {
            // Keep deleted devices in the results to make sure the old data does not get retained after deletion
            return (from d in db.DeviceAppConfig
                    join c in db.Cars on d.CarId equals c.Id
                    where c.TenantId == teamId
                    select d.Id).ToArray();
        }

        private DateTime? LoadOldestChannelLog(int deviceId, SqlConnection conn)
        {
            var c = $"SELECT TOP 1 [Timestamp] FROM [ChannelLog] WHERE [DeviceAppId]={deviceId}";
            using var cmd = new SqlCommand(c, conn);
            object robj = cmd.ExecuteScalar();
            if (robj != null)
            {
                return (DateTime)robj;
            }
            return null;
        }

        private ChannelLog[] LoadChannelLogs(BigMissionDbContext db, int device, DateTime start, DateTime end)
        {
            return db.ChannelLog
                .Where(l => l.DeviceAppId == device && l.Timestamp >= start && l.Timestamp < end)
                .ToArray();
        }

        private void DeleteChannelLogs(int device, DateTime start, DateTime end, SqlConnection conn)
        {
            Logger.Info($"Deleting channel rows for device {device}");
            var c = $"DELETE FROM ChannelLog WHERE DeviceAppId={device} AND [Timestamp]>='{start}' AND [Timestamp]<'{end}'";
            using var cmd = new SqlCommand(c, conn);
            var rows = cmd.ExecuteNonQuery();
            Logger.Info($"Deleted {rows} from ChannelLog for Device {device} [Timestamp]>='{start}' AND [Timestamp]<'{end}'");
        }

        /// <summary>
        /// Create a CSV and save to blob storage.
        /// </summary>
        private void SaveChannelLogArchive(int device, ChannelLog[] logs, DateTime start)
        {
            var blobServiceClient = new BlobServiceClient(Config["ArchiveBlobConnStr"]);
            var containers = blobServiceClient.GetBlobContainers();
            var containerName = $"device-{device}";
            var cont = containers.FirstOrDefault(c => c.Name == containerName);
            if (cont == null)
            {
                blobServiceClient.CreateBlobContainer(containerName);
            }

            var contClient = blobServiceClient.GetBlobContainerClient(containerName);
            var blobName = $"{start.ToString(BLOB_FORMAT)}";
            var blobClient = contClient.GetBlobClient(blobName);
            var csvData = CreateChanneLogCsv(logs);
            using var ms = new MemoryStream(Encoding.UTF8.GetBytes(csvData));
            blobClient.Upload(ms, true);
        }

        private static string CreateChanneLogCsv(ChannelLog[] logs)
        {
            var sb = new StringBuilder();
            foreach (var l in logs)
            {
                sb.AppendLine($"{l.Timestamp},{l.ChannelId},{l.Value}");
            }

            return sb.ToString();
        }

        private void PurgeChannelArchive(int device, DateTime newest)
        {
            Logger.Info($"Purging archive for device {device} older than {newest}...");
            var blobServiceClient = new BlobServiceClient(Config["ArchiveBlobConnStr"]);
            var containers = blobServiceClient.GetBlobContainers();
            Logger.Debug($"Found {containers.Count()} containers.");
            var containerName = $"device-{device}";

            var cont = containers.FirstOrDefault(c => c.Name == containerName);
            if (cont != null)
            {
                Logger.Debug($"Checking container {cont.Name}...");
                var contClient = blobServiceClient.GetBlobContainerClient(containerName);
                var blobs = contClient.GetBlobs();
                foreach (var b in blobs)
                {
                    var s = b.Name.Replace(".csv", "");
                    var date = DateTime.ParseExact(s, BLOB_FORMAT, CultureInfo.InvariantCulture);
                    if (date < newest)
                    {
                        Logger.Debug($"Deleting blob {b.Name}...");
                        contClient.DeleteBlob(b.Name);
                    }
                }
            }
            else
            {
                Logger.Debug($"Did not find container archive for device {device}");
            }
        }

        private void ResetAuditMaintenanceFlag(SqlConnection conn)
        {
            var c = $"UPDATE [ArchivePurgeSettings] SET [RunAuditMaintenance]=0";
            using var cmd = new SqlCommand(c, conn);
            int rows = cmd.ExecuteNonQuery();
            Logger.Info($"Reset {rows} rows RunAuditMaintenance");
        }

        private void ResetChannelMaintenanceFlag(SqlConnection conn)
        {
            var c = $"UPDATE [ArchivePurgeSettings] SET [RunChannelMaintenance]=0";
            using var cmd = new SqlCommand(c, conn);
            int rows = cmd.ExecuteNonQuery();
            Logger.Info($"Reset {rows} rows [RunChannelMaintenance]");
        }

        private static bool IsInTimeWindow(DateTime now, DateTime start, DateTime end)
        {
            TimeSpan startts = new TimeSpan(start.Hour, start.Minute, 0);
            TimeSpan endts = new TimeSpan(end.Hour, end.Minute, 0);
            TimeSpan nowts = now.TimeOfDay;
            // see if start comes before end
            if (startts < endts)
                return startts <= nowts && nowts <= endts;
            // start is after end, so do the inverse comparison
            return !(endts < nowts && nowts < startts);
        }
    }
}
