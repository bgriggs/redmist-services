using BigMission.Cache.Models;
using BigMission.EntityFrameworkCore;
using Newtonsoft.Json;
using StackExchange.Redis;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace BigMission.FuelStatistics
{
    class Event : IDisposable
    {
        public int EventId { get; private set; }
        public Dictionary<string, Car> Cars { get; } = new Dictionary<string, Car>();
        private readonly ConnectionMultiplexer cacheMuxer;
        private readonly string dbConnStr;
        private bool disposed;


        public Event(int eventId, ConnectionMultiplexer cacheMuxer, string dbConnStr)
        {
            if (eventId <= 0) { throw new ArgumentException("eventId"); }
            EventId = eventId;
            this.cacheMuxer = cacheMuxer;
            this.dbConnStr = dbConnStr;
        }


        public void Initialize()
        {
            var cf = new BigMissionDbContextFactory();
            using var db = cf.CreateDbContext(new[] { dbConnStr });
            var laps = db.CarRacerLaps
                .Where(l => l.EventId == EventId)
                .Select(l => new Lap
                {
                    EventId = EventId,
                    CarNumber = l.CarNumber,
                    Timestamp = l.Timestamp,
                    ClassName = l.ClassName,
                    PositionInRun = l.PositionInRun,
                    CurrentLap = l.CurrentLap,
                    LastLapTimeSeconds = l.LastLapTimeSeconds,
                    LastPitLap = l.LastPitLap,
                    PitStops = l.PitStops
                })
                .ToArray();

            UpdateLap(laps);
        }

        public void UpdateLap(params Lap[] laps)
        {
            var carLaps = laps.GroupBy(l => l.CarNumber);
            foreach (var cl in carLaps)
            {
                if (!Cars.TryGetValue(cl.Key, out var car))
                {
                    car = new Car(cl.Key, cl.First().ClassName);
                    Cars[cl.Key] = car;
                }
                car.AddLap(cl.ToArray());

                // Save car status
                var cache = cacheMuxer.GetDatabase();
                var eventKey = string.Format(Consts.FUEL_STAT, EventId);
                var carJson = JsonConvert.SerializeObject(car);
                cache.HashSet(eventKey, car.Number, carJson);
            }
        }


        #region Dispose

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposed)
                return;

            if (disposing)
            {

            }

            disposed = true;
        }

        #endregion
    }
}
