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

namespace BigMission.VirtualChannelAggregator
{
    class Program
    {
        private static Logger logger;

        static async Task Main()
        {
            try
            {
                AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
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
                    .AddEnvironmentVariables()
                    .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
                    .Build();

                var serviceStatus = new ServiceTracking(new Guid(config["ServiceId"]), "VirtualChannelAggregator", config["RedisConn"]);

                var host = new HostBuilder()
                   .ConfigureServices((builderContext, services) =>
                   {
                       services.AddSingleton<ILogger>(logger);
                       services.AddSingleton<IConfiguration>(config);
                       services.AddTransient<IDateTimeHelper, DateTimeHelper>();
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

        private static void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            try
            {
                var exception = e.ExceptionObject as Exception;
                if (exception != null)
                {
                    logger.Fatal(exception, "Unhandled exception");
                }
                else
                {
                    logger.Fatal("Unhandled exception, but unable to retrieve the exception.");
                }
            }
            catch (Exception)
            {
                // This is a really bad place to be. We are currently in the unhandled exception event and
                // as such we are going down already, but we can't necessarily figure out how to log the
                // event. The only reason this catch is here is to prevent us generating yet another unhandled
                // exception.
            }
        }
    }
}
