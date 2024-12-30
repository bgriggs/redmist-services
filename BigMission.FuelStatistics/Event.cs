using BigMission.Cache.Models.Flags;
using BigMission.Cache.Models.FuelRange;
using BigMission.Database.Models;
using BigMission.DeviceApp.Shared;
using BigMission.FuelStatistics.FuelConsumption;
using BigMission.FuelStatistics.FuelRange;
using BigMission.TestHelpers;
using System.Diagnostics;

namespace BigMission.FuelStatistics;

/// <summary>
/// Processes data for a race hero event including pit stops and car range.
/// </summary>
public class Event
{
    public const string SPEED = "Speed";
    public const string FUEL_LEVEL = "FuelLevel";
    private readonly RaceEventSetting settings;
    private readonly ILoggerFactory loggerFactory;
    private readonly IDateTimeHelper dateTimeHelper;
    private ILogger Logger { get; }
    public int RhEventId { get; private set; }
    private Dictionary<string, Car> Cars { get; } = new Dictionary<string, Car>();
    private readonly Dictionary<int, CarRange> carRanges = new();
    private readonly HashSet<int> dirtyCarRanges = new();
    private readonly Dictionary<int, int> deviceAppCarMappings = new();
    private readonly Dictionary<string, int> carNumberToIdMappings = new();
    private readonly IDataContext dataContext;
    private readonly IFuelRangeContext fuelRangeContext;
    private readonly IFlagContext flagContext;
    private readonly ConsumptionProcessor consumptionProcessor;


    public Event(RaceEventSetting settings, ILoggerFactory loggerFactory, IDateTimeHelper dateTimeHelper, IDataContext dataContext,
        IFuelRangeContext fuelRangeContext, IFlagContext flagContext)
    {
        this.settings = settings;
        this.loggerFactory = loggerFactory;
        if (!int.TryParse(settings.RaceHeroEventId, out var id)) { throw new ArgumentException("rhEventId"); }
        RhEventId = id;
        Logger = loggerFactory.CreateLogger(GetType().Name);
        this.dateTimeHelper = dateTimeHelper;
        this.dataContext = dataContext;
        this.fuelRangeContext = fuelRangeContext;
        this.flagContext = flagContext;

        consumptionProcessor = new ConsumptionProcessor(loggerFactory, dataContext, dateTimeHelper);
    }


    /// <summary>
    /// Pull in any existing data for the event to reset on service restart or event change.
    /// </summary>
    public async Task Initialize()
    {
        carRanges.Clear();
        deviceAppCarMappings.Clear();
        carNumberToIdMappings.Clear();

        await dataContext.StartBatch();
        try
        {
            // Load any saved laps from log for the event
            var laps = await dataContext.GetSavedLaps(RhEventId);
            await UpdateLap(laps);

            // Load range settings for event cars
            Logger.LogInformation("Loading Fuel Range Settings...");
            var carIds = settings.GetCarIds();
            var carSettings = await dataContext.GetFuelRangeSettings(carIds);
            Logger.LogInformation($"Loaded {carSettings.Count} fuel range settings");
            foreach (var cs in carSettings)
            {
                var carRange = new CarRange(cs, dateTimeHelper, fuelRangeContext, loggerFactory);
                carRanges[cs.CarId] = carRange;
            }

            // Load mappings to associate telemetry from device ID to Car ID
            Logger.LogInformation("Loading device apps...");
            var deviceApps = await dataContext.GetDeviceAppConfig(carIds);
            Logger.LogInformation($"Loaded {deviceApps.Count} device apps");
            foreach (var da in deviceApps)
            {
                if (da.CarId.HasValue)
                {
                    deviceAppCarMappings[da.Id] = da.CarId.Value;
                }
            }

            // Load Cars to be able to go from RH car number to a car ID
            Logger.LogInformation("Loading cars...");
            var cars = await dataContext.GetCars(carIds);
            Logger.LogInformation($"Loaded {cars.Count} cars");
            foreach (var c in cars)
            {
                carNumberToIdMappings[c.Number.ToUpper()] = c.Id;
            }

            // Load car's telemetry channel definitions
            var deviceAppIds = deviceAppCarMappings.Keys.ToArray();
            var channelNames = new[] { SPEED, FUEL_LEVEL };
            var channels = await dataContext.GetChannelMappings(deviceAppIds, channelNames);
            foreach (var chMap in channels)
            {
                if (deviceAppCarMappings.TryGetValue(chMap.DeviceAppId, out int carId))
                {
                    if (carRanges.TryGetValue(carId, out CarRange? cr))
                    {
                        if (chMap.ReservedName == SPEED)
                        {
                            cr.SpeedChannel = chMap;
                        }
                        else if (chMap.ReservedName == FUEL_LEVEL)
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
        var carLaps = laps.Where(l => l.CarNumber != null && l.ClassName != null).GroupBy(l => l.CarNumber!);
        foreach (var cl in carLaps)
        {
            if (!Cars.TryGetValue(cl.Key, out var car))
            {
                car = new Car(cl.Key, cl.First().ClassName!);
                Cars[cl.Key] = car;
            }

            // Check for an event/lap reset when new laps are less than what's tracked for the car.
            // This is typically when you have a multi-race event.
            if (laps.Any() && car.Laps.Count != 0)
            {
                var latestLap = laps.Max(l => l.CurrentLap);
                var carsLatest = car.Laps.Keys.Max();
                if (carsLatest > latestLap)
                {
                    car.Reset();
                }
            }

            car.AddLap([.. cl]);

            // Save car status
            await dataContext.UpdateCarStatus(car, RhEventId);

            //Newtonsoft.Json.Serialization.ITraceWriter traceWriter = new Newtonsoft.Json.Serialization.MemoryTraceWriter();
            //var jss = new JsonSerializerSettings { TraceWriter = traceWriter, Converters = { new Newtonsoft.Json.Converters.JavaScriptDateTimeConverter() } };
            //var c = JsonConvert.DeserializeObject<CarBase>(carJson, jss);
            //Console.WriteLine(traceWriter);

            // Update Fuel Range stats
            if (carNumberToIdMappings.TryGetValue(cl.Key.ToUpper(), out int carId))
            {
                if (carRanges.TryGetValue(carId, out CarRange? cr))
                {
                    var changed = await cr.ProcessLaps([.. cl]);
                    if (changed)
                    {
                        SetDirty(carId);
                    }
                }

                // Update directly measured fuel consumption
                await consumptionProcessor.UpdateLaps([.. cl], carId);
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
            if (carRanges.TryGetValue(carId, out CarRange? cr))
            {
                var changed = await cr.ProcessTelemetry(telem);
                if (changed)
                {
                    SetDirty(carId);
                }

                // Update directly measured fuel consumption
                if (cr.FuelLevelChannel != null)
                {
                    consumptionProcessor.UpdateTelemetry(telem, cr.CarId, cr.FuelLevelChannel);
                }
                else
                {
                    Logger.LogDebug($"No fuel level channel for car {cr.CarId}");
                }
            }
        }
    }

    public async Task OverrideStint(RangeUpdate stint)
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

            // Apply flags and update calculated values
            var flags = await flagContext.GetFlags(RhEventId);
            foreach (var car in carRanges.Values)
            {
                var changed = car.ApplyEventFlags(flags);
                changed |= car.Refresh();
                if (changed)
                {
                    SetDirty(car.CarId);
                }
            }

            // Get updated cars
            var eventStints = new List<Cache.Models.FuelRange.Stint>();
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

            Logger.LogTrace($"CommitFuelRangeStintUpdates for event {RhEventId} in {sw.ElapsedMilliseconds}ms");
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error saving fuel ranges");
        }
    }

    public Car[] GetCars()
    {
        return [.. Cars.Values];
    }

    /// <summary>
    /// Update direct fuel flow meter consumption and range timestamps for the event's cars.
    /// </summary>
    public async Task PublishDirectFuelConsumptionRanges()
    {
        await consumptionProcessor.PublishConsumptionRanges();
    }
}
