using BigMission.Cache.Models.Flags;
using BigMission.Cache.Models.FuelRange;
using BigMission.Database.Models;
using BigMission.RaceHeroTestHelpers;
using BigMission.TestHelpers;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace BigMission.FuelStatistics.Tests
{
    [TestClass]
    public class EventTests
    {
        //[TestMethod]
        public void CheckNegativePitTimes()
        {
            var settings = new RaceEventSetting { RaceHeroEventId = "29201" };
            var logger = new Mock<ILoggerFactory>();
            var dateTime = new Mock<IDateTimeHelper>();
            var dataContext = new Mock<IDataContext>();
            var fuelRangeContext = new Mock<IFuelRangeContext>();
            var flagContext = new Mock<IFlagContext>();

            var evt = new Event(settings, logger.Object, dateTime.Object, dataContext.Object, fuelRangeContext.Object, flagContext.Object);
            // TODO: move to personal onedrive location
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
