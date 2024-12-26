using BigMission.Backend.Shared.Models;
using BigMission.Database;
using BigMission.Database.V2;
using BigMission.Database.V2.Models.UI.Channels.CarStatusTable;
using BigMission.TestHelpers;
using BigMission.UI.Channels.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using StackExchange.Redis;

namespace BigMission.UI.Channels.Controllers;

[ApiController]
[Route("[controller]/[action]")]
[Authorize]
public class CarStatusController : Controller
{
    private readonly IDbContextFactory<RedMist> rmFactory;
    private readonly IDbContextFactory<ContextV2> v2Factory;
    private readonly IConnectionMultiplexer cache;

    private ILogger Logger { get; }
    public IDateTimeHelper DateTime { get; }

    public CarStatusController(ILoggerFactory loggerFactory, IDbContextFactory<RedMist> rmFactory, IDbContextFactory<ContextV2> v2Factory, IDateTimeHelper dateTime, IConnectionMultiplexer cache)
    {
        Logger = loggerFactory.CreateLogger(GetType().Name);
        this.rmFactory = rmFactory;
        this.v2Factory = v2Factory;
        DateTime = dateTime;
        this.cache = cache;
    }

    [HttpGet]
    [ProducesResponseType<Configuration>(StatusCodes.Status200OK)]
    public async Task<ActionResult<Configuration?>> LoadConfiguration()
    {
        using var context = await v2Factory.CreateDbContextAsync();
        var config = await context.CarStatusTableConfiguration
            .Include(c => c.ColumnOverrides)
            .Include(c => c.Columns)
            .FirstOrDefaultAsync();
        return Ok(config);
    }

    [HttpPost]
    [Authorize(Roles = "administrator,contributor")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult> SaveConfiguration(Configuration config)
    {
        using var context = await v2Factory.CreateDbContextAsync();
        config.LastUpdated = DateTime.UtcNow;
        config.Version++;
        context.CarStatusTableConfiguration.Update(config);
        await context.SaveChangesAsync();
        return Ok();
    }

    [HttpPost]
    [ProducesResponseType<CarStatusSettings>(StatusCodes.Status200OK)]
    public async Task<ActionResult> LoadActiveStatusSettings()
    {
        var settings = new CarStatusSettings();

        using var contextV2 = await v2Factory.CreateDbContextAsync();
        settings.TableConfiguration = await contextV2.CarStatusTableConfiguration
            .Include(c => c.ColumnOverrides)
            .Include(c => c.Columns)
            .FirstOrDefaultAsync() ?? new Configuration();

        // Load Active Cars
        using var rmContext = await rmFactory.CreateDbContextAsync();
        var eventSettings = await rmContext.RaceEventSettings.FirstOrDefaultAsync();
        if (eventSettings != null)
        {
            var carIds = eventSettings.GetCarIds();
            if (carIds.Length > 0)
            {
                settings.Cars = await rmContext.Cars
                    .Where(c => !c.IsDeleted && carIds.Contains(c.Id))
                    .ToListAsync();

                var channels = await rmContext.ChannelMappings
                .Join(rmContext.DeviceAppConfigs, map => map.DeviceAppId, device => device.Id, (m, c) => new { m.Id, m.ChannelName, DeviceId = c.Id, c.CarId })
                .Join(rmContext.Cars, c => c.CarId, car => car.Id, (mapDev, car) => new { mapDev, car })
                .Where(c => !c.car.IsDeleted && carIds.Contains(c.car.Id))
                .Select(c => new ChannelDefinition
                {
                    ChannelId = c.mapDev.Id,
                    ChannelName = c.mapDev.ChannelName,
                    DeviceAppId = c.mapDev.DeviceId,
                    CarId = c.car.Id,
                    CarNumber = c.car.Number
                }).ToListAsync();

                settings.Channels = channels;
            }
        }

        return Ok(settings);
    }

    [HttpPost]
    [ProducesResponseType<CarConnectionStatus[]>(StatusCodes.Status200OK)]
    public async Task<ActionResult<CarConnectionStatus[]>> GetCarConnectionStatus()
    {
        var status = new List<CarConnectionStatus>();
        var db = cache.GetDatabase();
        var hashResult = await db.HashGetAllAsync(CarConnectionCacheConst.CAR_STATUS);
        foreach (var ccsJson in hashResult)
        {
            try
            {
                if (ccsJson.Value.HasValue)
                {
                    var carConnectionStatus = JsonConvert.DeserializeObject<CarConnectionStatus>(ccsJson.Value!);
                    if (carConnectionStatus != null)
                    {
                        status.Add(carConnectionStatus);
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, $"Error getting car connection status for carId: {ccsJson.Name}");
            }
        }
        return Ok(status);
    }
}
