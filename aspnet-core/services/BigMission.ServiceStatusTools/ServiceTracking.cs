using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using BigMission.EntityFrameworkCore;
using BigMission.ServiceData;
using NLog;

namespace BigMission.ServiceStatusTools
{
    public class ServiceTracking : IDisposable
    {
        private readonly ServiceStatus status;
        private readonly string connString;
        private Timer statusTimer;
        private ILogger Logger { get; }

        public ServiceTracking(Guid id, string name, string connString, ILogger logger)
        {
            if (name == null || connString == null)
            {
                throw new ArgumentNullException();
            }

            status = new ServiceStatus { ServiceId = id, Name = name, State = ServiceState.OFFLINE, Note = "Initializing" };
            this.connString = connString;
            Logger = logger;

            var cf = new BigMissionDbContextFactory();
            using var db = cf.CreateDbContext(new[] { connString });
            Update(status.State, status.Note, db);
        }

        public void Update(string state, string note)
        {
            var cf = new BigMissionDbContextFactory();
            using var db = cf.CreateDbContext(new[] { connString });
            Update(state, note, db);
        }

        private void Update(string state, string note, BigMissionDbContext db)
        {
            status.State = state;
            status.Note = note;
            status.Timestamp = DateTime.UtcNow;

            var row = db.ServiceStatus.SingleOrDefault(s => s.ServiceId == status.ServiceId);
            if (row != null)
            {
                row.State = state;
                row.Note = note;
                row.Timestamp = status.Timestamp;
            }
            else
            {
                db.ServiceStatus.Add(status);
            }

            db.SaveChanges();
        }

        /// <summary>
        /// Updates service status on a frequency while the service is running.
        /// </summary>
        public void Start()
        {
            if (statusTimer != null)
            {
                throw new InvalidOperationException("Already running.");
            }

            statusTimer = new Timer(UpdateCallback, null, 100, 10000);
        }

        /// <summary>
        /// Update service status timestamp in the database.
        /// </summary>
        /// <param name="obj"></param>
        private void UpdateCallback(object obj)
        {
            if (Monitor.TryEnter(statusTimer))
            {
                try
                {
                    var cf = new BigMissionDbContextFactory();
                    using var db = cf.CreateDbContext(new[] { connString });
                    Update(ServiceState.ONLINE, string.Empty, db);
                    ScanForTimeouts(db);
                }
                catch (Exception ex)
                {
                    Logger.Error(ex, "Error updating service status");
                }
                finally
                {
                    Monitor.Exit(statusTimer);
                }
            }
        }

        /// <summary>
        /// Look for other service timeouts.
        /// </summary>
        /// <param name="db"></param>
        private void ScanForTimeouts(BigMissionDbContext db)
        {
            var timeout = DateTime.UtcNow - TimeSpan.FromSeconds(30);
            var rows = db.ServiceStatus.Where(s => s.Timestamp < timeout);
            foreach (var r in rows)
            {
                r.State = ServiceState.OFFLINE;
                r.Note = "Service failed to respond within 30 seconds";
            }
            db.SaveChanges();
        }

        public void Dispose()
        {
            if (statusTimer != null)
            {
                statusTimer.Dispose();
            }
        }
    }
}
