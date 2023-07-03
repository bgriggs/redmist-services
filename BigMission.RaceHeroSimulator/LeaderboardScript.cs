using BigMission.RaceHeroSdk.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using static BigMission.RaceHeroSdk.RaceHeroClient;

namespace BigMission.RaceHeroSimulator
{
    public class LeaderboardScript
    {
        public RacerRow[] Rows = new[]
        {
            new RacerRow { TimeOffset = TimeSpan.FromSeconds(50), Racer = new Racer { RacerNumber = "777C", RacerClassName = "GP1", CurrentLap = 1, LastLapTimeSeconds = 30, PositionInRun = 1, LastPitLap = 0 } },
            new RacerRow { TimeOffset = TimeSpan.FromSeconds(51), Racer = new Racer { RacerNumber = "43A", RacerClassName = "GP2", CurrentLap = 1, LastLapTimeSeconds = 31, PositionInRun = 2, LastPitLap = 0 } },
            new RacerRow { TimeOffset = TimeSpan.FromSeconds(52), Racer = new Racer { RacerNumber = "336", RacerClassName = "GP1", CurrentLap = 1, LastLapTimeSeconds = 32, PositionInRun = 3, LastPitLap = 0 } },

            new RacerRow { TimeOffset = TimeSpan.FromSeconds(30), Racer = new Racer { RacerNumber = "777C", RacerClassName = "GP1", CurrentLap = 2, LastLapTimeSeconds = 30, PositionInRun = 1, LastPitLap = 0 } },
            new RacerRow { TimeOffset = TimeSpan.FromSeconds(31), Racer = new Racer { RacerNumber = "43A", RacerClassName = "GP2", CurrentLap = 2, LastLapTimeSeconds = 31, PositionInRun = 2, LastPitLap = 0 } },
            new RacerRow { TimeOffset = TimeSpan.FromSeconds(32), Racer = new Racer { RacerNumber = "336", RacerClassName = "GP1", CurrentLap = 2, LastLapTimeSeconds = 32, PositionInRun = 3, LastPitLap = 0 } },

            new RacerRow { TimeOffset = TimeSpan.FromSeconds(31), Racer = new Racer { RacerNumber = "43A", RacerClassName = "GP2", CurrentLap = 3, LastLapTimeSeconds = 31, PositionInRun = 1, LastPitLap = 0 } },
            new RacerRow { TimeOffset = TimeSpan.FromSeconds(32), Racer = new Racer { RacerNumber = "777C", RacerClassName = "GP1", CurrentLap = 3, LastLapTimeSeconds = 32, PositionInRun = 2, LastPitLap = 0 } },
            new RacerRow { TimeOffset = TimeSpan.FromSeconds(33), Racer = new Racer { RacerNumber = "336", RacerClassName = "GP1", CurrentLap = 3, LastLapTimeSeconds = 33, PositionInRun = 3, LastPitLap = 0 } },

            new RacerRow { TimeOffset = TimeSpan.FromSeconds(30), Racer = new Racer { RacerNumber = "43A", RacerClassName = "GP2", CurrentLap = 4, LastLapTimeSeconds = 30, PositionInRun = 1, LastPitLap = 0 } },
            new RacerRow { TimeOffset = TimeSpan.FromSeconds(31), Racer = new Racer { RacerNumber = "336", RacerClassName = "GP1", CurrentLap = 4, LastLapTimeSeconds = 31, PositionInRun = 2, LastPitLap = 0 } },
            new RacerRow { TimeOffset = TimeSpan.FromSeconds(32), Racer = new Racer { RacerNumber = "777C", RacerClassName = "GP1", CurrentLap = 4, LastLapTimeSeconds = 32, PositionInRun = 3, LastPitLap = 0 } },

            new RacerRow { TimeOffset = TimeSpan.FromSeconds(29), Racer = new Racer { RacerNumber = "336", RacerClassName = "GP1", CurrentLap = 5, LastLapTimeSeconds = 29, PositionInRun = 1, LastPitLap = 0 } },
            new RacerRow { TimeOffset = TimeSpan.FromSeconds(31), Racer = new Racer { RacerNumber = "43A", RacerClassName = "GP2", CurrentLap = 5, LastLapTimeSeconds = 31, PositionInRun = 2, LastPitLap = 0 } },
            new RacerRow { TimeOffset = TimeSpan.FromSeconds(33), Racer = new Racer { RacerNumber = "777C", RacerClassName = "GP1", CurrentLap = 5, LastLapTimeSeconds = 33, PositionInRun = 3, LastPitLap = 0 } },

            new RacerRow { TimeOffset = TimeSpan.FromSeconds(29), Racer = new Racer { RacerNumber = "336", RacerClassName = "GP1", CurrentLap = 6, LastLapTimeSeconds = 29, PositionInRun = 1, LastPitLap = 6 } },
            new RacerRow { TimeOffset = TimeSpan.FromSeconds(31), Racer = new Racer { RacerNumber = "43A", RacerClassName = "GP2", CurrentLap = 6, LastLapTimeSeconds = 31, PositionInRun = 2, LastPitLap = 0 } },
            new RacerRow { TimeOffset = TimeSpan.FromSeconds(33), Racer = new Racer { RacerNumber = "777C", RacerClassName = "GP1", CurrentLap = 6, LastLapTimeSeconds = 33, PositionInRun = 3, LastPitLap = 0 } },

            new RacerRow { TimeOffset = TimeSpan.FromSeconds(31), Racer = new Racer { RacerNumber = "43A", RacerClassName = "GP2", CurrentLap = 7, LastLapTimeSeconds = 31, PositionInRun = 1, LastPitLap = 7 } },
            new RacerRow { TimeOffset = TimeSpan.FromSeconds(33), Racer = new Racer { RacerNumber = "777C", RacerClassName = "GP1", CurrentLap = 7, LastLapTimeSeconds = 33, PositionInRun = 2, LastPitLap = 0 } },
            new RacerRow { TimeOffset = TimeSpan.FromSeconds(45), Racer = new Racer { RacerNumber = "336", RacerClassName = "GP1", CurrentLap = 7, LastLapTimeSeconds = 45, PositionInRun = 3, LastPitLap = 6 } },

            new RacerRow { TimeOffset = TimeSpan.FromSeconds(31), Racer = new Racer { RacerNumber = "43A", RacerClassName = "GP2", CurrentLap = 8, LastLapTimeSeconds = 31, PositionInRun = 1, LastPitLap = 7 } },
            new RacerRow { TimeOffset = TimeSpan.FromSeconds(30), Racer = new Racer { RacerNumber = "336", RacerClassName = "GP1", CurrentLap = 8, LastLapTimeSeconds = 30, PositionInRun = 2, LastPitLap = 6 } },
            new RacerRow { TimeOffset = TimeSpan.FromSeconds(60), Racer = new Racer { RacerNumber = "777C", RacerClassName = "GP1", CurrentLap = 8, LastLapTimeSeconds = 60, PositionInRun = 3, LastPitLap = 8 } },

            new RacerRow { TimeOffset = TimeSpan.FromSeconds(30), Racer = new Racer { RacerNumber = "43A", RacerClassName = "GP2", CurrentLap = 9, LastLapTimeSeconds = 30, PositionInRun = 1, LastPitLap = 7 } },
            new RacerRow { TimeOffset = TimeSpan.FromSeconds(30), Racer = new Racer { RacerNumber = "336", RacerClassName = "GP1", CurrentLap = 9, LastLapTimeSeconds = 30, PositionInRun = 2, LastPitLap = 6 } },
            new RacerRow { TimeOffset = TimeSpan.FromSeconds(30), Racer = new Racer { RacerNumber = "777C", RacerClassName = "GP1", CurrentLap = 9, LastLapTimeSeconds = 30, PositionInRun = 3, LastPitLap = 8 } },

            new RacerRow { TimeOffset = TimeSpan.FromSeconds(30), Racer = new Racer { RacerNumber = "43A", RacerClassName = "GP2", CurrentLap = 10, LastLapTimeSeconds = 30, PositionInRun = 1, LastPitLap = 7 } },
            new RacerRow { TimeOffset = TimeSpan.FromSeconds(30), Racer = new Racer { RacerNumber = "336", RacerClassName = "GP1", CurrentLap = 10, LastLapTimeSeconds = 30, PositionInRun = 2, LastPitLap = 6 } },
            new RacerRow { TimeOffset = TimeSpan.FromSeconds(30), Racer = new Racer { RacerNumber = "777C", RacerClassName = "GP1", CurrentLap = 10, LastLapTimeSeconds = 30, PositionInRun = 3, LastPitLap = 8 } },
        };

        // Start offset, end offset, racer
        private List<OffsetRacer> compiledOffsetRacers = new List<OffsetRacer>();
        private class OffsetRacer
        {
            public TimeSpan Start;
            public TimeSpan End;
            public Racer Racer;
        }

        public LeaderboardScript()
        {
            var carNums = Rows.Select(r => r.Racer.RacerNumber).Distinct();
            foreach (var num in carNums)
            {
                OffsetRacer last = null;
                TimeSpan runningOffset = TimeSpan.Zero;
                foreach (var rr in Rows)
                {
                    if (rr.Racer.RacerNumber == num)
                    {
                        runningOffset += rr.TimeOffset;
                        var t = new OffsetRacer { Start = runningOffset, Racer = rr.Racer };
                        if (last != null)
                        {
                            last.End = runningOffset;
                        }
                        compiledOffsetRacers.Add(t);
                        last = t;
                    }
                }
            }
        }

        public Racer[] GetRacers(TimeSpan offset)
        {
            var rs = new List<Racer>();
            foreach (var cr in compiledOffsetRacers)
            {
                if (offset >= cr.Start && offset < cr.End)
                {
                    rs.Add(cr.Racer);
                }
            }
            return rs.ToArray();
        }

        public FlagRow[] FlagRows = new[]
        {
            new FlagRow { TimeOffset = TimeSpan.FromSeconds(0), Flag = Flag.Unknown },
            new FlagRow { TimeOffset = TimeSpan.FromSeconds(20), Flag = Flag.Green },
            new FlagRow { TimeOffset = TimeSpan.FromSeconds(160), Flag = Flag.Yellow },
            new FlagRow { TimeOffset = TimeSpan.FromSeconds(20), Flag = Flag.Green },
            new FlagRow { TimeOffset = TimeSpan.FromSeconds(60), Flag = Flag.Finish },
        };
    }
}

