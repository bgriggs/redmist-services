using BigMission.CommandTools;
using BigMission.Database;
using BigMission.Database.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace BigMission.ServiceHub.Controllers
{
    [Authorize(AuthenticationSchemes = "ApiKey")]
    [ApiController]
    [Route("[controller]")]
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
        public MasterConfiguration DeviceCanAppConfiguration()
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

            var deviceAppConfig = db.DeviceAppConfigs.FirstOrDefault(d => d.DeviceAppKey == authData.appId);
            if (deviceAppConfig != null)
            {
                master.BaseConfig = db.CanAppConfigs.FirstOrDefault(c => c.DeviceAppId == deviceAppConfig.Id);
                master.ChannelMappings = db.ChannelMappings.Where(c => c.DeviceAppId == deviceAppConfig.Id)?.ToArray();
                master.FuelSettings = db.FuelCarAppSettings.FirstOrDefault(f => f.DeviceAppId == deviceAppConfig.Id) ?? new FuelCarAppSetting();
                master.KeypadSettings = db.KeypadCarAppConfigs.FirstOrDefault(k => k.DeviceAppId == deviceAppConfig.Id);
                if (master.KeypadSettings != null)
                {
                    master.KeypadSettings.MomentaryButtonRules = db.KeypadCarAppMomentaryButtonRules.Where(k => k.KeypadId == master.KeypadSettings.Id)?.ToArray();
                    master.KeypadSettings.CanStateRules = db.KeypadCarAppCanStateRules.Where(k => master.KeypadSettings.Id == k.KeypadId)?.ToArray();
                }
                else
                {
                    master.KeypadSettings = new KeypadCarAppConfig();
                }
            }

            return master;
        }


    }
}