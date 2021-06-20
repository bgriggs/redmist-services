using BigMission.Cache.Models;
using BigMission.Cache.Models.FuelStatistics;
using BigMission.EntityFrameworkCore;
using Newtonsoft.Json;
using StackExchange.Redis;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;

namespace BigMission.FuelStatistics
{
    class Event : IDisposable
    {
        public int RhEventId { get; private set; }
        public Dictionary<string, Car> Cars { get; } = new Dictionary<string, Car>();
        private readonly ConnectionMultiplexer cacheMuxer;
        private readonly string dbConnStr;
        private bool disposed;


        public Event(string rhEventId, ConnectionMultiplexer cacheMuxer, string dbConnStr)
        {
            if (!int.TryParse(rhEventId, out var id)) { throw new ArgumentException("rhEventId"); }
            RhEventId = id;
            this.cacheMuxer = cacheMuxer;
            this.dbConnStr = dbConnStr;
        }


        /// <summary>
        /// Pull in any existing data for the event to reset on service restart or event change.
        /// </summary>
        public void Initialize()
        {
            // Load any saved laps from log for the event
            var cf = new BigMissionDbContextFactory();
            using var db = cf.CreateDbContext(new[] { dbConnStr });
            var laps = db.CarRacerLaps
                .Where(l => l.EventId == RhEventId)
                .Select(l => new Lap
                {
                    EventId = RhEventId,
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

                // Check for an event/lap reset when new laps are less than what's tracked for the car.
                // This is typcially when you have a multi-race event.
                if (laps.Any() && car.Laps.Any())
                {
                    var latestLap = laps.Max(l => l.CurrentLap);
                    var carsLatest = car.Laps.Keys.Max();
                    if (carsLatest > latestLap)
                    {
                        car.Reset();
                    }
                }

                car.AddLap(cl.ToArray());

                // Save car status
                var cache = cacheMuxer.GetDatabase();
                var eventKey = string.Format(Consts.FUEL_STAT, RhEventId);
                var carJson = JsonConvert.SerializeObject(car);
                cache.HashSet(eventKey, car.Number, carJson);
                //Newtonsoft.Json.Serialization.ITraceWriter traceWriter = new Newtonsoft.Json.Serialization.MemoryTraceWriter();
                //var jss = new JsonSerializerSettings { TraceWriter = traceWriter, Converters = { new Newtonsoft.Json.Converters.JavaScriptDateTimeConverter() } };
                //var c = JsonConvert.DeserializeObject<CarBase>(carJson, jss);
                //Console.WriteLine(traceWriter);
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
