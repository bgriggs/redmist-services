using BigMission.Cache.Models.FuelRange;
using BigMission.Database.Models;
using BigMission.DeviceApp.Shared;
using BigMission.FuelStatistics.FuelRange;
using BigMission.TestHelpers;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using System;
using System.Linq;
using System.Threading.Tasks;
using static BigMission.RaceHeroSdk.RaceHeroClient;

namespace BigMission.FuelStatistics.Tests.FuelRange;

[TestClass]
public class CarRangeTests
{
    private ChannelMapping GetFuelLevelChannel()
    {
        return new ChannelMapping { Id = 12, DeviceAppId = 1, CanId = 251711489, ChannelName = "FuelLevel", ReservedName = "FuelLevel" };
    }
    private ChannelMapping GetSpeedChannel()
    {
        return new ChannelMapping { Id = 78, DeviceAppId = 1, CanId = 251711493, ChannelName = "Speed", ReservedName = "Speed" };
    }


    [TestMethod]
    public void StartRaceWithLaps()
    {
        var dtMock = new Mock<IDateTimeHelper>();
        dtMock.Setup(p => p.UtcNow).Returns(DateTime.Parse("7/4/2021 8:00:00.0 am"));

        var fuelRangeContextMock = new Mock<IFuelRangeContext>();
        fuelRangeContextMock.Setup(f => f.SaveTeamStint(It.IsAny<Cache.Models.FuelRange.Stint>())).Returns(Task.FromResult(new Cache.Models.FuelRange.Stint { Id = -1 }));

        var loggerMock = new Mock<ILoggerFactory>();

        var settings = new FuelRangeSetting { UseRaceHeroTrigger = true, UseTelemetry = false };

        var laps = new[]
        {
            new Lap { CurrentLap = 0, LastPitLap = 0, Timestamp = DateTime.Parse("7/4/2021 8:00:00.0 am"), LastLapTimeSeconds = 0 },
            new Lap { CurrentLap = 1, LastPitLap = 0, Timestamp = DateTime.Parse("7/4/2021 8:01:00.0 am"), LastLapTimeSeconds = 60 },
            new Lap { CurrentLap = 2, LastPitLap = 0, Timestamp = DateTime.Parse("7/4/2021 8:02:00.0 am"), LastLapTimeSeconds = 60 }
        };

        var carRange = new CarRange(settings, dtMock.Object, fuelRangeContextMock.Object, loggerMock.Object);
        carRange.ResetForNewRace(123, 4444);
        var changed = carRange.ProcessLaps(laps);
        Assert.IsTrue(changed.Result);
        var stints = carRange.GetStints();
        Assert.AreEqual(1, stints.Length);
        Assert.AreEqual(DateTime.Parse("7/4/2021 8:00:00.0 am"), stints[0].Start);
    }

    [TestMethod]
    public void StartRaceWithTelemetry()
    {
        var dtMock = new Mock<IDateTimeHelper>();
        dtMock.Setup(p => p.UtcNow).Returns(DateTime.Parse("7/4/2021 8:00:00.0 am"));

        var fuelRangeContextMock = new Mock<IFuelRangeContext>();
        fuelRangeContextMock.Setup(f => f.SaveTeamStint(It.IsAny<Cache.Models.FuelRange.Stint>())).Returns(Task.FromResult(new Cache.Models.FuelRange.Stint { Id = -1 }));

        var loggerMock = new Mock<ILoggerFactory>();

        var settings = new FuelRangeSetting { UseRaceHeroTrigger = true, UseTelemetry = true };
        var carRange = new CarRange(settings, dtMock.Object, fuelRangeContextMock.Object, loggerMock.Object)
        {
            SpeedChannel = GetSpeedChannel(),
            FuelLevelChannel = GetFuelLevelChannel()
        };
        carRange.ResetForNewRace(123, 4444);

        var data = new[]
        {
            new ChannelStatusDto { ChannelId = 78, Value = 0 },
            new ChannelStatusDto { ChannelId = 78, Value = 0 },
            new ChannelStatusDto { ChannelId = 78, Value = 1 },
            new ChannelStatusDto { ChannelId = 78, Value = 4 },
            new ChannelStatusDto { ChannelId = 78, Value = 7 },
            new ChannelStatusDto { ChannelId = 78, Value = 9 },
            new ChannelStatusDto { ChannelId = 78, Value = 10 },
            new ChannelStatusDto { ChannelId = 78, Value = 14 },
            new ChannelStatusDto { ChannelId = 78, Value = 18 },
            new ChannelStatusDto { ChannelId = 78, Value = 22 },
            new ChannelStatusDto { ChannelId = 78, Value = 24 },
            new ChannelStatusDto { ChannelId = 78, Value = 28 },
            new ChannelStatusDto { ChannelId = 78, Value = 33 },
            new ChannelStatusDto { ChannelId = 78, Value = 37 },
            new ChannelStatusDto { ChannelId = 78, Value = 33 },
            new ChannelStatusDto { ChannelId = 78, Value = 45 },
            new ChannelStatusDto { ChannelId = 78, Value = 48 },
        };

        var timestamps = new[]
        {
            DateTime.Parse("7/4/2021 8:00:00.0 am"),
            DateTime.Parse("7/4/2021 8:00:01.0 am"),
            DateTime.Parse("7/4/2021 8:00:02.0 am"),
            DateTime.Parse("7/4/2021 8:00:03.0 am"),
            DateTime.Parse("7/4/2021 8:00:04.0 am"),
            DateTime.Parse("7/4/2021 8:00:05.0 am"),
            DateTime.Parse("7/4/2021 8:00:06.0 am"),
            DateTime.Parse("7/4/2021 8:00:07.0 am"),
            DateTime.Parse("7/4/2021 8:00:08.0 am"),
            DateTime.Parse("7/4/2021 8:00:09.0 am"),
            DateTime.Parse("7/4/2021 8:00:10.0 am"),
            DateTime.Parse("7/4/2021 8:00:11.0 am"),
            DateTime.Parse("7/4/2021 8:00:12.0 am"),
            DateTime.Parse("7/4/2021 8:00:13.0 am"),
            DateTime.Parse("7/4/2021 8:00:14.0 am"),
            DateTime.Parse("7/4/2021 8:00:15.0 am"),
            DateTime.Parse("7/4/2021 8:00:16.0 am"),
        };

        var ds = new ChannelDataSetDto { };
        var fuelCh = new ChannelStatusDto { ChannelId = 12, Value = 15f };
        for (int i = 0; i < data.Length - 1; i++)
        {
            ds.Data = new[] { data[i], fuelCh };
            dtMock.Setup(p => p.UtcNow).Returns(timestamps[i]);
            var changed = carRange.ProcessTelemetry(ds);
            Assert.IsFalse(changed.Result);
            var stints = carRange.GetStints();
            Assert.AreEqual(0, stints.Length);
        }

        ds.Data = new[] { data.Last(), fuelCh };
        dtMock.Setup(p => p.UtcNow).Returns(timestamps.Last());
        var changed2 = carRange.ProcessTelemetry(ds);
        Assert.IsTrue(changed2.Result);
        var stints2 = carRange.GetStints();
        Assert.AreEqual(1, stints2.Length);
        Assert.AreEqual(timestamps.Last(), stints2[0].Start);
    }

    [TestMethod]
    public void FallBackToLaps()
    {
        var dtMock = new Mock<IDateTimeHelper>();
        dtMock.Setup(p => p.UtcNow).Returns(DateTime.Parse("7/4/2021 8:00:00.0 am"));

        var fuelRangeContextMock = new Mock<IFuelRangeContext>();
        fuelRangeContextMock.Setup(f => f.SaveTeamStint(It.IsAny<Cache.Models.FuelRange.Stint>())).Returns(Task.FromResult(new Cache.Models.FuelRange.Stint { Id = -1 }));

        var loggerMock = new Mock<ILoggerFactory>();

        var settings = new FuelRangeSetting { UseRaceHeroTrigger = true, UseTelemetry = true };

        var telemTimestamps = new[]
        {
            DateTime.Parse("7/4/2021 8:00:00.0 am"),
            DateTime.Parse("7/4/2021 8:00:01.0 am"),
            DateTime.Parse("7/4/2021 8:00:04.0 am"),
            DateTime.Parse("7/4/2021 8:00:05.0 am"),
            DateTime.Parse("7/4/2021 8:00:06.0 am"),
            DateTime.Parse("7/4/2021 8:00:10.0 am"),
            DateTime.Parse("7/4/2021 8:00:11.0 am"),
            DateTime.Parse("7/4/2021 8:00:12.0 am"),
            DateTime.Parse("7/4/2021 8:00:16.0 am"),
        };
        var speedCh = new ChannelStatusDto { ChannelId = 78, Value = 80 };
        var fuelCh = new ChannelStatusDto { ChannelId = 12, Value = 15f };
        var ds = new ChannelDataSetDto { Data = new[] { fuelCh, speedCh } };

        // Init event
        var carRange = new CarRange(settings, dtMock.Object, fuelRangeContextMock.Object, loggerMock.Object);
        carRange.ResetForNewRace(123, 4444);

        // Start the race with telemetry
        for (int i = 0; i < telemTimestamps.Length; i++)
        {
            dtMock.Setup(p => p.UtcNow).Returns(telemTimestamps[i]);
            carRange.ProcessTelemetry(ds).Wait();
        }

        // Telemetry stops after 16 seconds, next run the laps to take over
        var laps = new[]
        {
            new Lap { CurrentLap = 5, LastPitLap = 0, Timestamp = DateTime.Parse("7/4/2021 8:05:00.0 am"), LastLapTimeSeconds = 60 },
            new Lap { CurrentLap = 6, LastPitLap = 0, Timestamp = DateTime.Parse("7/4/2021 8:06:00.0 am"), LastLapTimeSeconds = 60 },
            new Lap { CurrentLap = 7, LastPitLap = 0, Timestamp = DateTime.Parse("7/4/2021 8:07:00.0 am"), LastLapTimeSeconds = 60 },
            new Lap { CurrentLap = 8, LastPitLap = 0, Timestamp = DateTime.Parse("7/4/2021 8:08:00.0 am"), LastLapTimeSeconds = 60 },
            new Lap { CurrentLap = 9, LastPitLap = 9, Timestamp = DateTime.Parse("7/4/2021 8:09:00.0 am"), LastLapTimeSeconds = 60 },
            new Lap { CurrentLap = 10, LastPitLap = 9, Timestamp = DateTime.Parse("7/4/2021 8:10:00.0 am"), LastLapTimeSeconds = 60 },
        };

        var changed = carRange.ProcessLaps(laps);
        Assert.IsTrue(changed.Result);
        var stints = carRange.GetStints();
        Assert.AreEqual(2, stints.Length);
        Assert.AreEqual(DateTime.Parse("7/4/2021 8:09:00.0 am"), stints[1].Start);
    }

    [TestMethod]
    public void EndTelemRaceWithLaps()
    {
        var dtMock = new Mock<IDateTimeHelper>();
        dtMock.Setup(p => p.UtcNow).Returns(DateTime.Parse("7/4/2021 8:00:00.0 am"));

        var fuelRangeContextMock = new Mock<IFuelRangeContext>();
        fuelRangeContextMock.Setup(f => f.SaveTeamStint(It.IsAny<Cache.Models.FuelRange.Stint>())).Returns(Task.FromResult(new Cache.Models.FuelRange.Stint { Id = -1 }));

        var loggerMock = new Mock<ILoggerFactory>();

        var settings = new FuelRangeSetting { UseRaceHeroTrigger = true, UseTelemetry = true };

        var telemTimestamps = new[]
        {
            DateTime.Parse("7/4/2021 8:00:00.0 am"),
            DateTime.Parse("7/4/2021 8:00:01.0 am"),
            DateTime.Parse("7/4/2021 8:00:04.0 am"),
            DateTime.Parse("7/4/2021 8:00:05.0 am"),
            DateTime.Parse("7/4/2021 8:00:06.0 am"),
            DateTime.Parse("7/4/2021 8:00:10.0 am"),
            DateTime.Parse("7/4/2021 8:00:11.0 am"),
            DateTime.Parse("7/4/2021 8:00:12.0 am"),
            DateTime.Parse("7/4/2021 8:00:16.0 am"),
        };
        var speedCh = new ChannelStatusDto { ChannelId = 78, Value = 80 };
        var fuelCh = new ChannelStatusDto { ChannelId = 12, Value = 15f };
        var ds = new ChannelDataSetDto { Data = new[] { fuelCh, speedCh } };

        // Init event
        var carRange = new CarRange(settings, dtMock.Object, fuelRangeContextMock.Object, loggerMock.Object);
        carRange.ResetForNewRace(123, 4444);

        // Start the race with telemetry
        for (int i = 0; i < telemTimestamps.Length; i++)
        {
            dtMock.Setup(p => p.UtcNow).Returns(telemTimestamps[i]);
            carRange.ProcessTelemetry(ds).Wait();
        }

        // Telemetry stops after 16 seconds, next run the laps to take over
        var laps = new[]
        {
            new Lap { CurrentLap = 50, LastPitLap = 10, Timestamp = DateTime.Parse("7/4/2021 3:05:00.0 pm"), LastLapTimeSeconds = 60 },
            new Lap { CurrentLap = 51, LastPitLap = 51, Timestamp = DateTime.Parse("7/4/2021 3:06:00.0 pm"), LastLapTimeSeconds = 60 },
            new Lap { CurrentLap = 52, LastPitLap = 51, Timestamp = DateTime.Parse("7/4/2021 3:07:00.0 pm"), LastLapTimeSeconds = 60 },
            new Lap { CurrentLap = 53, LastPitLap = 51, Timestamp = DateTime.Parse("7/4/2021 3:08:00.0 pm"), LastLapTimeSeconds = 60 },
            new Lap { CurrentLap = 54, LastPitLap = 51, Timestamp = DateTime.Parse("7/4/2021 3:09:00.0 pm"), LastLapTimeSeconds = 60 },
            new Lap { CurrentLap = 55, LastPitLap = 51, Timestamp = DateTime.Parse("7/4/2021 3:10:00.0 pm"), LastLapTimeSeconds = 60, Flag = (byte)Flag.Finish },
        };

        var changed = carRange.ProcessLaps(laps);
        Assert.IsTrue(changed.Result);
        var stints = carRange.GetStints();
        Assert.AreEqual(2, stints.Length);
        Assert.AreEqual(DateTime.Parse("7/4/2021 3:10:00.0 pm"), stints[1].End);
    }

    [TestMethod]
    public void ResetForNewRace()
    {
        var dtMock = new Mock<IDateTimeHelper>();
        dtMock.Setup(p => p.UtcNow).Returns(DateTime.Parse("7/4/2021 8:00:00.0 am"));

        var fuelRangeContextMock = new Mock<IFuelRangeContext>();
        fuelRangeContextMock.Setup(f => f.SaveTeamStint(It.IsAny<Cache.Models.FuelRange.Stint>())).Returns(Task.FromResult(new Cache.Models.FuelRange.Stint { Id = -1 }));

        var loggerMock = new Mock<ILoggerFactory>();

        var settings = new FuelRangeSetting { UseRaceHeroTrigger = true, UseTelemetry = true };
        var carRange = new CarRange(settings, dtMock.Object, fuelRangeContextMock.Object, loggerMock.Object);
        carRange.ResetForNewRace(123, 4444);
        var laps = new[]
        {
            new Lap { CurrentLap = 50, LastPitLap = 10, Timestamp = DateTime.Parse("7/4/2021 3:05:00.0 pm"), LastLapTimeSeconds = 60 },
            new Lap { CurrentLap = 51, LastPitLap = 51, Timestamp = DateTime.Parse("7/4/2021 3:06:00.0 pm"), LastLapTimeSeconds = 60 },
            new Lap { CurrentLap = 52, LastPitLap = 51, Timestamp = DateTime.Parse("7/4/2021 3:07:00.0 pm"), LastLapTimeSeconds = 60 },
            new Lap { CurrentLap = 53, LastPitLap = 51, Timestamp = DateTime.Parse("7/4/2021 3:08:00.0 pm"), LastLapTimeSeconds = 60 },
            new Lap { CurrentLap = 54, LastPitLap = 51, Timestamp = DateTime.Parse("7/4/2021 3:09:00.0 pm"), LastLapTimeSeconds = 60 },
            new Lap { CurrentLap = 55, LastPitLap = 51, Timestamp = DateTime.Parse("7/4/2021 3:10:00.0 pm"), LastLapTimeSeconds = 60, Flag = (byte)Flag.Finish },
        };

        var changed = carRange.ProcessLaps(laps);
        Assert.IsTrue(changed.Result);
        var stints = carRange.GetStints();
        Assert.AreEqual(2, stints.Length);

        // Reset
        carRange.ResetForNewRace(123, 4445);

        laps = new[]
        {
            new Lap { CurrentLap = 0, LastPitLap = 0, Timestamp = DateTime.Parse("7/5/2021 3:05:00.0 pm"), LastLapTimeSeconds = 60 },
            new Lap { CurrentLap = 1, LastPitLap = 0, Timestamp = DateTime.Parse("7/5/2021 3:06:00.0 pm"), LastLapTimeSeconds = 60 },
            new Lap { CurrentLap = 2, LastPitLap = 0, Timestamp = DateTime.Parse("7/5/2021 3:07:00.0 pm"), LastLapTimeSeconds = 60 },
            new Lap { CurrentLap = 3, LastPitLap = 0, Timestamp = DateTime.Parse("7/5/2021 3:08:00.0 pm"), LastLapTimeSeconds = 60 },
        };

        changed = carRange.ProcessLaps(laps);
        Assert.IsTrue(changed.Result);
        stints = carRange.GetStints();
        Assert.AreEqual(1, stints.Length);
    }
}
