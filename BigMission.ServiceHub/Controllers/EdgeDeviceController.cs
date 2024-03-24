using Amazon.S3;
using BigMission.CommandTools;
using BigMission.Database;
using BigMission.Database.Models;
using BigMission.ServiceHub.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using System.Reflection;
using System.Security.Claims;

namespace BigMission.ServiceHub.Controllers;

[Authorize(AuthenticationSchemes = "ApiKey")]
[ApiController]
[Route("[controller]/[action]")]
public class EdgeDeviceController : ControllerBase
{
    private readonly IDbContextFactory<RedMist> dbFactory;

    public ILogger Logger { get; }
    public IConfiguration Config { get; }

    public EdgeDeviceController(ILoggerFactory loggerFactory, IConfiguration config, IDbContextFactory<RedMist> dbFactory)
    {
        Logger = loggerFactory.CreateLogger(GetType().Name);
        Config = config;
        this.dbFactory = dbFactory;
    }

    private async Task<MasterConfiguration> DeviceCanAppConfiguration(Guid appKey)
    {
        var master = new MasterConfiguration { DeviceAppKey = appKey };

        using var db = await dbFactory.CreateDbContextAsync();

        var deviceAppConfig = await db.DeviceAppConfigs.FirstOrDefaultAsync(d => d.DeviceAppKey == appKey);
        if (deviceAppConfig != null)
        {
            master.BaseConfig = await db.CanAppConfigs.FirstOrDefaultAsync(c => c.DeviceAppId == deviceAppConfig.Id);
            master.ChannelMappings = await db.ChannelMappings.Where(c => c.DeviceAppId == deviceAppConfig.Id).ToArrayAsync();
            master.FuelSettings = await db.FuelCarAppSettings.FirstOrDefaultAsync(f => f.DeviceAppId == deviceAppConfig.Id) ?? new FuelCarAppSetting();
            master.KeypadSettings = await db.KeypadCarAppConfigs.FirstOrDefaultAsync(k => k.DeviceAppId == deviceAppConfig.Id);
            if (deviceAppConfig.CarId.HasValue)
            {
                master.Car = await db.Cars.FirstOrDefaultAsync(c => c.Id == deviceAppConfig.CarId);
                master.TpmsSettings = await db.TpmsConfigs.FirstOrDefaultAsync(k => k.CarId == deviceAppConfig.CarId);
                master.RaceHeroSetting = await db.RaceHeroSettings.FirstOrDefaultAsync();
                master.RaceEventSetting = await db.RaceEventSettings.FirstOrDefaultAsync();
                master.EcuFuelCalcConfig = await db.EcuFuelCalcConfigs.FirstOrDefaultAsync(k => k.CarId == deviceAppConfig.CarId);
                master.UdpTelemetryConfig = await db.UdpTelemetryConfigs.FirstOrDefaultAsync(k => k.CarId == deviceAppConfig.CarId);
            }
            master.TpmsSettings ??= new TpmsConfig { Id = -1 };
            master.EcuFuelCalcConfig ??= new EcuFuelCalcConfig { Id = -1 };
            master.UdpTelemetryConfig ??= new UdpTelemetryConfig { Id = -1 };

            if (master.KeypadSettings != null)
            {
                var kpId = master.KeypadSettings.Id;

                // Load with manual SQL since EF is failing on an error on column that is non-existent
                using var conn = new SqlConnection(Config["ConnectionStrings:Default"]);
                await conn.OpenAsync();
                var cmd = new SqlCommand($"SELECT CanId,Offset,Length,Value,ButtonNumber,LedNumber,Blink FROM KeypadCarAppCanStateRules WHERE KeypadId={kpId}", conn);
                var crs = new List<KeypadCarAppCanStateRule>();
                using var creader = await cmd.ExecuteReaderAsync();
                while (creader.Read())
                {
                    crs.Add(new KeypadCarAppCanStateRule
                    {
                        KeypadId = kpId,
                        CanId = creader.GetInt32(0),
                        Offset = creader.GetInt32(1),
                        Length = creader.GetInt32(2),
                        Value = creader.GetInt32(3),
                        ButtonNumber = creader.GetInt32(4),
                        LedNumber = creader.GetInt32(5),
                        Blink = creader.GetInt32(6),
                    });
                }
                await creader.CloseAsync();

                master.KeypadSettings.CanStateRules = crs.ToArray();

                cmd = new SqlCommand($"SELECT ButtonNumber,LedNumber,Blink FROM KeypadCarAppMomentaryButtonRules WHERE KeypadId={kpId}", conn);
                var mrs = new List<KeypadCarAppMomentaryButtonRule>();
                using var mreader = await cmd.ExecuteReaderAsync();
                while (mreader.Read())
                {
                    mrs.Add(new KeypadCarAppMomentaryButtonRule
                    {
                        KeypadId = kpId,
                        ButtonNumber = mreader.GetInt32(0),
                        LedNumber = mreader.GetInt32(1),
                        Blink = mreader.GetInt32(2),
                    });
                }
                await mreader.CloseAsync();
                master.KeypadSettings.MomentaryButtonRules = mrs.ToArray();

                //master.KeypadSettings.MomentaryButtonRules = await db2.KeypadCarAppMomentaryButtonRules.Where(k => k.KeypadId == kpId).ToArrayAsync();
                //master.KeypadSettings.CanStateRules = await db2.KeypadCarAppCanStateRules.Where(k => k.KeypadId == kpId).ToArrayAsync();
            }
            else
            {
                master.KeypadSettings = new KeypadCarAppConfig();
            }
        }

        return master;
    }

    [HttpGet(Name = nameof(DeviceCanAppConfiguration))]
    public async Task<MasterConfiguration> DeviceCanAppConfiguration()
    {
        var nameClaim = User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier);
        if (nameClaim == null)
        {
            throw new UnauthorizedAccessException();
        }
        var token = nameClaim.Value.Remove(0, 7);
        var authData = KeyUtilities.DecodeToken(token);

        return await DeviceCanAppConfiguration(authData.appId);
    }

    [HttpGet]
    public async Task<Guid> LatestDeviceCanAppCongifurationId(int deviceId)
    {
        using var db = await dbFactory.CreateDbContextAsync();
        var carConfig = await db.CanAppConfigs.FirstOrDefaultAsync(c => c.DeviceAppId == deviceId);
        if (carConfig != null)
        {
            return carConfig.ConfigurationId;
        }
        return Guid.Empty;
    }

    [HttpGet]
    public async Task<IActionResult> GetCarServiceUpdate(string filename)
    {
        var currentDir = Directory.GetParent(Assembly.GetExecutingAssembly().Location);
        var releaseCache = $"{currentDir}{Path.DirectorySeparatorChar}CarServiceVersions";
        var localFilePath = $"{releaseCache}{Path.DirectorySeparatorChar}{filename}";

        // See if file is available on local disk already
        if (!System.IO.File.Exists(localFilePath))
        {
            // Pull from digital ocean S3 bucket
            var endpointUrl = Config["DIGITALOCEAN_ENDPOINTURL"];
            var releaseBucket = Config["DIGITALOCEAN_RELEASEBUCKET"];
            var keyId = Config["DIGITALOCEAN_KEYID"];
            var keySecret = Config["DIGITALOCEAN_KEYSECRET"];

            using IAmazonS3 client = new AmazonS3Client(keyId, keySecret, new AmazonS3Config { ServiceURL = endpointUrl });

            var doObj = await client.GetObjectAsync(releaseBucket, filename);

            if (!Directory.Exists(releaseCache))
            {
                Directory.CreateDirectory(releaseCache);
            }

            using var fs = new FileStream(localFilePath, FileMode.Create);
            await doObj.ResponseStream.CopyToAsync(fs);
            fs.Close();
        }

        var dataBytes = await System.IO.File.ReadAllBytesAsync(localFilePath);
        return File(dataBytes, "application/zip", filename);
    }


    // Methods for desktop application...
    // Change to user auth instead of device auth at future date
    [HttpGet]
    public async Task<DeviceAppIds[]> GetTeamDeviceIds(int teamId)
    {
        using var db = await dbFactory.CreateDbContextAsync();
        var teamDevices = await db.DeviceAppConfigs.Where(t => t.TenantId == teamId && !t.IsDeleted).ToArrayAsync();
        return teamDevices.Select(t => new DeviceAppIds { AppKey = t.DeviceAppKey, AppId = t.Id, CarId = t.CarId }).ToArray();
    }

    [HttpGet]
    public async Task<MasterConfiguration> GetDesktopDeviceCanAppConfiguration(Guid appKey)
    {
        return await DeviceCanAppConfiguration(appKey);
    }
}