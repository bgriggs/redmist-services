using BigMission.Database.Models;
using BigMission.DeviceApp.Shared;
using BigMission.FuelStatistics.FuelRange;
using BigMission.TestHelpers;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using System;

namespace BigMission.FuelStatistics.Tests.FuelRange;

[TestClass]
public class TelemetryTriggersTests
{
    private static ChannelMapping GetFuelLevelChannel()
    {
        return new ChannelMapping { Id = 12, DeviceAppId = 1, CanId = 251711489, ChannelName = "FuelLevel", ReservedName = "FuelLevel" };
    }
    private static ChannelMapping GetSpeedChannel()
    {
        return new ChannelMapping { Id = 78, DeviceAppId = 1, CanId = 251711493, ChannelName = "Speed", ReservedName = "Speed" };
    }

    [TestMethod]
    public void StartOfRaceStintTest()
    {
        var dtMock = new Mock<IDateTimeHelper>();
        dtMock.Setup(p => p.UtcNow).Returns(DateTime.Parse("7/4/2021 1:00:00.0 pm"));

        var telemTriggers = new TelemetryTriggers(dtMock.Object);
        telemTriggers.SpeedChannel = GetSpeedChannel();
        telemTriggers.FuelLevelChannel = GetFuelLevelChannel();

        var telem = new ChannelDataSetDto { };
        var tuples = new Tuple<DateTime, ChannelStatusDto, DateTime>[]
        {
            Tuple.Create(DateTime.Parse("7/4/2021 1:00:00.0 pm"), new ChannelStatusDto { ChannelId = 78, Value = 0 }, default(DateTime)),
            Tuple.Create(DateTime.Parse("7/4/2021 1:00:01.0 pm"), new ChannelStatusDto { ChannelId = 78, Value = 0 }, default(DateTime)),
            Tuple.Create(DateTime.Parse("7/4/2021 1:00:02.0 pm"), new ChannelStatusDto { ChannelId = 78, Value = 1 }, default(DateTime)),
            Tuple.Create(DateTime.Parse("7/4/2021 1:00:03.0 pm"), new ChannelStatusDto { ChannelId = 78, Value = 4 }, default(DateTime)),
            Tuple.Create(DateTime.Parse("7/4/2021 1:00:04.0 pm"), new ChannelStatusDto { ChannelId = 78, Value = 7 }, default(DateTime)),
            Tuple.Create(DateTime.Parse("7/4/2021 1:00:05.0 pm"), new ChannelStatusDto { ChannelId = 78, Value = 9 }, default(DateTime)),
            Tuple.Create(DateTime.Parse("7/4/2021 1:00:06.0 pm"), new ChannelStatusDto { ChannelId = 78, Value = 10 }, default(DateTime)),
            Tuple.Create(DateTime.Parse("7/4/2021 1:00:07.0 pm"), new ChannelStatusDto { ChannelId = 78, Value = 14 }, default(DateTime)),
            Tuple.Create(DateTime.Parse("7/4/2021 1:00:08.0 pm"), new ChannelStatusDto { ChannelId = 78, Value = 18 }, default(DateTime)),
            Tuple.Create(DateTime.Parse("7/4/2021 1:00:09.0 pm"), new ChannelStatusDto { ChannelId = 78, Value = 22 }, default(DateTime)),
            Tuple.Create(DateTime.Parse("7/4/2021 1:00:10.0 pm"), new ChannelStatusDto { ChannelId = 78, Value = 24 }, default(DateTime)),
            Tuple.Create(DateTime.Parse("7/4/2021 1:00:11.0 pm"), new ChannelStatusDto { ChannelId = 78, Value = 28 }, default(DateTime)),
            Tuple.Create(DateTime.Parse("7/4/2021 1:00:12.0 pm"), new ChannelStatusDto { ChannelId = 78, Value = 33 }, default(DateTime)),
            Tuple.Create(DateTime.Parse("7/4/2021 1:00:13.0 pm"), new ChannelStatusDto { ChannelId = 78, Value = 37 }, default(DateTime)),
            Tuple.Create(DateTime.Parse("7/4/2021 1:00:14.0 pm"), new ChannelStatusDto { ChannelId = 78, Value = 33 }, default(DateTime)),
            Tuple.Create(DateTime.Parse("7/4/2021 1:00:15.0 pm"), new ChannelStatusDto { ChannelId = 78, Value = 45 }, default(DateTime)),
            Tuple.Create(DateTime.Parse("7/4/2021 1:00:16.0 pm"), new ChannelStatusDto { ChannelId = 78, Value = 48 }, DateTime.Parse("7/4/2021 1:00:16.0 pm")),
            Tuple.Create(DateTime.Parse("7/4/2021 1:00:17.0 pm"), new ChannelStatusDto { ChannelId = 78, Value = 50 }, default(DateTime)),
            Tuple.Create(DateTime.Parse("7/4/2021 1:00:18.0 pm"), new ChannelStatusDto { ChannelId = 78, Value = 55 }, default(DateTime)),
            Tuple.Create(DateTime.Parse("7/4/2021 1:00:19.0 pm"), new ChannelStatusDto { ChannelId = 78, Value = 60 }, default(DateTime)),
        };

        Cache.Models.FuelRange.Stint startStint = null;
        var fuelCh = new ChannelStatusDto { ChannelId = 12, Value = 15f };
        foreach (var t in tuples)
        {
            dtMock.Setup(p => p.UtcNow).Returns(t.Item1);
            telem.Data = new[] { t.Item2, fuelCh };
            var result = telemTriggers.ProcessCarTelemetry(telem, startStint);
            Assert.AreEqual(t.Item3, result.Start);
            if (result.Start > default(DateTime))
            {
                startStint = result;
            }
        }
    }

    [TestMethod]
    public void StartNewStintTest()
    {
        var dtMock = new Mock<IDateTimeHelper>();
        dtMock.Setup(p => p.UtcNow).Returns(DateTime.Parse("7/4/2021 1:00:00.0 pm"));

        var telemTriggers = new TelemetryTriggers(dtMock.Object);
        telemTriggers.SpeedChannel = GetSpeedChannel();
        telemTriggers.FuelLevelChannel = GetFuelLevelChannel();

        var telem = new ChannelDataSetDto { };
        var tuples = new Tuple<DateTime, ChannelStatusDto, DateTime>[]
        {
            Tuple.Create(DateTime.Parse("7/4/2021 2:00:00.0 pm"), new ChannelStatusDto { ChannelId = 78, Value = 0 }, default(DateTime)),
            Tuple.Create(DateTime.Parse("7/4/2021 2:00:01.0 pm"), new ChannelStatusDto { ChannelId = 78, Value = 0 }, default(DateTime)),
            Tuple.Create(DateTime.Parse("7/4/2021 2:00:02.0 pm"), new ChannelStatusDto { ChannelId = 78, Value = 1 }, default(DateTime)),
            Tuple.Create(DateTime.Parse("7/4/2021 2:00:03.0 pm"), new ChannelStatusDto { ChannelId = 78, Value = 4 }, default(DateTime)),
            Tuple.Create(DateTime.Parse("7/4/2021 2:00:04.0 pm"), new ChannelStatusDto { ChannelId = 78, Value = 7 }, default(DateTime)),
            Tuple.Create(DateTime.Parse("7/4/2021 2:00:05.0 pm"), new ChannelStatusDto { ChannelId = 78, Value = 9 }, default(DateTime)),
            Tuple.Create(DateTime.Parse("7/4/2021 2:00:06.0 pm"), new ChannelStatusDto { ChannelId = 78, Value = 10 }, default(DateTime)),
            Tuple.Create(DateTime.Parse("7/4/2021 2:00:07.0 pm"), new ChannelStatusDto { ChannelId = 78, Value = 14 }, default(DateTime)),
            Tuple.Create(DateTime.Parse("7/4/2021 2:00:08.0 pm"), new ChannelStatusDto { ChannelId = 78, Value = 18 }, default(DateTime)),
            Tuple.Create(DateTime.Parse("7/4/2021 2:00:09.0 pm"), new ChannelStatusDto { ChannelId = 78, Value = 22 }, default(DateTime)),
            Tuple.Create(DateTime.Parse("7/4/2021 2:00:10.0 pm"), new ChannelStatusDto { ChannelId = 78, Value = 24 }, default(DateTime)),
            Tuple.Create(DateTime.Parse("7/4/2021 2:00:11.0 pm"), new ChannelStatusDto { ChannelId = 78, Value = 28 }, default(DateTime)),
            Tuple.Create(DateTime.Parse("7/4/2021 2:00:12.0 pm"), new ChannelStatusDto { ChannelId = 78, Value = 33 }, default(DateTime)),
            Tuple.Create(DateTime.Parse("7/4/2021 2:00:13.0 pm"), new ChannelStatusDto { ChannelId = 78, Value = 37 }, default(DateTime)),
            Tuple.Create(DateTime.Parse("7/4/2021 2:00:14.0 pm"), new ChannelStatusDto { ChannelId = 78, Value = 33 }, default(DateTime)),
            Tuple.Create(DateTime.Parse("7/4/2021 2:00:15.0 pm"), new ChannelStatusDto { ChannelId = 78, Value = 45 }, default(DateTime)),
            Tuple.Create(DateTime.Parse("7/4/2021 2:00:16.0 pm"), new ChannelStatusDto { ChannelId = 78, Value = 48 }, DateTime.Parse("7/4/2021 2:00:16.0 pm")),
            Tuple.Create(DateTime.Parse("7/4/2021 2:00:17.0 pm"), new ChannelStatusDto { ChannelId = 78, Value = 50 }, default(DateTime)),
            Tuple.Create(DateTime.Parse("7/4/2021 2:00:18.0 pm"), new ChannelStatusDto { ChannelId = 78, Value = 55 }, default(DateTime)),
            Tuple.Create(DateTime.Parse("7/4/2021 2:00:19.0 pm"), new ChannelStatusDto { ChannelId = 78, Value = 60 }, default(DateTime)),
        };

        var currentStint = new Cache.Models.FuelRange.Stint { Start = DateTime.Parse("7/4/2021 1:00:00.0 pm"), End = DateTime.Parse("7/4/2021 2:00:00.0 pm") };
        var fuelCh = new ChannelStatusDto { ChannelId = 12, Value = 15f };
        foreach (var t in tuples)
        {
            dtMock.Setup(p => p.UtcNow).Returns(t.Item1);
            telem.Data = new[] { t.Item2, fuelCh };
            var result = telemTriggers.ProcessCarTelemetry(telem, currentStint);
            Assert.AreEqual(t.Item3, result.Start);

            if (result.Start > default(DateTime))
            {
                currentStint = result;
            }
        }
    }

    [TestMethod]
    public void EndtStintTest()
    {
        var dtMock = new Mock<IDateTimeHelper>();
        dtMock.Setup(p => p.UtcNow).Returns(DateTime.Parse("7/4/2021 1:00:00.0 pm"));

        var telemTriggers = new TelemetryTriggers(dtMock.Object);
        telemTriggers.SpeedChannel = GetSpeedChannel();
        telemTriggers.FuelLevelChannel = GetFuelLevelChannel();

        var telem = new ChannelDataSetDto { };
        var tuples = new Tuple<DateTime, ChannelStatusDto, ChannelStatusDto, DateTime?>[]
        {
            Tuple.Create<DateTime, ChannelStatusDto, ChannelStatusDto, DateTime?>(DateTime.Parse("7/4/2021 3:00:00.0 pm"), new ChannelStatusDto { ChannelId = 78, Value = 50 }, new ChannelStatusDto { ChannelId = 12, Value = 7 }, null),
            Tuple.Create<DateTime, ChannelStatusDto, ChannelStatusDto, DateTime?>(DateTime.Parse("7/4/2021 3:00:01.0 pm"), new ChannelStatusDto { ChannelId = 78, Value = 40 }, new ChannelStatusDto { ChannelId = 12, Value = 5 }, null),
            Tuple.Create<DateTime, ChannelStatusDto, ChannelStatusDto, DateTime?>(DateTime.Parse("7/4/2021 3:00:02.0 pm"), new ChannelStatusDto { ChannelId = 78, Value = 35 }, new ChannelStatusDto { ChannelId = 12, Value = 4 }, null),
            Tuple.Create<DateTime, ChannelStatusDto, ChannelStatusDto, DateTime?>(DateTime.Parse("7/4/2021 3:00:03.0 pm"), new ChannelStatusDto { ChannelId = 78, Value = 35 }, new ChannelStatusDto { ChannelId = 12, Value = 4 }, null),
            Tuple.Create<DateTime, ChannelStatusDto, ChannelStatusDto, DateTime?>(DateTime.Parse("7/4/2021 3:00:04.0 pm"), new ChannelStatusDto { ChannelId = 78, Value = 34 }, new ChannelStatusDto { ChannelId = 12, Value = 4 }, null),
            Tuple.Create<DateTime, ChannelStatusDto, ChannelStatusDto, DateTime?>(DateTime.Parse("7/4/2021 3:00:05.0 pm"), new ChannelStatusDto { ChannelId = 78, Value = 33 }, new ChannelStatusDto { ChannelId = 12, Value = 5 }, null),
            Tuple.Create<DateTime, ChannelStatusDto, ChannelStatusDto, DateTime?>(DateTime.Parse("7/4/2021 3:00:06.0 pm"), new ChannelStatusDto { ChannelId = 78, Value = 34 }, new ChannelStatusDto { ChannelId = 12, Value = 4 }, null),
            Tuple.Create<DateTime, ChannelStatusDto, ChannelStatusDto, DateTime?>(DateTime.Parse("7/4/2021 3:00:07.0 pm"), new ChannelStatusDto { ChannelId = 78, Value = 35 }, new ChannelStatusDto { ChannelId = 12, Value = 3 }, null),
            Tuple.Create<DateTime, ChannelStatusDto, ChannelStatusDto, DateTime?>(DateTime.Parse("7/4/2021 3:00:08.0 pm"), new ChannelStatusDto { ChannelId = 78, Value = 20 }, new ChannelStatusDto { ChannelId = 12, Value = 4 }, null),
            Tuple.Create<DateTime, ChannelStatusDto, ChannelStatusDto, DateTime?>(DateTime.Parse("7/4/2021 3:00:09.0 pm"), new ChannelStatusDto { ChannelId = 78, Value = 10 }, new ChannelStatusDto { ChannelId = 12, Value = 3 }, null),
            Tuple.Create<DateTime, ChannelStatusDto, ChannelStatusDto, DateTime?>(DateTime.Parse("7/4/2021 3:00:10.0 pm"), new ChannelStatusDto { ChannelId = 78, Value = 5 }, new ChannelStatusDto { ChannelId = 12, Value = 4 }, null),
            Tuple.Create<DateTime, ChannelStatusDto, ChannelStatusDto, DateTime?>(DateTime.Parse("7/4/2021 3:00:11.0 pm"), new ChannelStatusDto { ChannelId = 78, Value = 4 }, new ChannelStatusDto { ChannelId = 12, Value = 5 }, null),
            Tuple.Create<DateTime, ChannelStatusDto, ChannelStatusDto, DateTime?>(DateTime.Parse("7/4/2021 3:00:12.0 pm"), new ChannelStatusDto { ChannelId = 78, Value = 3 }, new ChannelStatusDto { ChannelId = 12, Value = 4 }, null),
            Tuple.Create<DateTime, ChannelStatusDto, ChannelStatusDto, DateTime?>(DateTime.Parse("7/4/2021 3:00:13.0 pm"), new ChannelStatusDto { ChannelId = 78, Value = 1 }, new ChannelStatusDto { ChannelId = 12, Value = 3 }, null),
            Tuple.Create<DateTime, ChannelStatusDto, ChannelStatusDto, DateTime?>(DateTime.Parse("7/4/2021 3:00:14.0 pm"), new ChannelStatusDto { ChannelId = 78, Value = 1 }, new ChannelStatusDto { ChannelId = 12, Value = 4 }, null),
            Tuple.Create<DateTime, ChannelStatusDto, ChannelStatusDto, DateTime?>(DateTime.Parse("7/4/2021 3:00:15.0 pm"), new ChannelStatusDto { ChannelId = 78, Value = 0 }, new ChannelStatusDto { ChannelId = 12, Value = 4.5f }, null),
            Tuple.Create<DateTime, ChannelStatusDto, ChannelStatusDto, DateTime?>(DateTime.Parse("7/4/2021 3:00:16.0 pm"), new ChannelStatusDto { ChannelId = 78, Value = 0 }, new ChannelStatusDto { ChannelId = 12, Value = 5.3f }, null),
            Tuple.Create<DateTime, ChannelStatusDto, ChannelStatusDto, DateTime?>(DateTime.Parse("7/4/2021 3:00:17.0 pm"), new ChannelStatusDto { ChannelId = 78, Value = 2 }, new ChannelStatusDto { ChannelId = 12, Value = 7.1f }, null),
            Tuple.Create<DateTime, ChannelStatusDto, ChannelStatusDto, DateTime?>(DateTime.Parse("7/4/2021 3:00:18.0 pm"), new ChannelStatusDto { ChannelId = 78, Value = 3 }, new ChannelStatusDto { ChannelId = 12, Value = 7.5f }, null),
            Tuple.Create<DateTime, ChannelStatusDto, ChannelStatusDto, DateTime?>(DateTime.Parse("7/4/2021 3:00:19.0 pm"), new ChannelStatusDto { ChannelId = 78, Value = 2 }, new ChannelStatusDto { ChannelId = 12, Value = 7.4f }, null),
            Tuple.Create<DateTime, ChannelStatusDto, ChannelStatusDto, DateTime?>(DateTime.Parse("7/4/2021 3:00:20.0 pm"), new ChannelStatusDto { ChannelId = 78, Value = 0 }, new ChannelStatusDto { ChannelId = 12, Value = 9.4f }, DateTime.Parse("7/4/2021 3:00:20.0 pm")),
            Tuple.Create<DateTime, ChannelStatusDto, ChannelStatusDto, DateTime?>(DateTime.Parse("7/4/2021 3:00:21.0 pm"), new ChannelStatusDto { ChannelId = 78, Value = 0 }, new ChannelStatusDto { ChannelId = 12, Value = 14.4f }, null),                Tuple.Create<DateTime, ChannelStatusDto, ChannelStatusDto, DateTime?>(DateTime.Parse("7/4/2021 3:00:22.0 pm"), new ChannelStatusDto { ChannelId = 78, Value = 1 }, new ChannelStatusDto { ChannelId = 12, Value = 18.8f }, null),
        };

        var currentStint = new Cache.Models.FuelRange.Stint { Start = DateTime.Parse("7/4/2021 2:00:00.0 pm"), End = null };
        foreach (var t in tuples)
        {
            dtMock.Setup(p => p.UtcNow).Returns(t.Item1);
            telem.Data = new[] { t.Item2, t.Item3 };
            var result = telemTriggers.ProcessCarTelemetry(telem, currentStint);
            Assert.AreEqual(t.Item4, result.End);

            if (result.Start > default(DateTime))
            {
                currentStint = result;
            }
        }
    }

    [TestMethod]
    public void InsufficentDataTest()
    {
        var dtMock = new Mock<IDateTimeHelper>();
        dtMock.Setup(p => p.UtcNow).Returns(DateTime.Parse("7/4/2021 1:00:00.0 pm"));

        var telemTriggers = new TelemetryTriggers(dtMock.Object);
        telemTriggers.SpeedChannel = GetSpeedChannel();
        telemTriggers.FuelLevelChannel = GetFuelLevelChannel();

        var telem = new ChannelDataSetDto { };
        var tuples = new Tuple<DateTime, ChannelStatusDto, bool>[]
        {
            Tuple.Create(DateTime.Parse("7/4/2021 2:00:00.0 pm"), new ChannelStatusDto { ChannelId = 78, Value = 0 }, false),
            Tuple.Create(DateTime.Parse("7/4/2021 2:00:01.0 pm"), new ChannelStatusDto { ChannelId = 78, Value = 0 }, true),
            Tuple.Create(DateTime.Parse("7/4/2021 2:00:02.0 pm"), new ChannelStatusDto { ChannelId = 78, Value = 1 }, true),
            Tuple.Create(DateTime.Parse("7/4/2021 2:00:03.0 pm"), new ChannelStatusDto { ChannelId = 78, Value = 4 }, true),
            Tuple.Create(DateTime.Parse("7/4/2021 2:00:10.0 pm"), new ChannelStatusDto { ChannelId = 78, Value = 24 }, true),
            Tuple.Create(DateTime.Parse("7/4/2021 2:00:11.0 pm"), new ChannelStatusDto { ChannelId = 78, Value = 28 }, true),
            Tuple.Create(DateTime.Parse("7/4/2021 2:00:43.0 pm"), new ChannelStatusDto { ChannelId = 78, Value = 33 }, false),
            Tuple.Create(DateTime.Parse("7/4/2021 2:00:46.0 pm"), new ChannelStatusDto { ChannelId = 78, Value = 48 }, true),
            Tuple.Create(DateTime.Parse("7/4/2021 2:01:47.0 pm"), new ChannelStatusDto { ChannelId = 78, Value = 50 }, false),
            Tuple.Create(DateTime.Parse("7/4/2021 2:02:48.0 pm"), new ChannelStatusDto { ChannelId = 78, Value = 55 }, false),
            Tuple.Create(DateTime.Parse("7/4/2021 2:03:49.0 pm"), new ChannelStatusDto { ChannelId = 78, Value = 60 }, false),
        };

        var currentStint = new Cache.Models.FuelRange.Stint { Start = DateTime.Parse("7/4/2021 1:00:00.0 pm"), End = DateTime.Parse("7/4/2021 2:00:00.0 pm") };
        var fuelCh = new ChannelStatusDto { ChannelId = 12, Value = 15f };
        foreach (var t in tuples)
        {
            dtMock.Setup(p => p.UtcNow).Returns(t.Item1);
            telem.Data = new[] { t.Item2, fuelCh };
            Assert.AreEqual(t.Item3, telemTriggers.IsTelemetryAvailable);
            telemTriggers.ProcessCarTelemetry(telem, currentStint);
        }
    }

    [TestMethod]
    public void NoChannelTest()
    {
        var dtMock = new Mock<IDateTimeHelper>();
        dtMock.Setup(p => p.UtcNow).Returns(DateTime.Parse("7/4/2021 1:00:00.0 pm"));

        var telemTriggers = new TelemetryTriggers(dtMock.Object);
        var telem = new ChannelDataSetDto { DeviceAppId = 1, Timestamp = DateTime.Parse("7/4/2021 1:00:00.0 pm") };
        var result = telemTriggers.ProcessCarTelemetry(telem, null);
        Assert.IsNull(result);
    }

    [TestMethod]
    public void NoChannelDataTest()
    {
        var dtMock = new Mock<IDateTimeHelper>();
        dtMock.Setup(p => p.UtcNow).Returns(DateTime.Parse("7/4/2021 1:00:00.0 pm"));

        var telemTriggers = new TelemetryTriggers(dtMock.Object);
        telemTriggers.SpeedChannel = GetSpeedChannel();
        telemTriggers.FuelLevelChannel = GetFuelLevelChannel();
        var telem = new ChannelDataSetDto { DeviceAppId = 1, Timestamp = DateTime.Parse("7/4/2021 1:00:00.0 pm") };

        // Null data
        var result = telemTriggers.ProcessCarTelemetry(telem, null);
        Assert.AreEqual(default, result.Start);
        Assert.AreEqual(null, result.End);

        // Empty data
        telem.Data = new ChannelStatusDto[0];
        result = telemTriggers.ProcessCarTelemetry(telem, null);
        Assert.AreEqual(default, result.Start);
        Assert.AreEqual(null, result.End);

        // Partial data
        telem.Data = new[] { new ChannelStatusDto { ChannelId = 78, Value = 0 } };
        result = telemTriggers.ProcessCarTelemetry(telem, null);
        Assert.AreEqual(default, result.Start);
        Assert.AreEqual(null, result.End);

        telem.Data = new[] { new ChannelStatusDto { ChannelId = 12, Value = 0 } };
        result = telemTriggers.ProcessCarTelemetry(telem, null);
        Assert.AreEqual(default, result.Start);
        Assert.AreEqual(null, result.End);
    }
}
