using BigMission.ServiceStatusTools;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace BigMission.FuelStatistics.Tests;

[TestClass]
public class LapProcessorServiceTests
{
    [TestMethod]
    public void ExecuteAsync_SingleConsumerLap_Test()
    {
        var configVals = new Dictionary<string, string> { { "LAPCHECKMS", "1000" } };
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(configVals)
            .Build();
        var logger = new Mock<ILoggerFactory>();
        logger.Setup(s => s.CreateLogger(It.IsAny<string>())).Returns(new Logger());
        var dataContext = new Mock<IDataContext>();
        var laps = new List<Lap> { new() { EventId = 1 } };
        dataContext.Setup(p => p.PopEventLaps(It.IsAny<int>())).Returns(Task.FromResult(laps));
        var consumer = new LapConsumer { EventIds = new[] { 1 } };
        var startup = new Mock<IStartupHealthCheck>();
        startup.Setup(s => s.CheckDependencies()).Returns(Task.FromResult(true));

        var proc = new LapProcessorService(configuration, logger.Object, dataContext.Object, new ILapConsumer[] { consumer }, startup.Object);
        proc.StartAsync(new CancellationToken()).Wait();

        Assert.AreEqual(1, consumer.LapsResult.Count);
        Assert.AreEqual(1, consumer.LapsResult[0].EventId);
    }

    [TestMethod]
    public void ExecuteAsync_MultipleConsumersLap_Test()
    {
        var configVals = new Dictionary<string, string> { {"LAPCHECKMS", "1000"} };
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(configVals)
            .Build();
        var logger = new Mock<ILoggerFactory>();
        logger.Setup(s => s.CreateLogger(It.IsAny<string>())).Returns(new Logger());
        var dataContext = new Mock<IDataContext>();
        var laps = new List<Lap> { new() { EventId = 1 }, new() { EventId = 2 } };
        dataContext.Setup(p => p.PopEventLaps(It.IsAny<int>())).Returns(Task.FromResult(laps));
        var c1 = new LapConsumer { EventIds = new[] { 1 } };
        var c2 = new LapConsumer { EventIds = new[] { 1 } };
        var c3 = new LapConsumer { EventIds = new[] { 2 } };
        var startup = new Mock<IStartupHealthCheck>();
        startup.Setup(s => s.CheckDependencies()).Returns(Task.FromResult(true));

        var proc = new LapProcessorService(configuration, logger.Object, dataContext.Object, new ILapConsumer[] { c1, c2, c3 }, startup.Object);
        proc.StartAsync(new CancellationToken()).Wait();

        Assert.AreEqual(2, c1.LapsResult.Count);
        Assert.AreEqual(1, c1.LapsResult[0].EventId);

        Assert.AreEqual(2, c2.LapsResult.Count);
        Assert.AreEqual(1, c2.LapsResult[0].EventId);

        Assert.AreEqual(2, c3.LapsResult.Count);
    }
}

public class LapConsumer : ILapConsumer
{
    public int[] EventIds { get; set; }
    public List<Lap> LapsResult { get; private set; }

    public Task UpdateLaps(int eventId, List<Lap> laps)
    {
        LapsResult = laps;
        return Task.CompletedTask;
    }
}
public class Logger : ILogger
{
    public IDisposable BeginScope<TState>(TState state) where TState : notnull
    {
        return null;
    }

    public bool IsEnabled(LogLevel logLevel)
    {
        return true;
    }

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter)
    {
        
    }
}