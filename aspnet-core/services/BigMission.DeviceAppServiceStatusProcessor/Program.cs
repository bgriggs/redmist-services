using BigMission.ServiceStatusTools;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NLog;
using NLog.Config;
using System;

namespace BigMission.DeviceAppServiceStatusProcessor
{
    class Program
    {
        private static Logger logger;

        static void Main()
        {
            try
            {
                var env = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");
                if (env.ToUpper() == "PRODUCTION")
                {
                    LogManager.Configuration = new XmlLoggingConfiguration("nlog.Production.config");
                }
                logger = LogManager.GetCurrentClassLogger();

                logger.Info($"Starting env={env}...");
                var config = new ConfigurationBuilder()
                    .SetBasePath(System.IO.Directory.GetCurrentDirectory())
                    .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
                    .AddJsonFile($"appsettings.{env}.json", optional: true)
                    .Build();

                var serviceStatus = new ServiceTracking(new Guid(config["ServiceId"]), "DeviceAppStatusProcessor", config["RedisConn"], logger);

                var services = new ServiceCollection();
                services.AddSingleton<NLog.ILogger>(logger);
                services.AddSingleton<IConfiguration>(config);
                services.AddSingleton(serviceStatus);
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
