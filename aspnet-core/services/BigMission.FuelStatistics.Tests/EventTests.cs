using BigMission.Cache;
using BigMission.Cache.FuelRange;
using BigMission.RaceHeroTestHelpers;
using BigMission.RaceManagement;
using BigMission.TestHelpers;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using NLog;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace BigMission.FuelStatistics.Tests
{
    [TestClass]
    public class EventTests
    {
        [TestMethod]
        public void CheckNegativePitTimes()
        {
            var settings = new RaceEventSettings { RaceHeroEventId = "29201" };
            var logger = new Mock<ILogger>();
            var dateTime = new Mock<IDateTimeHelper>();
            var dataContext = new Mock<IDataContext>();
            var fuelRangeContext = new Mock<IFuelRangeContext>();
            var timer = new Mock<ITimerHelper>();

            var evt = new Event(settings, logger.Object, dateTime.Object, dataContext.Object, fuelRangeContext.Object, timer.Object);
            var frh = new FileRHClient("../../../EventTestData/Event-29201", "../../../EventTestData/Leaderboard-29201-47955237");
            var laps = frh.GetNextLaps();
            do
            {
                evt.UpdateLap(laps).Wait();

                // Check cars.  Negative times.
                var cars = evt.GetCars();

                foreach(var car in cars)
                {
                    foreach(var pit in car.Pits)
                    {
                        Assert.IsTrue(pit.EstPitStopSecs >= 0);
                    }
                }

                laps = frh.GetNextLaps();
            }
            while (laps != null);
        }
    }
}
