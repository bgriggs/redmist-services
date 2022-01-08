using Microsoft.AspNetCore.Mvc;

namespace BigMission.ServiceHub.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class EdgeDeviceController : ControllerBase
    {
        private readonly ILogger<EdgeDeviceController> _logger;

        public EdgeDeviceController(ILogger<EdgeDeviceController> logger)
        {
            _logger = logger;
        }

        [HttpGet(Name = "GetWeatherForecast")]
        public string Get()
        {
            return "test";
        }
    }
}