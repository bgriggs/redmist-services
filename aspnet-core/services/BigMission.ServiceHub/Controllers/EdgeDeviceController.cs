using BigMission.CommandTools;
using BigMission.Database;
using BigMission.Database.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace BigMission.ServiceHub.Controllers
{
    [Authorize(AuthenticationSchemes = "ApiKey")]
    [ApiController]
    [Route("[controller]/[action]")]
    public class EdgeDeviceController : ControllerBase
    {
        public NLog.ILogger Logger { get; }
        public IConfiguration Config { get; }

        public EdgeDeviceController(NLog.ILogger logger, IConfiguration config)
        {
            Logger = logger;
            Config = config;
        }


        [HttpGet(Name = nameof(DeviceCanAppConfiguration))]
        //[Route("[controller]/[action]")]
        public async Task<MasterConfiguration> DeviceCanAppConfiguration()
        {
            var nameClaim = User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier);
            if (nameClaim == null)
            {
                throw new UnauthorizedAccessException();
            }
            var token = nameClaim.Value.Remove(0, 7);
            var authData = KeyUtilities.DecodeToken(token);

            var master = new MasterConfiguration { DeviceAppKey = authData.appId };

            using var db = new RedMist(Config["ConnectionString"]);

            var deviceAppConfig = await db.DeviceAppConfigs.FirstOrDefaultAsync(d => d.DeviceAppKey == authData.appId);
            if (deviceAppConfig != null)
            {
                master.BaseConfig = await db.CanAppConfigs.FirstOrDefaultAsync(c => c.DeviceAppId == deviceAppConfig.Id);
                master.ChannelMappings = await db.ChannelMappings.Where(c => c.DeviceAppId == deviceAppConfig.Id).ToArrayAsync();
                master.FuelSettings = await db.FuelCarAppSettings.FirstOrDefaultAsync(f => f.DeviceAppId == deviceAppConfig.Id) ?? new FuelCarAppSetting();
                master.KeypadSettings = await db.KeypadCarAppConfigs.FirstOrDefaultAsync(k => k.DeviceAppId == deviceAppConfig.Id);
                if (deviceAppConfig.CarId.HasValue)
                {
                    master.TpmsSettings = await db.TpmsConfigs.FirstOrDefaultAsync(k => k.CarId == deviceAppConfig.CarId);
                }
                if (master.TpmsSettings == null)
                {
                    master.TpmsSettings = new TpmsConfig { Id = -1 };
                }

                if (master.KeypadSettings != null)
                {
                    var kpId = master.KeypadSettings.Id;

                    // Load with manual SQL since EF is failing on an error on column that is non-existant
                    using var conn = new SqlConnection(Config["ConnectionString"]);
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

        [HttpGet]
        public async Task<Guid> LatestDeviceCanAppCongifurationId(int deviceId)
        {
            using var db = new RedMist(Config["ConnectionString"]);
            var carConfig = await db.CanAppConfigs.FirstOrDefaultAsync(c => c.DeviceAppId == deviceId);
            if (carConfig != null)
            {
                return carConfig.ConfigurationId;
            }
            return Guid.Empty;
        }
    }
}