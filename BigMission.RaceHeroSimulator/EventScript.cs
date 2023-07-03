using BigMission.RaceHeroSdk.Models;
using System;

namespace BigMission.RaceHeroSimulator
{
    public class EventScript
    {
        public EventRow[] Rows = new[]
        {
            new EventRow { TimeOffset = TimeSpan.Zero, Event = new Event{ Id = 3134, IsLive = false } },
            new EventRow { TimeOffset = TimeSpan.FromMinutes(1), Event = new Event{ Id = 3134, IsLive = true } },
            new EventRow { TimeOffset = TimeSpan.FromHours(1), Event = new Event{ Id = 3134, IsLive = false } },
        };
    }
}
