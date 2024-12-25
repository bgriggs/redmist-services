using BigMission.Database;
using BigMission.Database.Models;
using BigMission.UI.Channels.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BigMission.UI.Channels.Controllers;

[ApiController]
[Route("[controller]/[action]")]
[Authorize]
public class ChannelSettingsController : Controller
{
    private readonly IDbContextFactory<RedMist> rmFactory;
    private ILogger Logger { get; }


    public ChannelSettingsController(ILoggerFactory loggerFactory, IDbContextFactory<RedMist> rmFactory)
    {
        Logger = loggerFactory.CreateLogger(GetType().Name);
        this.rmFactory = rmFactory;
    }

    [HttpGet]
    public async Task<ActionResult<List<ChannelDefinition>>> LoadChannelDefinitions()
    {
        using var db = await rmFactory.CreateDbContextAsync();
        var channels = await db.ChannelMappings
            .Join(db.DeviceAppConfigs, map => map.DeviceAppId, device => device.Id, (m, c) => new { m.Id, m.ChannelName, DeviceId = c.Id, c.CarId })
            .Join(db.Cars, c => c.CarId, car => car.Id, (mapDev, car) => new { mapDev, car })
            .Where(c => !c.car.IsDeleted)
            .Select(c => new ChannelDefinition
            {
                ChannelId = c.mapDev.Id,
                ChannelName = c.mapDev.ChannelName,
                DeviceAppId = c.mapDev.DeviceId,
                CarId = c.car.Id,
                CarNumber = c.car.Number
            }).ToListAsync();

        return Ok(channels);
    }

    [HttpGet]
    public async Task<ActionResult<List<Car>>> LoadCars()
    {
        using var db = await rmFactory.CreateDbContextAsync();
        var cars = await db.Cars.Where(c => !c.IsDeleted).ToListAsync();
        return Ok(cars);
    }
}
