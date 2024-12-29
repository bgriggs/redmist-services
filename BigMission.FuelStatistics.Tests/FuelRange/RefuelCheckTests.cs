using BigMission.DeviceApp.Shared;
using BigMission.TestHelpers;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using System;

namespace BigMission.FuelStatistics.Tests.FuelRange;

[TestClass]
public class RefuelCheckTests
{
    [TestMethod]
    public void RefuelPositiveTest()
    {
        var dtMock = new Mock<IDateTimeHelper>();
        dtMock.Setup(p => p.UtcNow).Returns(DateTime.Parse("7/4/2021 1:00:00.0 pm"));

        var data = new Tuple<int, float, DateTime, bool>[] 
        {
            // Pit lane @ 35  slowing...
            Tuple.Create(35, 4f, DateTime.Parse("7/4/2021 1:00:00.0 pm"), false),
            Tuple.Create(30, 4.5f, DateTime.Parse("7/4/2021 1:00:01.0 pm"), false),
            Tuple.Create(25, 3.5f, DateTime.Parse("7/4/2021 1:00:02.0 pm"), false),
            Tuple.Create(10, 3.5f, DateTime.Parse("7/4/2021 1:00:03.0 pm"), false),
            Tuple.Create(7, 5.5f, DateTime.Parse("7/4/2021 1:00:04.0 pm"), false),
            Tuple.Create(6, 4.5f, DateTime.Parse("7/4/2021 1:00:05.0 pm"), false),
            // Stopping...
            Tuple.Create(5, 6.5f, DateTime.Parse("7/4/2021 1:00:06.0 pm"), false),
            Tuple.Create(4, 4.5f, DateTime.Parse("7/4/2021 1:00:07.0 pm"), false),
            Tuple.Create(3, 3.5f, DateTime.Parse("7/4/2021 1:00:08.0 pm"), false),
            Tuple.Create(2, 2.5f, DateTime.Parse("7/4/2021 1:00:09.0 pm"), false),
            Tuple.Create(1, 3.5f, DateTime.Parse("7/4/2021 1:00:10.0 pm"), false),
            Tuple.Create(2, 3.5f, DateTime.Parse("7/4/2021 1:00:11.0 pm"), false),
            Tuple.Create(1, 3.6f, DateTime.Parse("7/4/2021 1:00:12.0 pm"), false),
            Tuple.Create(2, 3.7f, DateTime.Parse("7/4/2021 1:00:13.0 pm"), false),
            Tuple.Create(1, 3.5f, DateTime.Parse("7/4/2021 1:00:14.0 pm"), false),
            // Fueling started...
            Tuple.Create(2, 3.6f, DateTime.Parse("7/4/2021 1:00:15.0 pm"), false),
            Tuple.Create(1, 4.0f, DateTime.Parse("7/4/2021 1:00:17.0 pm"), false),
            Tuple.Create(2, 5.6f, DateTime.Parse("7/4/2021 1:00:18.0 pm"), false),
            // Triggger
            Tuple.Create(1, 6.4f, DateTime.Parse("7/4/2021 1:00:19.0 pm"), true),
            Tuple.Create(2, 7.3f, DateTime.Parse("7/4/2021 1:00:20.0 pm"), false),
            Tuple.Create(0, 8.3f, DateTime.Parse("7/4/2021 1:00:22.0 pm"), false),
            Tuple.Create(0, 9.1f, DateTime.Parse("7/4/2021 1:00:23.0 pm"), false),
            Tuple.Create(0, 10.5f, DateTime.Parse("7/4/2021 1:00:24.0 pm"), false),
            Tuple.Create(0, 12.3f, DateTime.Parse("7/4/2021 1:00:25.0 pm"), false),
            Tuple.Create(0, 14.0f, DateTime.Parse("7/4/2021 1:00:27.0 pm"), false),
            Tuple.Create(0, 16.3f, DateTime.Parse("7/4/2021 1:00:28.0 pm"), false),
            Tuple.Create(0, 18.8f, DateTime.Parse("7/4/2021 1:00:29.0 pm"), false),
            Tuple.Create(0, 18.8f, DateTime.Parse("7/4/2021 1:00:30.0 pm"), false),
            Tuple.Create(0, 18.8f, DateTime.Parse("7/4/2021 1:00:31.0 pm"), false),
            // Leave pits...
            Tuple.Create(3, 18.8f, DateTime.Parse("7/4/2021 1:00:32.0 pm"), false),
            Tuple.Create(10, 18.8f, DateTime.Parse("7/4/2021 1:00:34.0 pm"), false),
            Tuple.Create(15, 18.8f, DateTime.Parse("7/4/2021 1:00:35.0 pm"), false),
            Tuple.Create(25, 18.8f, DateTime.Parse("7/4/2021 1:00:36.0 pm"), false),
            Tuple.Create(35, 18.8f, DateTime.Parse("7/4/2021 1:00:38.0 pm"), false),
        };

        var rc = new RefuelCheck(dtMock.Object);

        foreach(var d in data)
        {
            dtMock.Setup(p => p.UtcNow).Returns(d.Item3);
            var result = rc.IsRefuling(d.Item1, d.Item2);
            Assert.AreEqual(d.Item4, result);
        }
    }

    [TestMethod]
    public void StoppedWithoutRefulingTest()
    {
        var dtMock = new Mock<IDateTimeHelper>();
        dtMock.Setup(p => p.UtcNow).Returns(DateTime.Parse("7/4/2021 1:00:00.0 pm"));

        var data = new Tuple<int, float, DateTime, bool>[]
        {
            // Pit lane @ 35  slowing...
            Tuple.Create(35, 4f, DateTime.Parse("7/4/2021 1:00:00.0 pm"), false),
            Tuple.Create(30, 4.5f, DateTime.Parse("7/4/2021 1:00:01.0 pm"), false),
            Tuple.Create(25, 3.5f, DateTime.Parse("7/4/2021 1:00:02.0 pm"), false),
            Tuple.Create(10, 3.5f, DateTime.Parse("7/4/2021 1:00:03.0 pm"), false),
            Tuple.Create(7, 5.5f, DateTime.Parse("7/4/2021 1:00:04.0 pm"), false),
            Tuple.Create(6, 4.5f, DateTime.Parse("7/4/2021 1:00:05.0 pm"), false),
            // Stopping...
            Tuple.Create(5, 6.5f, DateTime.Parse("7/4/2021 1:00:06.0 pm"), false),
            Tuple.Create(4, 4.5f, DateTime.Parse("7/4/2021 1:00:07.0 pm"), false),
            Tuple.Create(3, 3.5f, DateTime.Parse("7/4/2021 1:00:08.0 pm"), false),
            Tuple.Create(2, 2.5f, DateTime.Parse("7/4/2021 1:00:09.0 pm"), false),
            Tuple.Create(1, 3.5f, DateTime.Parse("7/4/2021 1:00:10.0 pm"), false),
            Tuple.Create(2, 3.5f, DateTime.Parse("7/4/2021 1:00:11.0 pm"), false),
            Tuple.Create(1, 3.6f, DateTime.Parse("7/4/2021 1:00:12.0 pm"), false),
            Tuple.Create(2, 3.7f, DateTime.Parse("7/4/2021 1:00:13.0 pm"), false),
            Tuple.Create(1, 3.5f, DateTime.Parse("7/4/2021 1:00:14.0 pm"), false),
            Tuple.Create(2, 3.6f, DateTime.Parse("7/4/2021 1:00:15.0 pm"), false),
            Tuple.Create(1, 4.0f, DateTime.Parse("7/4/2021 1:00:17.0 pm"), false),
            Tuple.Create(2, 5.6f, DateTime.Parse("7/4/2021 1:00:18.0 pm"), false),
            Tuple.Create(1, 3.4f, DateTime.Parse("7/4/2021 1:00:19.0 pm"), false),
            Tuple.Create(2, 3.3f, DateTime.Parse("7/4/2021 1:00:20.0 pm"), false),
            Tuple.Create(0, 3.3f, DateTime.Parse("7/4/2021 1:00:22.0 pm"), false),
            Tuple.Create(0, 4.1f, DateTime.Parse("7/4/2021 1:00:23.0 pm"), false),
            Tuple.Create(0, 2.5f, DateTime.Parse("7/4/2021 1:00:24.0 pm"), false),
            Tuple.Create(0, 2.3f, DateTime.Parse("7/4/2021 1:00:25.0 pm"), false),
            Tuple.Create(0, 3.0f, DateTime.Parse("7/4/2021 1:00:27.0 pm"), false),
            Tuple.Create(0, 3.3f, DateTime.Parse("7/4/2021 1:00:28.0 pm"), false),
            Tuple.Create(0, 3.8f, DateTime.Parse("7/4/2021 1:00:29.0 pm"), false),
            Tuple.Create(0, 3.8f, DateTime.Parse("7/4/2021 1:00:30.0 pm"), false),
            Tuple.Create(0, 4.8f, DateTime.Parse("7/4/2021 1:00:31.0 pm"), false),
            // Leave pits...
            Tuple.Create(3, 2.8f, DateTime.Parse("7/4/2021 1:00:32.0 pm"), false),
            Tuple.Create(10, 3.8f, DateTime.Parse("7/4/2021 1:00:34.0 pm"), false),
            Tuple.Create(15, 3.8f, DateTime.Parse("7/4/2021 1:00:35.0 pm"), false),
            Tuple.Create(25, 5.8f, DateTime.Parse("7/4/2021 1:00:36.0 pm"), false),
            Tuple.Create(35, 6.8f, DateTime.Parse("7/4/2021 1:00:38.0 pm"), false),
        };

        var rc = new RefuelCheck(dtMock.Object);

        foreach (var d in data)
        {
            dtMock.Setup(p => p.UtcNow).Returns(d.Item3);
            var result = rc.IsRefuling(d.Item1, d.Item2);
            Assert.AreEqual(d.Item4, result);
        }
    }

    [TestMethod]
    public void RefulingWhileMovingTest()
    {
        var dtMock = new Mock<IDateTimeHelper>();
        dtMock.Setup(p => p.UtcNow).Returns(DateTime.Parse("7/4/2021 1:00:00.0 pm"));

        var data = new Tuple<int, float, DateTime, bool>[]
        {
            Tuple.Create(35, 4f, DateTime.Parse("7/4/2021 1:00:00.0 pm"), false),
            Tuple.Create(30, 4.5f, DateTime.Parse("7/4/2021 1:00:01.0 pm"), false),
            Tuple.Create(25, 3.5f, DateTime.Parse("7/4/2021 1:00:02.0 pm"), false),
            Tuple.Create(10, 3.5f, DateTime.Parse("7/4/2021 1:00:03.0 pm"), false),
            Tuple.Create(17, 5.5f, DateTime.Parse("7/4/2021 1:00:04.0 pm"), false),
            Tuple.Create(16, 4.5f, DateTime.Parse("7/4/2021 1:00:05.0 pm"), false),
            Tuple.Create(15, 6.5f, DateTime.Parse("7/4/2021 1:00:06.0 pm"), false),
            Tuple.Create(14, 4.5f, DateTime.Parse("7/4/2021 1:00:07.0 pm"), false),
            Tuple.Create(13, 3.5f, DateTime.Parse("7/4/2021 1:00:08.0 pm"), false),
            Tuple.Create(12, 2.5f, DateTime.Parse("7/4/2021 1:00:09.0 pm"), false),
            Tuple.Create(11, 3.5f, DateTime.Parse("7/4/2021 1:00:10.0 pm"), false),
            Tuple.Create(12, 3.5f, DateTime.Parse("7/4/2021 1:00:11.0 pm"), false),
            Tuple.Create(11, 3.6f, DateTime.Parse("7/4/2021 1:00:12.0 pm"), false),
            Tuple.Create(21, 3.7f, DateTime.Parse("7/4/2021 1:00:13.0 pm"), false),
            Tuple.Create(11, 3.5f, DateTime.Parse("7/4/2021 1:00:14.0 pm"), false),
            // Fueling started...
            Tuple.Create(12, 3.6f, DateTime.Parse("7/4/2021 1:00:15.0 pm"), false),
            Tuple.Create(11, 4.0f, DateTime.Parse("7/4/2021 1:00:17.0 pm"), false),
            Tuple.Create(12, 5.6f, DateTime.Parse("7/4/2021 1:00:18.0 pm"), false),
            Tuple.Create(11, 6.4f, DateTime.Parse("7/4/2021 1:00:19.0 pm"), false),
            Tuple.Create(12, 7.3f, DateTime.Parse("7/4/2021 1:00:20.0 pm"), false),
            Tuple.Create(10, 8.3f, DateTime.Parse("7/4/2021 1:00:22.0 pm"), false),
            Tuple.Create(10, 9.1f, DateTime.Parse("7/4/2021 1:00:23.0 pm"), false),
            Tuple.Create(10, 10.5f, DateTime.Parse("7/4/2021 1:00:24.0 pm"), false),
            Tuple.Create(10, 12.3f, DateTime.Parse("7/4/2021 1:00:25.0 pm"), false),
            Tuple.Create(10, 14.0f, DateTime.Parse("7/4/2021 1:00:27.0 pm"), false),
            Tuple.Create(10, 16.3f, DateTime.Parse("7/4/2021 1:00:28.0 pm"), false),
            Tuple.Create(10, 18.8f, DateTime.Parse("7/4/2021 1:00:29.0 pm"), false),
            Tuple.Create(10, 18.8f, DateTime.Parse("7/4/2021 1:00:30.0 pm"), false),
            Tuple.Create(10, 18.8f, DateTime.Parse("7/4/2021 1:00:31.0 pm"), false),
            Tuple.Create(13, 18.8f, DateTime.Parse("7/4/2021 1:00:32.0 pm"), false),
            Tuple.Create(10, 18.8f, DateTime.Parse("7/4/2021 1:00:34.0 pm"), false),
            Tuple.Create(15, 18.8f, DateTime.Parse("7/4/2021 1:00:35.0 pm"), false),
            Tuple.Create(25, 18.8f, DateTime.Parse("7/4/2021 1:00:36.0 pm"), false),
            Tuple.Create(35, 18.8f, DateTime.Parse("7/4/2021 1:00:38.0 pm"), false),
        };

        var rc = new RefuelCheck(dtMock.Object);

        foreach (var d in data)
        {
            dtMock.Setup(p => p.UtcNow).Returns(d.Item3);
            var result = rc.IsRefuling(d.Item1, d.Item2);
            Assert.AreEqual(d.Item4, result);
        }
    }

    [TestMethod]
    public void StopsThenStartsTest()
    {
        var dtMock = new Mock<IDateTimeHelper>();
        dtMock.Setup(p => p.UtcNow).Returns(DateTime.Parse("7/4/2021 1:00:00.0 pm"));

        var data = new Tuple<int, float, DateTime, bool>[]
        {
            Tuple.Create(35, 4f, DateTime.Parse("7/4/2021 1:00:00.0 pm"), false),
            Tuple.Create(30, 4.5f, DateTime.Parse("7/4/2021 1:00:01.0 pm"), false),
            Tuple.Create(25, 3.5f, DateTime.Parse("7/4/2021 1:00:02.0 pm"), false),
            Tuple.Create(10, 3.5f, DateTime.Parse("7/4/2021 1:00:03.0 pm"), false),
            Tuple.Create(17, 5.5f, DateTime.Parse("7/4/2021 1:00:04.0 pm"), false),
            Tuple.Create(16, 4.5f, DateTime.Parse("7/4/2021 1:00:05.0 pm"), false),
            Tuple.Create(15, 6.5f, DateTime.Parse("7/4/2021 1:00:06.0 pm"), false),
            Tuple.Create(14, 4.5f, DateTime.Parse("7/4/2021 1:00:07.0 pm"), false),
            Tuple.Create(13, 3.5f, DateTime.Parse("7/4/2021 1:00:08.0 pm"), false),
            Tuple.Create(12, 2.5f, DateTime.Parse("7/4/2021 1:00:09.0 pm"), false),
            Tuple.Create(11, 3.5f, DateTime.Parse("7/4/2021 1:00:10.0 pm"), false),
            Tuple.Create(12, 3.5f, DateTime.Parse("7/4/2021 1:00:11.0 pm"), false),
            Tuple.Create(2, 3.6f, DateTime.Parse("7/4/2021 1:00:12.0 pm"), false),
            Tuple.Create(2, 3.7f, DateTime.Parse("7/4/2021 1:00:13.0 pm"), false),
            Tuple.Create(2, 3.5f, DateTime.Parse("7/4/2021 1:00:14.0 pm"), false),
            // Fueling started...
            Tuple.Create(2, 3.6f, DateTime.Parse("7/4/2021 1:00:15.0 pm"), false),
            Tuple.Create(1, 4.0f, DateTime.Parse("7/4/2021 1:00:17.0 pm"), false),
            Tuple.Create(1, 5.6f, DateTime.Parse("7/4/2021 1:00:18.0 pm"), false),
            Tuple.Create(11, 6.4f, DateTime.Parse("7/4/2021 1:00:19.0 pm"), false),
            Tuple.Create(12, 7.3f, DateTime.Parse("7/4/2021 1:00:20.0 pm"), false),
            Tuple.Create(10, 8.3f, DateTime.Parse("7/4/2021 1:00:22.0 pm"), false),
            Tuple.Create(10, 9.1f, DateTime.Parse("7/4/2021 1:00:23.0 pm"), false),
            Tuple.Create(10, 10.5f, DateTime.Parse("7/4/2021 1:00:24.0 pm"), false),
            Tuple.Create(10, 12.3f, DateTime.Parse("7/4/2021 1:00:25.0 pm"), false),
            Tuple.Create(10, 14.0f, DateTime.Parse("7/4/2021 1:00:27.0 pm"), false),
            Tuple.Create(10, 16.3f, DateTime.Parse("7/4/2021 1:00:28.0 pm"), false),
            Tuple.Create(10, 18.8f, DateTime.Parse("7/4/2021 1:00:29.0 pm"), false),
            Tuple.Create(10, 18.8f, DateTime.Parse("7/4/2021 1:00:30.0 pm"), false),
            Tuple.Create(10, 18.8f, DateTime.Parse("7/4/2021 1:00:31.0 pm"), false),
            Tuple.Create(13, 18.8f, DateTime.Parse("7/4/2021 1:00:32.0 pm"), false),
            Tuple.Create(10, 18.8f, DateTime.Parse("7/4/2021 1:00:34.0 pm"), false),
            Tuple.Create(15, 18.8f, DateTime.Parse("7/4/2021 1:00:35.0 pm"), false),
            Tuple.Create(25, 18.8f, DateTime.Parse("7/4/2021 1:00:36.0 pm"), false),
            Tuple.Create(35, 18.8f, DateTime.Parse("7/4/2021 1:00:38.0 pm"), false),
        };

        var rc = new RefuelCheck(dtMock.Object);

        foreach (var d in data)
        {
            dtMock.Setup(p => p.UtcNow).Returns(d.Item3);
            var result = rc.IsRefuling(d.Item1, d.Item2);
            Assert.AreEqual(d.Item4, result);
        }
    }

    /// <summary>
    /// For for when another fueling event is happening but it's too quick, i.e. < 15 mins.
    /// This mostly makes sure there are not multiple events while you're fueling at a single stop.
    /// </summary>
    [TestMethod]
    public void MultpleRefuelWithin15Test()
    {
        var dtMock = new Mock<IDateTimeHelper>();
        dtMock.Setup(p => p.UtcNow).Returns(DateTime.Parse("7/4/2021 1:00:00.0 pm"));

        var data = new Tuple<int, float, DateTime, bool>[]
        {
            // Pit lane @ 35  slowing...
            Tuple.Create(35, 4f, DateTime.Parse("7/4/2021 1:00:00.0 pm"), false),
            Tuple.Create(30, 4.5f, DateTime.Parse("7/4/2021 1:00:01.0 pm"), false),
            Tuple.Create(25, 3.5f, DateTime.Parse("7/4/2021 1:00:02.0 pm"), false),
            Tuple.Create(10, 3.5f, DateTime.Parse("7/4/2021 1:00:03.0 pm"), false),
            Tuple.Create(7, 5.5f, DateTime.Parse("7/4/2021 1:00:04.0 pm"), false),
            Tuple.Create(6, 4.5f, DateTime.Parse("7/4/2021 1:00:05.0 pm"), false),
            // Stopping...
            Tuple.Create(5, 6.5f, DateTime.Parse("7/4/2021 1:00:06.0 pm"), false),
            Tuple.Create(4, 4.5f, DateTime.Parse("7/4/2021 1:00:07.0 pm"), false),
            Tuple.Create(3, 3.5f, DateTime.Parse("7/4/2021 1:00:08.0 pm"), false),
            Tuple.Create(2, 2.5f, DateTime.Parse("7/4/2021 1:00:09.0 pm"), false),
            Tuple.Create(1, 3.5f, DateTime.Parse("7/4/2021 1:00:10.0 pm"), false),
            Tuple.Create(2, 3.5f, DateTime.Parse("7/4/2021 1:00:11.0 pm"), false),
            Tuple.Create(1, 3.6f, DateTime.Parse("7/4/2021 1:00:12.0 pm"), false),
            Tuple.Create(2, 3.7f, DateTime.Parse("7/4/2021 1:00:13.0 pm"), false),
            Tuple.Create(1, 3.5f, DateTime.Parse("7/4/2021 1:00:14.0 pm"), false),
            // Fueling started...
            Tuple.Create(2, 3.6f, DateTime.Parse("7/4/2021 1:00:15.0 pm"), false),
            Tuple.Create(1, 4.0f, DateTime.Parse("7/4/2021 1:00:17.0 pm"), false),
            Tuple.Create(2, 5.6f, DateTime.Parse("7/4/2021 1:00:18.0 pm"), false),
            // Triggger
            Tuple.Create(1, 6.4f, DateTime.Parse("7/4/2021 1:00:19.0 pm"), true),
            Tuple.Create(2, 7.3f, DateTime.Parse("7/4/2021 1:00:20.0 pm"), false),
            Tuple.Create(0, 8.3f, DateTime.Parse("7/4/2021 1:00:22.0 pm"), false),
            Tuple.Create(0, 9.1f, DateTime.Parse("7/4/2021 1:00:23.0 pm"), false),
            Tuple.Create(0, 10.5f, DateTime.Parse("7/4/2021 1:00:24.0 pm"), false),
            Tuple.Create(0, 12.3f, DateTime.Parse("7/4/2021 1:00:25.0 pm"), false),
            Tuple.Create(0, 14.0f, DateTime.Parse("7/4/2021 1:00:27.0 pm"), false),
            Tuple.Create(0, 16.3f, DateTime.Parse("7/4/2021 1:00:28.0 pm"), false),
            Tuple.Create(0, 18.8f, DateTime.Parse("7/4/2021 1:00:29.0 pm"), false),
            Tuple.Create(0, 18.8f, DateTime.Parse("7/4/2021 1:00:30.0 pm"), false),
            Tuple.Create(0, 18.8f, DateTime.Parse("7/4/2021 1:00:31.0 pm"), false),
            // Leave pits...
            Tuple.Create(3, 18.8f, DateTime.Parse("7/4/2021 1:00:32.0 pm"), false),
            Tuple.Create(10, 18.8f, DateTime.Parse("7/4/2021 1:00:34.0 pm"), false),
            Tuple.Create(15, 18.8f, DateTime.Parse("7/4/2021 1:00:35.0 pm"), false),
            Tuple.Create(25, 18.8f, DateTime.Parse("7/4/2021 1:00:36.0 pm"), false),
            Tuple.Create(35, 18.8f, DateTime.Parse("7/4/2021 1:00:38.0 pm"), false),


            // Pit lane @ 35  slowing...
            Tuple.Create(35, 4f, DateTime.Parse("7/4/2021 1:10:00.0 pm"), false),
            Tuple.Create(30, 4.5f, DateTime.Parse("7/4/2021 1:10:01.0 pm"), false),
            Tuple.Create(25, 3.5f, DateTime.Parse("7/4/2021 1:10:02.0 pm"), false),
            Tuple.Create(10, 3.5f, DateTime.Parse("7/4/2021 1:10:03.0 pm"), false),
            Tuple.Create(7, 5.5f, DateTime.Parse("7/4/2021 1:10:04.0 pm"), false),
            Tuple.Create(6, 4.5f, DateTime.Parse("7/4/2021 1:10:05.0 pm"), false),
            // Stopping...
            Tuple.Create(5, 6.5f, DateTime.Parse("7/4/2021 1:10:06.0 pm"), false),
            Tuple.Create(4, 4.5f, DateTime.Parse("7/4/2021 1:10:07.0 pm"), false),
            Tuple.Create(3, 3.5f, DateTime.Parse("7/4/2021 1:10:08.0 pm"), false),
            Tuple.Create(2, 2.5f, DateTime.Parse("7/4/2021 1:10:09.0 pm"), false),
            Tuple.Create(1, 3.5f, DateTime.Parse("7/4/2021 1:10:10.0 pm"), false),
            Tuple.Create(2, 3.5f, DateTime.Parse("7/4/2021 1:10:11.0 pm"), false),
            Tuple.Create(1, 3.6f, DateTime.Parse("7/4/2021 1:10:12.0 pm"), false),
            Tuple.Create(2, 3.7f, DateTime.Parse("7/4/2021 1:10:13.0 pm"), false),
            Tuple.Create(1, 3.5f, DateTime.Parse("7/4/2021 1:10:14.0 pm"), false),
            // Fueling started...
            Tuple.Create(2, 3.6f, DateTime.Parse("7/4/2021 1:10:15.0 pm"), false),
            Tuple.Create(1, 4.0f, DateTime.Parse("7/4/2021 1:10:17.0 pm"), false),
            Tuple.Create(2, 5.6f, DateTime.Parse("7/4/2021 1:10:18.0 pm"), false),
            // False Triggger
            Tuple.Create(1, 6.4f, DateTime.Parse("7/4/2021 1:10:19.0 pm"), false),
            Tuple.Create(2, 7.3f, DateTime.Parse("7/4/2021 1:10:20.0 pm"), false),
            Tuple.Create(0, 8.3f, DateTime.Parse("7/4/2021 1:10:22.0 pm"), false),
            Tuple.Create(0, 9.1f, DateTime.Parse("7/4/2021 1:10:23.0 pm"), false),
            Tuple.Create(0, 10.5f, DateTime.Parse("7/4/2021 1:10:24.0 pm"), false),
            Tuple.Create(0, 12.3f, DateTime.Parse("7/4/2021 1:10:25.0 pm"), false),
            Tuple.Create(0, 14.0f, DateTime.Parse("7/4/2021 1:10:27.0 pm"), false),
            Tuple.Create(0, 16.3f, DateTime.Parse("7/4/2021 1:10:28.0 pm"), false),
            Tuple.Create(0, 18.8f, DateTime.Parse("7/4/2021 1:10:29.0 pm"), false),
            Tuple.Create(0, 18.8f, DateTime.Parse("7/4/2021 1:10:30.0 pm"), false),
            Tuple.Create(0, 18.8f, DateTime.Parse("7/4/2021 1:10:31.0 pm"), false),
            // Leave pits...
            Tuple.Create(3, 18.8f, DateTime.Parse("7/4/2021 1:10:32.0 pm"), false),
            Tuple.Create(10, 18.8f, DateTime.Parse("7/4/2021 1:10:34.0 pm"), false),
            Tuple.Create(15, 18.8f, DateTime.Parse("7/4/2021 1:10:35.0 pm"), false),
            Tuple.Create(25, 18.8f, DateTime.Parse("7/4/2021 1:10:36.0 pm"), false),
            Tuple.Create(35, 18.8f, DateTime.Parse("7/4/2021 1:10:38.0 pm"), false),
        };

        var rc = new RefuelCheck(dtMock.Object);

        foreach (var d in data)
        {
            dtMock.Setup(p => p.UtcNow).Returns(d.Item3);
            var result = rc.IsRefuling(d.Item1, d.Item2);
            Assert.AreEqual(d.Item4, result);
        }
    }
}
