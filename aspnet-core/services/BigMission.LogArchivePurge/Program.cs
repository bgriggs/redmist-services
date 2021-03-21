using BigMission.ServiceStatusTools;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NLog;
using System;

namespace BigMission.LogArchivePurge
{
    class Program
    {
        private static readonly Logger logger = LogManager.GetCurrentClassLogger();

        static void Main(string[] args)
        {
            try
            {
                var env = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");
                logger.Info($"Starting {env}...");
                var config = new ConfigurationBuilder()
                    .SetBasePath(System.IO.Directory.GetCurrentDirectory())
                    .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
                    .AddJsonFile($"appsettings.{env}.json", optional: true)
                    .Build();

                var serviceStatus = new ServiceTracking(new Guid(config["ServiceId"]), "LogArchivePurge", config["RedisConn"], logger);

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
