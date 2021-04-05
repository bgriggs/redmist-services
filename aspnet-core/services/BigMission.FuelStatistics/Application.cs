using BigMission.CommandTools;
using BigMission.EntityFrameworkCore;
using BigMission.ServiceData;
using BigMission.ServiceStatusTools;
using Microsoft.Extensions.Configuration;
using NLog;
using StackExchange.Redis;
using System.Threading;

namespace BigMission.FuelStatistics
{
    /// <summary>
    /// Processes application status from the in car apps. (not channel status)
    /// </summary>
    class Application
    {
        private IConfiguration Config { get; }
        private ILogger Logger { get; }
        private ServiceTracking ServiceTracking { get; }
        private AppCommands Commands { get; }
        private readonly EventHubHelpers ehReader;
        private readonly ManualResetEvent serviceBlock = new ManualResetEvent(false);
        private readonly ConnectionMultiplexer cacheMuxer;


        public Application(IConfiguration config, ILogger logger, ServiceTracking serviceTracking)
        {
            Config = config;
            Logger = logger;
            ServiceTracking = serviceTracking;
            Commands = new AppCommands(Config["ServiceId"], Config["KafkaConnectionString"], logger);
            ehReader = new EventHubHelpers(logger);
            cacheMuxer = ConnectionMultiplexer.Connect(Config["RedisConn"]);
        }


        public void Run()
        {
            ServiceTracking.Update(ServiceState.STARTING, string.Empty);

            var cf = new BigMissionDbContextFactory();
            using var db = cf.CreateDbContext(new[] { Config["ConnectionString"] });
            

            // Start updating service status
            ServiceTracking.Start();
            Logger.Info("Started");
            serviceBlock.WaitOne();
        }
    }
}
