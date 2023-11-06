using BigMission.Database.Models;
using BigMission.DeviceApp.Shared;
using BigMission.FuelStatistics.FuelConsumption;
using BigMission.TestHelpers;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static BigMission.RaceHeroSdk.RaceHeroClient;

namespace BigMission.FuelStatistics.Tests.FuelConsumption;

[TestClass]
public class CarConsumptionTests
{
    [TestMethod]
    public void CarConsumption_TwoGrnLaps_Test()
    {
        var logger = new Mock<ILoggerFactory>();
        logger.Setup(s => s.CreateLogger(It.IsAny<string>())).Returns(new Logger());
        var dataContext = new DataContext();
        var dateTime = new Mock<IDateTimeHelper>();

        var cc = new CarConsumption(1, logger.Object, dataContext, dateTime.Object);

        var lap1 = new Lap { EventId = 1, Flag = (byte)Flag.Green, LastLapTimeSeconds = 90 };
        cc.FuelLevel = 10.0f;
        cc.Process(lap1).Wait();

        var lap2 = new Lap { EventId = 1, Flag = (byte)Flag.Green, LastLapTimeSeconds = 90 };
        cc.FuelLevel = 9.7f;
        cc.Process(lap2).Wait();

        var rangeLapsCh = dataContext.ChannelDataSetDto.Data.First(x => x.ChannelId == 1);
        Assert.AreEqual(32, rangeLapsCh.Value, 0.001);

        var rangeTimeCh = dataContext.ChannelDataSetDto.Data.First(x => x.ChannelId == 2);
        Assert.AreEqual(48.5, rangeTimeCh.Value, 0.001);

        var rangeLapsFlCh = dataContext.ChannelDataSetDto.Data.First(x => x.ChannelId == 3);
        Assert.AreEqual(32, rangeLapsFlCh.Value, 0.001);

        var rangeTimeFlCh = dataContext.ChannelDataSetDto.Data.First(x => x.ChannelId == 4);
        Assert.AreEqual(48.5, rangeTimeFlCh.Value, 0.001);

        var consCh = dataContext.ChannelDataSetDto.Data.First(x => x.ChannelId == 10);
        Assert.AreEqual(0.3, consCh.Value, 0.001);
    }
    
    [TestMethod]
    public void CarConsumption_IncFuel_Test()
    {
        var logger = new Mock<ILoggerFactory>();
        logger.Setup(s => s.CreateLogger(It.IsAny<string>())).Returns(new Logger());
        var dataContext = new DataContext();
        var dateTime = new Mock<IDateTimeHelper>();

        var cc = new CarConsumption(1, logger.Object, dataContext, dateTime.Object);

        var lap1 = new Lap { EventId = 1, Flag = (byte)Flag.Green, LastLapTimeSeconds = 90 };
        cc.FuelLevel = 10.0f;
        cc.Process(lap1).Wait();

        var lap2 = new Lap { EventId = 1, Flag = (byte)Flag.Green, LastLapTimeSeconds = 90 };
        cc.FuelLevel = 9.7f;
        cc.Process(lap2).Wait();
        
        var lap3 = new Lap { EventId = 1, Flag = (byte)Flag.Green, LastLapTimeSeconds = 90 };
        cc.FuelLevel = 9.7f;
        cc.Process(lap3).Wait();

        var rangeLapsCh = dataContext.ChannelDataSetDto.Data.First(x => x.ChannelId == 1);
        Assert.AreEqual(0, rangeLapsCh.Value, 0.001);

        var rangeTimeCh = dataContext.ChannelDataSetDto.Data.First(x => x.ChannelId == 2);
        Assert.AreEqual(0, rangeTimeCh.Value, 0.001);

        var rangeLapsFlCh = dataContext.ChannelDataSetDto.Data.First(x => x.ChannelId == 3);
        Assert.AreEqual(32, rangeLapsFlCh.Value, 0.001);

        var rangeTimeFlCh = dataContext.ChannelDataSetDto.Data.First(x => x.ChannelId == 4);
        Assert.AreEqual(48.5, rangeTimeFlCh.Value, 0.001);
    }

    [TestMethod]
    public void CarConsumption_NonGreen_Test()
    {
        var logger = new Mock<ILoggerFactory>();
        logger.Setup(s => s.CreateLogger(It.IsAny<string>())).Returns(new Logger());
        var dataContext = new DataContext();
        var dateTime = new Mock<IDateTimeHelper>();

        var cc = new CarConsumption(1, logger.Object, dataContext, dateTime.Object);

        var lap1 = new Lap { EventId = 1, Flag = (byte)Flag.Green, LastLapTimeSeconds = 90 };
        cc.FuelLevel = 10.0f;
        cc.Process(lap1).Wait();

        var lap2 = new Lap { EventId = 1, Flag = (byte)Flag.Green, LastLapTimeSeconds = 90 };
        cc.FuelLevel = 9.7f;
        cc.Process(lap2).Wait();

        var lap3 = new Lap { EventId = 1, Flag = (byte)Flag.Yellow, LastLapTimeSeconds = 105 };
        cc.FuelLevel = 9.5f;
        cc.Process(lap3).Wait();

        var rangeLapsCh = dataContext.ChannelDataSetDto.Data.First(x => x.ChannelId == 1);
        Assert.AreEqual(47, rangeLapsCh.Value, 0.001);

        var rangeTimeCh = dataContext.ChannelDataSetDto.Data.First(x => x.ChannelId == 2);
        Assert.AreEqual(83.1, rangeTimeCh.Value, 0.001);

        var rangeLapsFlCh = dataContext.ChannelDataSetDto.Data.First(x => x.ChannelId == 3);
        Assert.AreEqual(31, rangeLapsFlCh.Value, 0.001);

        var rangeTimeFlCh = dataContext.ChannelDataSetDto.Data.First(x => x.ChannelId == 4);
        Assert.AreEqual(47.5, rangeTimeFlCh.Value, 0.001);

        var consFlCh = dataContext.ChannelDataSetDto.Data.First(x => x.ChannelId == 100);
        Assert.AreEqual(0.3, consFlCh.Value, 0.001);
    }

    [TestMethod]
    public void CarConsumption_HistoryTrim_Test()
    {
        var logger = new Mock<ILoggerFactory>();
        logger.Setup(s => s.CreateLogger(It.IsAny<string>())).Returns(new Logger());
        var dataContext = new DataContext();
        var dateTime = new Mock<IDateTimeHelper>();

        var cc = new CarConsumption(1, logger.Object, dataContext, dateTime.Object);

        var lap1 = new Lap { EventId = 1, Flag = (byte)Flag.Green, LastLapTimeSeconds = 90 };
        cc.FuelLevel = 10.0f;
        cc.Process(lap1).Wait();

        var lap2 = new Lap { EventId = 1, Flag = (byte)Flag.Green, LastLapTimeSeconds = 90 };
        cc.FuelLevel = 9.7f;
        cc.Process(lap2).Wait();

        var lap3 = new Lap { EventId = 1, Flag = (byte)Flag.Green, LastLapTimeSeconds = 88 };
        cc.FuelLevel = 9.5f;
        cc.Process(lap3).Wait();

        var lap4 = new Lap { EventId = 1, Flag = (byte)Flag.Green, LastLapTimeSeconds = 88 };
        cc.FuelLevel = 9.2f;
        cc.Process(lap4).Wait();

        var lap5 = new Lap { EventId = 1, Flag = (byte)Flag.Green, LastLapTimeSeconds = 85 };
        cc.FuelLevel = 8.7f;
        cc.Process(lap5).Wait();

        var rangeLapsCh = dataContext.ChannelDataSetDto.Data.First(x => x.ChannelId == 1);
        Assert.AreEqual(17, rangeLapsCh.Value, 0.001);

        var rangeTimeCh = dataContext.ChannelDataSetDto.Data.First(x => x.ChannelId == 2);
        Assert.AreEqual(24.6, rangeTimeCh.Value, 0.001);

        var rangeLapsFlCh = dataContext.ChannelDataSetDto.Data.First(x => x.ChannelId == 3);
        Assert.AreEqual(26, rangeLapsFlCh.Value, 0.001);

        var rangeTimeFlCh = dataContext.ChannelDataSetDto.Data.First(x => x.ChannelId == 4);
        Assert.AreEqual(37.8, rangeTimeFlCh.Value, 0.001);
    }
}

public class DataContext : IDataContext
{
    public ChannelDataSetDto ChannelDataSetDto { get; set; }

    public Task<DateTime?> CheckReload(int teamId)
    {
        throw new NotImplementedException();
    }

    public Task ClearCachedEvent(int rhEventId)
    {
        throw new NotImplementedException();
    }

    public Task EndBatch()
    {
        throw new NotImplementedException();
    }

    public Task<List<Database.Models.Car>> GetCars(int[] carIds)
    {
        throw new NotImplementedException();
    }

    public Task<List<ChannelMapping>> GetChannelMappings(int[] deviceAppIds, string[] channelNames)
    {
        throw new NotImplementedException();
    }

    public Task<List<ChannelMapping>> GetConsumptionChannels(int carId)
    {
        var mappings = new List<ChannelMapping>();

        var rangeLapsMap = new ChannelMapping { Id = 1, DeviceAppId = 2, ReservedName = CarConsumption.SVR_RANGE_LAPS };
        var rangeTimeMap = new ChannelMapping { Id = 2, DeviceAppId = 2, ReservedName = CarConsumption.SVR_RANGE_TIME };
        var rangeFlLapsMap = new ChannelMapping { Id = 3, DeviceAppId = 2, ReservedName = CarConsumption.SVR_FL_RANGE_LAPS };
        var rangeFlTimeMap = new ChannelMapping { Id = 4, DeviceAppId = 2, ReservedName = CarConsumption.SVR_FL_RANGE_TIME };
        var consMap = new ChannelMapping { Id = 10, DeviceAppId = 2, ReservedName = CarConsumption.SVR_CONS_GAL_LAP };
        var consFlMap = new ChannelMapping { Id = 100, DeviceAppId = 2, ReservedName = CarConsumption.SVR_FL_CONS_GAL_LAP };
        mappings.Add(rangeLapsMap);
        mappings.Add(rangeTimeMap);
        mappings.Add(rangeFlLapsMap);
        mappings.Add(rangeFlTimeMap);
        mappings.Add(consMap);
        mappings.Add(consFlMap);

        return Task.FromResult(mappings);
    }

    public Task<List<DeviceAppConfig>> GetDeviceAppConfig(int[] carIds)
    {
        throw new NotImplementedException();
    }

    public Task<List<RaceEventSetting>> GetEventSettings()
    {
        throw new NotImplementedException();
    }

    public Task<List<FuelRangeSetting>> GetFuelRangeSettings(int[] carIds)
    {
        throw new NotImplementedException();
    }

    public Task<List<Lap>> GetSavedLaps(int eventId)
    {
        throw new NotImplementedException();
    }

    public Task<List<FuelRangeStint>> GetTeamStints(int teamId, int eventId)
    {
        throw new NotImplementedException();
    }

    public Task<List<Lap>> PopEventLaps(int eventId)
    {
        throw new NotImplementedException();
    }

    public Task PublishChannelStatus(ChannelDataSetDto channelDataSetDto)
    {
        ChannelDataSetDto = channelDataSetDto;
        return Task.CompletedTask;
    }

    public Task StartBatch()
    {
        throw new NotImplementedException();
    }

    public Task UpdateCarStatus(Car car, int eventId)
    {
        throw new NotImplementedException();
    }
}