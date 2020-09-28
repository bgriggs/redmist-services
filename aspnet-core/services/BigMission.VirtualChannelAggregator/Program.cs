using BigMission.ServiceStatusTools;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NLog;
using NLog.Extensions.Logging;
using System;


namespace BigMission.VirtualChannelAggregator
{
    class Program
    {
        private static readonly Logger logger = LogManager.GetCurrentClassLogger();

        static void Main(string[] args)
        {
            try
            {
                logger.Info("Starting...");
                var config = new ConfigurationBuilder()
                    .SetBasePath(System.IO.Directory.GetCurrentDirectory())
                    .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
                    .Build();

                var serviceStatus = new ServiceTracking(new Guid(config["ServiceId"]), "VirtualChannelAggregator", config["ConnectionString"], logger);

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
