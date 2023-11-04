using BigMission.Database.Models;
using BigMission.DeviceApp.Shared;
using BigMission.FuelStatistics.FuelConsumption;
using BigMission.TestHelpers;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using System.Collections.Generic;
using System.Linq;
using static BigMission.RaceHeroSdk.RaceHeroClient;

namespace BigMission.FuelStatistics.Tests.FuelConsumption;

[TestClass]
public class ConsumptionProcessorTests
{
    [TestMethod]
    public void ConsumptionProcessor_Test()
    {
        var logger = new Mock<ILoggerFactory>();
        logger.Setup(s => s.CreateLogger(It.IsAny<string>())).Returns(new Logger());
        var dataContext = new DataContext();
        var dateTime = new Mock<IDateTimeHelper>();
        var cons = new ConsumptionProcessor(logger.Object, dataContext, dateTime.Object);

        var map = new ChannelMapping { Id = 55 };
        var fuelStatus = new ChannelStatusDto { ChannelId = 55, Value = 10.0f };
        var dto = new ChannelDataSetDto { Data = new[] { fuelStatus } };

        cons.UpdateTelemetry(dto, 1, map);
        cons.UpdateTelemetry(dto, 2, map);

        var lap1 = new Lap { EventId = 1, Flag = (byte)Flag.Green, LastLapTimeSeconds = 90 };
        cons.UpdateLaps(new List<Lap> { lap1 }, 1).Wait();

        fuelStatus.Value = 9.7f;
        cons.UpdateTelemetry(dto, 1, map);
        cons.UpdateTelemetry(dto, 2, map);

        var lap2 = new Lap { EventId = 1, Flag = (byte)Flag.Green, LastLapTimeSeconds = 90 };
        cons.UpdateLaps(new List<Lap> { lap2 }, 1).Wait();
        cons.UpdateLaps(new List<Lap> { lap2 }, 2).Wait();

        var rangeLapsCh = dataContext.ChannelDataSetDto.Data.First(x => x.ChannelId == 1);
        Assert.AreEqual(32, rangeLapsCh.Value, 0.001);

        var rangeTimeCh = dataContext.ChannelDataSetDto.Data.First(x => x.ChannelId == 2);
        Assert.AreEqual(48.5, rangeTimeCh.Value, 0.001);

        var rangeLapsFlCh = dataContext.ChannelDataSetDto.Data.First(x => x.ChannelId == 3);
        Assert.AreEqual(32, rangeLapsFlCh.Value, 0.001);

        var rangeTimeFlCh = dataContext.ChannelDataSetDto.Data.First(x => x.ChannelId == 4);
        Assert.AreEqual(48.5, rangeTimeFlCh.Value, 0.001);
    }
}
