using BigMission.Cache;
using BigMission.Cache.Models;
using BigMission.DeviceApp.Shared;
using BigMission.EntityFrameworkCore;
using BigMission.FuelStatistics.FuelRange;
using BigMission.RaceManagement;
using BigMission.TestHelpers;
using Newtonsoft.Json;
using NLog;
using StackExchange.Redis;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace BigMission.FuelStatistics
{
    /// <summary>
    /// Processes data for a race hero event including pit stops and car range.
    /// </summary>
    class Event : IDisposable
    {
        private readonly RaceEventSettings settings;
        private readonly IDateTimeHelper dateTimeHelper;
        private ILogger Logger { get; }
        public int RhEventId { get; private set; }
        public Dictionary<string, Car> Cars { get; } = new Dictionary<string, Car>();
        private readonly Dictionary<int, CarRange> carRanges = new Dictionary<int, CarRange>();
        private readonly HashSet<int> dirtyCarRanges = new HashSet<int>();
        private readonly Dictionary<int, int> deviceAppCarMappings = new Dictionary<int, int>();
        private readonly Dictionary<string, int> carNumberToIdMappings = new Dictionary<string, int>();
        private readonly ConnectionMultiplexer cacheMuxer;
        private readonly string dbConnStr;
        private FuelRangeContext fuelRangeContext;
        private readonly TimeSpan frUpdateInterval = TimeSpan.FromSeconds(1);
        private Timer fuelRangeUpdateTimer;
        private DateTime lastStintTimestamp;
        private bool disposed;


        public Event(RaceEventSettings settings, ConnectionMultiplexer cacheMuxer, string dbConnStr, ILogger logger, IDateTimeHelper dateTimeHelper)
        {
            this.settings = settings;
            if (!int.TryParse(settings.RaceHeroEventId, out var id)) { throw new ArgumentException("rhEventId"); }
            RhEventId = id;
            this.cacheMuxer = cacheMuxer;
            this.dbConnStr = dbConnStr;
            Logger = logger;
            this.dateTimeHelper = dateTimeHelper;
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
            int latestRunId = 0;
            var runIds = db.CarRacerLaps.Where(l => l.EventId == RhEventId).Select(p => p.RunId);
            if (runIds.Any())
            {
                latestRunId = runIds.Max();
            }

            var laps = db.CarRacerLaps
                .Where(l => l.EventId == RhEventId && l.RunId == latestRunId)
                .Select(l => new Lap
                {
                    EventId = RhEventId,
                    RunId = l.RunId,
                    CarNumber = l.CarNumber,
                    Timestamp = l.Timestamp,
                    ClassName = l.ClassName,
                    PositionInRun = l.PositionInRun,
                    CurrentLap = l.CurrentLap,
                    LastLapTimeSeconds = l.LastLapTimeSeconds,
                    LastPitLap = l.LastPitLap,
                    PitStops = l.PitStops,
                    Flag = l.Flag
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
                var carRange = new CarRange(cs, dateTimeHelper);
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

            fuelRangeUpdateTimer = new Timer(DoUpdateFuelRangeStints, null, TimeSpan.FromMilliseconds(500), frUpdateInterval);
        }

        /// <summary>
        /// Update statistics using new race hero lap data.
        /// </summary>
        /// <param name="laps"></param>
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
                        var changed = cr.ProcessLaps(cl.ToArray());
                        if (changed)
                        {
                            SetDirty(carId);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Processes car's telemetry used by fuel range calculations.
        /// </summary>
        /// <param name="telem"></param>
        public void UpdateTelemetry(ChannelDataSetDto telem)
        {
            if (deviceAppCarMappings.TryGetValue(telem.DeviceAppId, out int carId))
            {
                if (carRanges.TryGetValue(carId, out CarRange cr))
                {
                    var changed = cr.ProcessTelemetery(telem);
                    if (changed)
                    {
                        SetDirty(carId);
                    }
                }
            }
        }

        private void SetDirty(int carId)
        {
            lock (dirtyCarRanges)
            {
                dirtyCarRanges.Add(carId);
            }
        }

        private int[] FlushDirtyCarRanges()
        {
            lock (dirtyCarRanges)
            {
                var cids = dirtyCarRanges.ToArray();
                dirtyCarRanges.Clear();
                return cids;
            }
        }

        /// <summary>
        /// Pull any updates from the cars and save them without exceeding an interval.
        /// </summary>
        private void DoUpdateFuelRangeStints(object obj = null)
        {
            if (Monitor.TryEnter(fuelRangeUpdateTimer))
            {
                try
                {
                    var cache = cacheMuxer.GetDatabase();

                    // Check for user DB udpates
                    var lastts = CheckReload(cache);
                    if (lastts != null && lastStintTimestamp != lastts)
                    {
                        var stints = LoadTeamStints(settings.TenantId, RhEventId);
                        foreach (var car in carRanges.Values)
                        {
                            car.MergeStintUserChanges(stints);
                        }

                        lastStintTimestamp = lastts.Value;
                    }

                    // Apply flags
                    var flags = GetFlags(cache);
                    foreach (var car in carRanges.Values)
                    {
                        car.ApplyEventFlags(flags);
                    }

                    // Get updated cars
                    var eventStints = new List<FuelRangeStint>();
                    var carIdsToUpdate = FlushDirtyCarRanges();
                    if (carIdsToUpdate.Any())
                    {
                        foreach (var car in carRanges.Values)
                        {
                            var carStints = car.GetStints();
                            eventStints.AddRange(carStints);
                        }
                        fuelRangeContext.UpdateTeamStints(eventStints).Wait();
                    }
                }
                catch (Exception ex)
                {
                    Logger.Error(ex, "Error saving fuel ranges");
                }
                finally
                {
                    Monitor.Exit(fuelRangeUpdateTimer);
                }
            }
            else
            {
                Logger.Info("Skipping DoUpdateFuelRangeStints");
            }
        }

        private List<EventFlag> GetFlags(IDatabase cache)
        {
            var flags = new List<EventFlag>();
            var key = string.Format(Consts.EVENT_FLAGS, RhEventId);
            var json = cache.StringGet(key);
            if (!string.IsNullOrEmpty(json))
            {
                var f = JsonConvert.DeserializeObject<List<EventFlag>>(json);
                if (f != null)
                {
                    flags = f;
                }
            }
            return flags;
        }

        private DateTime? CheckReload(IDatabase cache)
        {
            var key = string.Format(Consts.FUEL_RANGE_STATUS_UPDATED, settings.TenantId);
            var dtstr = cache.StringGet(key);
            if (!string.IsNullOrEmpty(dtstr))
            {
                return DateTime.Parse(dtstr).ToUniversalTime();
            }
            return null;
        }

        private List<FuelRangeStint> LoadTeamStints(int teamId, int eventId)
        {
            var cf = new BigMissionDbContextFactory();
            var db = cf.CreateDbContext(new[] { dbConnStr });
            using (db)
            {
                var latestRunId = db.FuelRangeStints.Where(s => s.TenantId == teamId && s.EventId == eventId).Select(p => p.RunId).DefaultIfEmpty(0).Max();
                return db.FuelRangeStints.Where(s => s.TenantId == teamId && s.EventId == eventId && s.RunId == latestRunId).ToList();
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
                if (fuelRangeUpdateTimer != null)
                {
                    try
                    {
                        fuelRangeUpdateTimer.Dispose();
                    }
                    catch { }
                    fuelRangeUpdateTimer = null;
                }
            }

            disposed = true;
        }

        #endregion
    }
}
