using BigMission.Cache;
using BigMission.EntityFrameworkCore;
using BigMission.ServiceStatusTools;
using BigMission.TestHelpers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NLog;
using NLog.Config;
using StackExchange.Redis;
using System;
using System.IO;

namespace BigMission.FuelStatistics
{
    class Program
    {
        private static Logger logger;

        static void Main(string[] args)
        {
            try
            {
                var basePath = Directory.GetCurrentDirectory();
                var env = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");
                if (env.ToUpper() == "PRODUCTION")
                {
                    LogManager.Configuration = new XmlLoggingConfiguration($"{basePath}{Path.DirectorySeparatorChar}nlog.Production.config");
                }
                logger = LogManager.GetCurrentClassLogger();

                logger.Info($"Starting env={env}...");
                var config = new ConfigurationBuilder()
                    .SetBasePath(basePath)
                    .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
                    .AddJsonFile($"appsettings.{env}.json", optional: true)
                    .Build();

                var cf = new BigMissionDbContextFactory();
                var db = cf.CreateDbContext(new[] { config["ConnectionString"] });
                var cacheMuxer = ConnectionMultiplexer.Connect(config["RedisConn"]);


                var services = new ServiceCollection();
                services.AddSingleton<NLog.ILogger>(logger);
                services.AddSingleton<IConfiguration>(config);
                services.AddTransient<ITimerHelper, TimerHelper>();
                
                var serviceStatus = new ServiceTracking(new Guid(config["ServiceId"]), "FuelStatistics", config["RedisConn"], logger);
                services.AddSingleton(serviceStatus);

                var fuelRangeContext = new FuelRangeContext(cacheMuxer, db);
                services.AddSingleton(fuelRangeContext);

                var dataContext = new DataContext(cacheMuxer, config["ConnectionString"]);
                services.AddSingleton<IDataContext>(dataContext);

                services.AddTransient<Application>();

                var provider = services.BuildServiceProvider();

                var application = provider.GetService<Application>();
                application.Run();
            }
            catch (Exception ex)
            {
                logger.Error(ex);
            }
            finally
            {
                LogManager.Shutdown();
            }
        }
    }
}
