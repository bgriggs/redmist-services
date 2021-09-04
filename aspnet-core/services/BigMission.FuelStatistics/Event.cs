using BigMission.Cache.FuelRange;
using BigMission.DeviceApp.Shared;
using BigMission.FuelStatistics.FuelRange;
using BigMission.RaceManagement;
using BigMission.TestHelpers;
using NLog;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;

namespace BigMission.FuelStatistics
{
    /// <summary>
    /// Processes data for a race hero event including pit stops and car range.
    /// </summary>
    public class Event : IDisposable
    {
        private readonly RaceEventSettings settings;
        private readonly IDateTimeHelper dateTimeHelper;
        private ILogger Logger { get; }
        public int RhEventId { get; private set; }
        private Dictionary<string, Car> Cars { get; } = new Dictionary<string, Car>();
        private readonly Dictionary<int, CarRange> carRanges = new Dictionary<int, CarRange>();
        private readonly HashSet<int> dirtyCarRanges = new HashSet<int>();
        private readonly Dictionary<int, int> deviceAppCarMappings = new Dictionary<int, int>();
        private readonly Dictionary<string, int> carNumberToIdMappings = new Dictionary<string, int>();
        private readonly IDataContext dataContext;
        private readonly IFuelRangeContext fuelRangeContext;
        private readonly ITimerHelper fuelRangeUpdateTimer;
        private bool disposed;


        public Event(RaceEventSettings settings, ILogger logger, IDateTimeHelper dateTimeHelper, IDataContext dataContext, IFuelRangeContext fuelRangeContext, ITimerHelper fuelRangeUpdateTimer)
        {
            this.settings = settings;
            if (!int.TryParse(settings.RaceHeroEventId, out var id)) { throw new ArgumentException("rhEventId"); }
            RhEventId = id;
            Logger = logger;
            this.dateTimeHelper = dateTimeHelper;
            this.dataContext = dataContext;
            this.fuelRangeContext = fuelRangeContext;
            this.fuelRangeUpdateTimer = fuelRangeUpdateTimer;
        }


        /// <summary>
        /// Pull in any existing data for the event to reset on service restart or event change.
        /// </summary>
        public async Task Initialize()
        {
            carRanges.Clear();
            deviceAppCarMappings.Clear();
            carNumberToIdMappings.Clear();

            dataContext.StartBatch();
            try
            {
                // Load any saved laps from log for the event
                var laps = await dataContext.GetSavedLaps(RhEventId);
                await UpdateLap(laps);

                // Load range settings for event cars
                Logger.Info("Loading Fuel Range Settings...");
                var carIds = settings.GetCarIds();
                var carSettings = await dataContext.GetFuelRangeSettings(carIds);
                Logger.Info($"Loaded {carSettings.Count()} fuel range settings");
                foreach (var cs in carSettings)
                {
                    var carRange = new CarRange(cs, dateTimeHelper, fuelRangeContext, Logger);
                    carRanges[cs.CarId] = carRange;
                }

                // Load mappings to associate telemetry from device ID to Car ID
                Logger.Info("Loading device apps...");
                var deviceApps = await dataContext.GetDeviceAppConfig(carIds);
                Logger.Info($"Loaded {deviceApps.Count()} device apps");
                foreach (var da in deviceApps)
                {
                    deviceAppCarMappings[da.Id] = da.CarId.Value;
                }

                // Load Cars to be able to go from RH car number to a car ID
                Logger.Info("Loading cars...");
                var cars = await dataContext.GetCars(carIds);
                Logger.Info($"Loaded {cars.Count()} cars");
                foreach (var c in cars)
                {
                    carNumberToIdMappings[c.Number.ToUpper()] = c.Id;
                }

                // Load car's telemetry channel definitions
                var deviceAppIds = deviceAppCarMappings.Keys.ToArray();
                var channelNames = new[] { ReservedChannel.SPEED, ReservedChannel.FUEL_LEVEL };
                var channels = await dataContext.GetChannelMappings(deviceAppIds, channelNames);
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
            }
            finally
            {
                await dataContext.EndBatch();
            }
        }

        /// <summary>
        /// Update statistics using new race hero lap data.
        /// </summary>
        /// <param name="laps"></param>
        public async Task UpdateLap(List<Lap> laps)
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
                await dataContext.UpdateCarStatus(car, RhEventId);

                //Newtonsoft.Json.Serialization.ITraceWriter traceWriter = new Newtonsoft.Json.Serialization.MemoryTraceWriter();
                //var jss = new JsonSerializerSettings { TraceWriter = traceWriter, Converters = { new Newtonsoft.Json.Converters.JavaScriptDateTimeConverter() } };
                //var c = JsonConvert.DeserializeObject<CarBase>(carJson, jss);
                //Console.WriteLine(traceWriter);

                // Udpate Fuel Range stats
                if (carNumberToIdMappings.TryGetValue(cl.Key.ToUpper(), out int carId))
                {
                    if (carRanges.TryGetValue(carId, out CarRange cr))
                    {
                        var changed = await cr.ProcessLaps(cl.ToArray());
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
        public async Task UpdateTelemetry(ChannelDataSetDto telem)
        {
            if (deviceAppCarMappings.TryGetValue(telem.DeviceAppId, out int carId))
            {
                if (carRanges.TryGetValue(carId, out CarRange cr))
                {
                    var changed = await cr.ProcessTelemetery(telem);
                    if (changed)
                    {
                        SetDirty(carId);
                    }
                }
            }
        }

        public async Task OverrideStint(FuelRangeUpdate stint)
        {
            foreach (var car in carRanges.Values)
            {
                var dirty = await car.OverrideStint(stint);
                if (dirty)
                {
                    SetDirty(car.CarId);
                }
            }
        }

        private void SetDirty(int carId)
        {
            dirtyCarRanges.Add(carId);
        }

        private int[] FlushDirtyCarRanges()
        {
            var cids = dirtyCarRanges.ToArray();
            dirtyCarRanges.Clear();
            return cids;
        }

        /// <summary>
        /// Pull any updates from the cars and save them without exceeding an interval.
        /// </summary>
        public async Task CommitFuelRangeStintUpdates()
        {
            try
            {
                var sw = Stopwatch.StartNew();

                // Apply flags
                var flags = await dataContext.GetFlags(RhEventId);
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
                    await fuelRangeContext.UpdateTeamStints(eventStints);
                }

                Logger.Trace($"CommitFuelRangeStintUpdates for event {RhEventId} in {sw.ElapsedMilliseconds}ms");
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error saving fuel ranges");
            }
        }

        public Car[] GetCars()
        {
            return Cars.Values.ToArray();
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
                }
            }

            disposed = true;
        }

        #endregion
    }
}
