﻿using BigMission.ServiceStatusTools;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NLog;
using System;

namespace BigMission.AlarmProcessor
{
    /// <summary>
    /// This service monitors channel data for values that will trigger an alarm.  When the 
    /// user conditions are met, the trigger actions will be executed.
    /// </summary>
    class Program
    {
        private static readonly Logger logger = LogManager.GetCurrentClassLogger();

        static void Main()
        {
            try
            {
                logger.Info("Starting...");
                var config = new ConfigurationBuilder()
                    .SetBasePath(System.IO.Directory.GetCurrentDirectory())
                    .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
                    .Build();

                var serviceStatus = new ServiceTracking(new Guid(config["ServiceId"]), "AlarmProcessor", config["ConnectionString"], logger);

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