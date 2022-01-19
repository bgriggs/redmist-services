using BigMission.FuelStatistics.FuelRange;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using static BigMission.RaceHeroSdk.RaceHeroClient;

namespace BigMission.FuelStatistics.Tests.FuelRange
{
    [TestClass]
    public class LapDataTriggersTests
    {
        [TestMethod]
        public void StartRaceStint()
        {
            var data = new Tuple<Lap, DateTime>[]
            {
                Tuple.Create(new Lap { CurrentLap = 0, LastPitLap = 0, Timestamp = DateTime.Parse("7/4/2021 8:00:00.0 am"), LastLapTimeSeconds = 0 }, default(DateTime)),
                Tuple.Create(new Lap { CurrentLap = 1, LastPitLap = 0, Timestamp = DateTime.Parse("7/4/2021 8:01:00.0 am"), LastLapTimeSeconds = 60 }, DateTime.Parse("7/4/2021 8:00:00.0 am")),
                Tuple.Create(new Lap { CurrentLap = 2, LastPitLap = 0, Timestamp = DateTime.Parse("7/4/2021 8:02:00.0 am"), LastLapTimeSeconds = 60 }, default(DateTime)),
            };

            var lapTriggers = new LapDataTriggers();
            Cache.Models.FuelRange.Stint startStint = null;
            foreach(var d in data)
            {
                var result = lapTriggers.ProcessLap(d.Item1, startStint);
                Assert.AreEqual(d.Item2, result.Start);

                if (result.Start > default(DateTime))
                {
                    startStint = result;
                }
            }
        }

        [TestMethod]
        public void StartNewStint()
        {
            var data = new Tuple<Lap, DateTime>[]
            {
                Tuple.Create(new Lap { CurrentLap = 100, LastPitLap = 100, Timestamp = DateTime.Parse("7/4/2021 8:00:00.0 am"), LastLapTimeSeconds = 60 }, default(DateTime)),
                Tuple.Create(new Lap { CurrentLap = 101, LastPitLap = 100, Timestamp = DateTime.Parse("7/4/2021 8:01:00.0 am"), LastLapTimeSeconds = 60 }, DateTime.Parse("7/4/2021 8:00:00.0 am")),
                Tuple.Create(new Lap { CurrentLap = 102, LastPitLap = 100, Timestamp = DateTime.Parse("7/4/2021 8:02:00.0 am"), LastLapTimeSeconds = 60 }, default(DateTime)),
            };

            var lapTriggers = new LapDataTriggers();
            var currentStint = new Cache.Models.FuelRange.Stint { Start = DateTime.Parse("7/4/2021 7:00:00.0 am"), End = DateTime.Parse("7/4/2021 8:00:00.0 am") };
            foreach (var d in data)
            {
                var result = lapTriggers.ProcessLap(d.Item1, currentStint);
                Assert.AreEqual(d.Item2, result.Start);

                if (result.Start > default(DateTime))
                {
                    currentStint = result;
                }
            }
        }

        [TestMethod]
        public void EndStint()
        {
            var data = new Tuple<Lap, DateTime?>[]
            {
                Tuple.Create<Lap, DateTime?>(new Lap { CurrentLap = 100, LastPitLap = 0, Timestamp = DateTime.Parse("7/4/2021 8:00:00.0 am"), LastLapTimeSeconds = 60 }, null),
                Tuple.Create<Lap, DateTime?>(new Lap { CurrentLap = 101, LastPitLap = 101, Timestamp = DateTime.Parse("7/4/2021 8:01:00.0 am"), LastLapTimeSeconds = 60 }, DateTime.Parse("7/4/2021 8:01:00.0 am")),
            };

            var lapTriggers = new LapDataTriggers();
            var currentStint = new Cache.Models.FuelRange.Stint { Start = DateTime.Parse("7/4/2021 7:00:00.0 am"), End = null };
            foreach (var d in data)
            {
                var result = lapTriggers.ProcessLap(d.Item1, currentStint);
                Assert.AreEqual(d.Item2, result.End);
            }
        }

        [TestMethod]
        public void EndRace()
        {
            var data = new Tuple<Lap, DateTime?>[]
            {
                Tuple.Create<Lap, DateTime?>(new Lap { CurrentLap = 200, LastPitLap = 180, Timestamp = DateTime.Parse("7/4/2021 8:00:00.0 am"), LastLapTimeSeconds = 60, Flag = (byte)Flag.Green }, null),
                Tuple.Create<Lap, DateTime?>(new Lap { CurrentLap = 201, LastPitLap = 180, Timestamp = DateTime.Parse("7/4/2021 8:01:00.0 am"), LastLapTimeSeconds = 60, Flag = (byte)Flag.Finish }, DateTime.Parse("7/4/2021 8:01:00.0 am")),
            };

            var lapTriggers = new LapDataTriggers();
            var currentStint = new Cache.Models.FuelRange.Stint { Start = DateTime.Parse("7/4/2021 7:00:00.0 am"), End = null };
            foreach (var d in data)
            {
                var result = lapTriggers.ProcessLap(d.Item1, currentStint);
                Assert.AreEqual(d.Item2, result.End);
            }
        }
    }
}
