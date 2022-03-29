using BigMission.ServiceStatusTools;
using BigMission.TestHelpers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NLog;
using NLog.Config;
using System;
using System.IO;
using System.Threading.Tasks;

namespace BigMission.RaceHeroAggregator
{
    class Program
    {
        private static Logger logger;

        static async Task Main()
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
                    .Build();

                var serviceStatus = new ServiceTracking(new Guid(config["ServiceId"]), "RaceHeroConnector", config["RedisConn"], logger);

                var host = new HostBuilder()
                   .ConfigureServices((builderContext, services) =>
                   {
                       services.AddSingleton<ILogger>(logger);
                       services.AddSingleton<IConfiguration>(config);
                       services.AddTransient<IDateTimeHelper, DateTimeHelper>();
                       services.AddSingleton<ISimulateSettingsService, SimulateSettingsService>();
                       services.AddSingleton(serviceStatus);
                       services.AddHostedService<Application>();
                   })
                   .Build();

                try
                {
                    serviceStatus.Start();
                    await host.RunAsync();
                }
                catch (OperationCanceledException)
                {
                    // suppress
                }
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
