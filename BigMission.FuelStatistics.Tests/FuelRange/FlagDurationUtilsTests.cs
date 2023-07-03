using BigMission.Cache.Models.Flags;
using BigMission.FuelStatistics.FuelRange;
using BigMission.TestHelpers;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using System;
using System.Collections.Generic;

namespace BigMission.FuelStatistics.Tests.FuelRange
{
    [TestClass]
    public class FlagDurationUtilsTests
    {
        [TestMethod]
        public void MultipleFlagsPerStint()
        {
            var s = GetStints();
            var flags = new List<EventFlag>
            {
                new EventFlag{ Flag = "Yellow", Start = DateTime.Parse("7/4/2021 8:10am"), End = DateTime.Parse("7/4/2021 8:15am")},
                new EventFlag{ Flag = "Yellow", Start = DateTime.Parse("7/4/2021 8:20am"), End = DateTime.Parse("7/4/2021 8:25am")},
                new EventFlag{ Flag = "Yellow", Start = DateTime.Parse("7/4/2021 9:20am"), End = DateTime.Parse("7/4/2021 9:21am")}
            };

            var dtMock = new Mock<IDateTimeHelper>();
            dtMock.Setup(p => p.UtcNow).Returns(DateTime.Parse("7/4/2021 1:20pm"));
            var flagUtils = new FlagDurationUtils(dtMock.Object);
            bool changed = flagUtils.UpdateStintFlagDurations(flags, ref s);

            Assert.IsTrue(changed);
            Assert.AreEqual(11, s[0].YellowDurationMins);
            Assert.AreEqual(0, s[0].RedDurationMins);
            for (int i = 1; i < s.Count - 1; i++)
            {
                Assert.AreEqual(0, s[i].YellowDurationMins);
                Assert.AreEqual(0, s[i].RedDurationMins);
            }
        }

        [TestMethod]
        public void StartStintEdge()
        {
            var s = GetStints();
            var flags = new List<EventFlag>
            {
                new EventFlag{ Flag = "Red", Start = DateTime.Parse("7/4/2021 8:00am"), End = DateTime.Parse("7/4/2021 8:15am")},
            };

            var dtMock = new Mock<IDateTimeHelper>();
            dtMock.Setup(p => p.UtcNow).Returns(DateTime.Parse("7/4/2021 1:20pm"));
            var flagUtils = new FlagDurationUtils(dtMock.Object);
            bool changed = flagUtils.UpdateStintFlagDurations(flags, ref s);
            Assert.IsTrue(changed);
            Assert.AreEqual(0, s[0].YellowDurationMins);
            Assert.AreEqual(15, s[0].RedDurationMins);
        }

        [TestMethod]
        public void EndStintEdge()
        {
            var s = GetStints();
            var flags = new List<EventFlag>
            {
                new EventFlag{ Flag = "Red", Start = DateTime.Parse("7/4/2021 9:10am"), End = DateTime.Parse("7/4/2021 9:30am")},
            };

            var dtMock = new Mock<IDateTimeHelper>();
            dtMock.Setup(p => p.UtcNow).Returns(DateTime.Parse("7/4/2021 1:20pm"));
            var flagUtils = new FlagDurationUtils(dtMock.Object);
            bool changed = flagUtils.UpdateStintFlagDurations(flags, ref s);
            Assert.IsTrue(changed);
            Assert.AreEqual(0, s[0].YellowDurationMins);
            Assert.AreEqual(20, s[0].RedDurationMins);
        }

        // STINT    |--------------|
        // FLAG  |-------------|
        [TestMethod]
        public void PreceedingStintFlag()
        {
            var s = GetStints();
            var flags = new List<EventFlag>
            {
                new EventFlag{ Flag = "Yellow", Start = DateTime.Parse("7/4/2021 8:10am"), End = DateTime.Parse("7/4/2021 8:15am")},
                new EventFlag{ Flag = "Yellow", Start = DateTime.Parse("7/4/2021 8:20am"), End = DateTime.Parse("7/4/2021 8:25am")},
                new EventFlag{ Flag = "Yellow", Start = DateTime.Parse("7/4/2021 9:20am"), End = DateTime.Parse("7/4/2021 9:50am")}
            };

            var dtMock = new Mock<IDateTimeHelper>();
            dtMock.Setup(p => p.UtcNow).Returns(DateTime.Parse("7/4/2021 1:20pm"));
            var flagUtils = new FlagDurationUtils(dtMock.Object);
            bool changed = flagUtils.UpdateStintFlagDurations(flags, ref s);
            Assert.IsTrue(changed);

            // Stint 1
            Assert.AreEqual(20, s[0].YellowDurationMins);
            Assert.AreEqual(0, s[0].RedDurationMins);

            // Stint 2
            Assert.AreEqual(17, s[1].YellowDurationMins);
            Assert.AreEqual(0, s[1].RedDurationMins);

            for (int i = 2; i < s.Count - 1; i++)
            {
                Assert.AreEqual(0, s[i].YellowDurationMins);
                Assert.AreEqual(0, s[i].RedDurationMins);
            }
        }


        // STINT |-------|------------|------------|
        // FLAG  |---------------------------------|
        [TestMethod]
        public void FlagAcrossMultipeStints()
        {
            var s = GetStints();
            var flags = new List<EventFlag>
            {
                new EventFlag{ Flag = "Yellow", Start = DateTime.Parse("7/4/2021 9:10am"), End = DateTime.Parse("7/4/2021 12:10pm")},
            };

            var dtMock = new Mock<IDateTimeHelper>();
            dtMock.Setup(p => p.UtcNow).Returns(DateTime.Parse("7/4/2021 1:20pm"));
            var flagUtils = new FlagDurationUtils(dtMock.Object);
            bool changed = flagUtils.UpdateStintFlagDurations(flags, ref s);
            Assert.IsTrue(changed);

            // Stint 1
            Assert.AreEqual(20, s[0].YellowDurationMins);
            Assert.AreEqual(0, s[0].RedDurationMins);

            // Stint 2
            Assert.AreEqual(46, s[1].YellowDurationMins);
            Assert.AreEqual(0, s[1].RedDurationMins);

            // Stint 3
            Assert.AreEqual(107, s[2].YellowDurationMins);
            Assert.AreEqual(0, s[2].RedDurationMins);
        }

        [TestMethod]
        public void ActiveFlagStartsBetweenStints()
        {
            var s = GetStints();
            var flags = new List<EventFlag>
            {
                new EventFlag{ Flag = "Yellow", Start = DateTime.Parse("7/4/2021 10:20am"), End = null },
            };

            var dtMock = new Mock<IDateTimeHelper>();
            dtMock.Setup(p => p.UtcNow).Returns(DateTime.Parse("7/4/2021 1:20pm"));
            var flagUtils = new FlagDurationUtils(dtMock.Object);
            bool changed = flagUtils.UpdateStintFlagDurations(flags, ref s);
            Assert.IsTrue(changed);

            // Stint 1
            Assert.AreEqual(0, s[0].YellowDurationMins);
            Assert.AreEqual(0, s[0].RedDurationMins);

            // Stint 2
            Assert.AreEqual(0, s[1].YellowDurationMins);
            Assert.AreEqual(0, s[1].RedDurationMins);

            // Stint 3
            Assert.AreEqual(127, s[2].YellowDurationMins);
            Assert.AreEqual(0, s[2].RedDurationMins);

            // Stint 4
            // Now = 1:20pm, Duration = 49
            Assert.AreEqual(49, s[3].YellowDurationMins);
            Assert.AreEqual(0, s[3].RedDurationMins);
        }

        private static List<Cache.Models.FuelRange.Stint> GetStints()
        {
            return new List<Cache.Models.FuelRange.Stint>
            {
                new Cache.Models.FuelRange.Stint{ Start = DateTime.Parse("7/4/2021 8:00am"), End = DateTime.Parse("7/4/2021 9:30am") },
                new Cache.Models.FuelRange.Stint{ Start = DateTime.Parse("7/4/2021 9:33am"), End = DateTime.Parse("7/4/2021 10:19am") },
                new Cache.Models.FuelRange.Stint{ Start = DateTime.Parse("7/4/2021 10:23am"), End = DateTime.Parse("7/4/2021 12:30pm") },
                new Cache.Models.FuelRange.Stint{ Start = DateTime.Parse("7/4/2021 12:31pm"),  },
            };
        }
    }
}
