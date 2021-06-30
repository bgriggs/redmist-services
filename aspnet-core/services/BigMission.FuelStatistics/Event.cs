using BigMission.Cache;
using BigMission.Cache.Models;
using BigMission.DeviceApp.Shared;
using BigMission.EntityFrameworkCore;
using BigMission.FuelStatistics.FuelRange;
using BigMission.RaceManagement;
using Newtonsoft.Json;
using NLog;
using StackExchange.Redis;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace BigMission.FuelStatistics
{
    class Event : IDisposable
    {
        private readonly RaceEventSettings settings;
        private ILogger Logger { get; }
        public int RhEventId { get; private set; }
        public Dictionary<string, Car> Cars { get; } = new Dictionary<string, Car>();
        private readonly Dictionary<int, CarRange> carRanges = new Dictionary<int, CarRange>();
        private readonly Dictionary<int, int> deviceAppCarMappings = new Dictionary<int, int>();
        private readonly Dictionary<string, int> carNumberToIdMappings = new Dictionary<string, int>();
        private readonly ConnectionMultiplexer cacheMuxer;
        private readonly string dbConnStr;
        private FuelRangeContext fuelRangeContext;
        private readonly TimeSpan frUpdateInterval = TimeSpan.FromSeconds(2);
        private DateTime lastFrUpdate;
        private readonly TimeSpan flagUpdateInterval = TimeSpan.FromMilliseconds(500);
        private Timer flagUpdateTimer;
        private List<EventFlag> lastEventFlags = new List<EventFlag>();
        private bool disposed;

        public Event(RaceEventSettings settings, ConnectionMultiplexer cacheMuxer, string dbConnStr, ILogger logger)
        {
            this.settings = settings;
            if (!int.TryParse(settings.RaceHeroEventId, out var id)) { throw new ArgumentException("rhEventId"); }
            RhEventId = id;
            this.cacheMuxer = cacheMuxer;
            this.dbConnStr = dbConnStr;
            Logger = logger;
        }


        /// <summary>
        /// Pull in any existing data for the event to reset on service restart or event change.
        /// </summary>
        public void Initialize()
        {
            carRanges.Clear();
            deviceAppCarMappings.Clear();
            carNumberToIdMappings.Clear();

            var cf = new BigMissionDbContextFactory();

            fuelRangeContext = new FuelRangeContext(cacheMuxer, cf.CreateDbContext(new[] { dbConnStr }));

            // Load any saved laps from log for the event
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

            // Load range settings for event cars
            Logger.Info("Loading Fuel Range Settings...");
            var carIds = settings.GetCarIds();
            var carSettings = db.FuelRangeSettings.Where(c => carIds.Contains(c.CarId));
            Logger.Info($"Loaded {carSettings.Count()} fuel range settings");
            foreach (var cs in carSettings)
            {
                var carRange = new CarRange(cs, fuelRangeContext);
                carRanges[cs.CarId] = carRange;
            }

            // Load mappings to associate telemetry from device ID to Car ID
            Logger.Info("Loading device apps...");
            var deviceApps = db.DeviceAppConfig.Where(d => d.CarId.HasValue && carIds.Contains(d.CarId.Value));
            Logger.Info($"Loaded {deviceApps.Count()} device apps");
            foreach (var da in deviceApps)
            {
                deviceAppCarMappings[da.Id] = da.CarId.Value;
            }

            // Load Cars to be able to go from RH car number to a car ID
            Logger.Info("Loading cars...");
            var cars = db.Cars.Where(c => !c.IsDeleted && carIds.Contains(c.Id));
            Logger.Info($"Loaded {cars.Count()} cars");
            foreach (var c in cars)
            {
                carNumberToIdMappings[c.Number.ToUpper()] = c.Id;
            }

            // Load car's telemetry channel definitions
            var deviceAppIds = deviceAppCarMappings.Keys.ToArray();
            var channelNames = new[] { ReservedChannel.SPEED, ReservedChannel.FUEL_LEVEL };
            var channels = db.ChannelMappings.Where(ch => deviceAppIds.Contains(ch.DeviceAppId) && channelNames.Contains(ch.ReservedName));
            foreach (var chMap in channels)
            {
                if (deviceAppCarMappings.TryGetValue(chMap.DeviceAppId, out int carId))
                {
                    if (carRanges.TryGetValue(carId, out CarRange cr))
                    {
                        if (chMap.ReservedName == ReservedChannel.SPEED)
                        {
                            cr.SpeedChannel = chMap;
                        }
                        else if (chMap.ReservedName == ReservedChannel.FUEL_LEVEL)
                        {
                            cr.FuelLevelChannel = chMap;
                        }
                    }
                }
            }

            flagUpdateTimer = new Timer(DoUpdateFlags, null, TimeSpan.FromMilliseconds(100), flagUpdateInterval);
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

                // Udpate Fuel Range stats
                if (carNumberToIdMappings.TryGetValue(cl.Key.ToUpper(), out int carId))
                {
                    if (carRanges.TryGetValue(carId, out CarRange cr))
                    {
                        cr.UpdateWithRaceHero(cl.ToArray());

                        TryUpdateStints();
                    }
                }
            }
        }

        /// <summary>
        /// Processes telemetry used by fuel range calculations.
        /// </summary>
        /// <param name="telem"></param>
        public void UpdateTelemetry(ChannelDataSetDto telem)
        {
            if (deviceAppCarMappings.TryGetValue(telem.DeviceAppId, out int carId))
            {
                if (carRanges.TryGetValue(carId, out CarRange cr))
                {
                    cr.UpdateWithTelemetry(telem);

                    TryUpdateStints();
                }
            }
        }

        /// <summary>
        /// Pull any updates from the cars and save them without exceeding an interval.
        /// </summary>
        private void TryUpdateStints()
        {
            var diff = DateTime.Now - lastFrUpdate;
            if (diff >= frUpdateInterval)
            {
                var updates = new List<FuelRangeStint>();
                foreach (var cr in carRanges)
                {
                    var frs = cr.Value.StintDataToSave;
                    if (frs != null)
                    {
                        updates.Add(frs);
                        cr.Value.StintDataToSave = null;
                    }
                }

                if (updates.Any())
                {
                    fuelRangeContext.UpdateTeamStints(updates).Wait();
                }

                lastFrUpdate = DateTime.Now;
            }
        }

        private void DoUpdateFlags(object obj)
        {
            if (Monitor.TryEnter(flagUpdateTimer))
            {
                try
                {
                    var cache = cacheMuxer.GetDatabase();
                    var key = string.Format(Consts.EVENT_FLAGS, RhEventId);
                    var json = cache.StringGet(key);
                    if (!string.IsNullOrEmpty(json))
                    {
                        var flags = JsonConvert.DeserializeObject<List<EventFlag>>(json);
                        if (lastEventFlags.Count != flags.Count)
                        {
                            foreach (var car in carRanges.Values)
                            {
                                car.UpdateFlagState(flags);
                            }
                            lastEventFlags = flags;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Logger.Error(ex, "Error update flags");
                }
                finally
                {
                    Monitor.Exit(flagUpdateInterval);
                }
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
                if (flagUpdateTimer != null)
                {
                    try
                    {
                        flagUpdateTimer.Dispose();
                    }
                    catch { }
                    flagUpdateTimer = null;
                }
            }

            disposed = true;
        }

        #endregion
    }
}
