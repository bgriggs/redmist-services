using BigMission.RaceHeroSdk.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace BigMission.RaceHeroSimulator.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class EventsController : ControllerBase
    {
        private readonly ILogger<EventsController> _logger;

        public EventsController(ILogger<EventsController> logger)
        {
            _logger = logger;
        }

        [HttpGet("{eventId}")]
        public Event GetEvent(int eventId)
        {
            return Simulation.GetEvent();
        }

        [HttpGet("{eventId}/live/leaderboard")]
        public Leaderboard GetLeaderboard(int eventId)
        {
            return Simulation.GetLeaderboard();
        }
    }
}
