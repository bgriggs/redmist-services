using BigMission.RaceHeroSdk.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using static BigMission.RaceHeroSdk.RaceHeroClient;

namespace BigMission.RaceHeroSimulator
{
    public static class Simulation
    {
        public const int SPEED = 1;
        private static readonly EventScript eventScript = new EventScript();
        private static readonly LeaderboardScript leaderboardScript = new LeaderboardScript();
        private static readonly Dictionary<string, Racer> racers = new Dictionary<string, Racer>();
        private static DateTime startTime;
        private static TimeSpan simOffset;
        private static Timer timer;

        static Simulation()
        {
            startTime = DateTime.Now;

            var speedMs = 1000 / SPEED;

            timer = new Timer(DoAdvanceSim, null, speedMs, speedMs);
        }

        private static void DoAdvanceSim(object obj)
        {
            simOffset += TimeSpan.FromSeconds(1);

            var rs = leaderboardScript.GetRacers(simOffset);
            foreach (var r in rs)
            {
                racers[r.RacerNumber] = r;
            }
        }

        public static DateTime GetSimTime()
        {
            return startTime + simOffset;
        }

        public static Event GetEvent()
        {
            for (int i = 0; i < eventScript.Rows.Length; i++)
            {
                var curr = eventScript.Rows[i];
                if (i + 1 < eventScript.Rows.Length)
                {
                    var next = eventScript.Rows[i + 1];

                    if (simOffset >= curr.TimeOffset && simOffset < next.TimeOffset)
                    {
                        return curr.Event;
                    }
                }
                else
                {
                    return curr.Event;
                }
            }
            throw new NotSupportedException();
        }

        public static Flag GetFlag()
        {
            for (int i = 0; i < leaderboardScript.FlagRows.Length; i++)
            {
                var curr = leaderboardScript.FlagRows[i];
                if (i + 1 < leaderboardScript.FlagRows.Length)
                {
                    var next = leaderboardScript.FlagRows[i + 1];

                    if (simOffset >= curr.TimeOffset && simOffset < next.TimeOffset)
                    {
                        return curr.Flag;
                    }
                }
                else
                {
                    return curr.Flag;
                }
            }

            throw new NotSupportedException();
        }

        public static Leaderboard GetLeaderboard()
        {
            var lap = racers.Values.Select(r => r.CurrentLap).DefaultIfEmpty(0).Max();
            return new Leaderboard
            {
                CurrentFlag = GetFlag().ToString(),
                StartedAt = startTime.ToString(),
                Racers = racers.Values.ToList(),
                CurrentLap = lap
            };
        }
    }
}
