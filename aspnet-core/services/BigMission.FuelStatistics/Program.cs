using BigMission.Cache.Models;
using BigMission.Cache.Models.Flags;
using BigMission.Cache.Models.FuelRange;
using BigMission.FuelStatistics.FuelRange;
using BigMission.ServiceStatusTools;
using BigMission.TestHelpers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NLog;
using NLog.Config;
using StackExchange.Redis;
using System;
using System.IO;
using System.Threading.Tasks;

namespace BigMission.FuelStatistics
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

                var serviceStatus = new ServiceTracking(new Guid(config["ServiceId"]), "FuelStatistics", config["RedisConn"]);
                serviceStatus.Update(ServiceState.STARTING, string.Empty);

                var cacheMuxer = ConnectionMultiplexer.Connect(config["RedisConn"]);
                var fuelRangeContext = new FuelRangeContext(config["ConnectionString"], cacheMuxer);
                var dataContext = new DataContext(cacheMuxer, config["ConnectionString"]);
                var flagContext = new FlagContext(cacheMuxer);

                var host = new HostBuilder()
                    .ConfigureAppConfiguration((context, builder) =>
                    {
                        //builder.AddJsonFile("appsettings.json", false, true);
                        //builder.AddJsonFile($"appsettings.{context.HostingEnvironment.EnvironmentName}.json", true, true);
                        //builder.AddEnvironmentVariables();
                    })
                    .ConfigureServices((builderContext, services) =>
                    {
                        services.AddSingleton<ILogger>(logger);
                        services.AddSingleton<IConfiguration>(config);
                        services.AddTransient<IDateTimeHelper, DateTimeHelper>();
                        services.AddSingleton<IFuelRangeContext>(fuelRangeContext);
                        services.AddSingleton<IDataContext>(dataContext);
                        services.AddSingleton<IFlagContext>(flagContext);
                        services.AddSingleton<ILapConsumer, EventService>();
                        services.AddSingleton<ICarTelemetryConsumer>(s =>
                        {
                            var svs = s.GetServices<ILapConsumer>();
                            foreach (var lc in svs)
                            {
                                var es = lc as EventService;
                                if (es != null)
                                {
                                    return es;
                                }
                            }
                            throw new InvalidOperationException("Missing dependency");
                        });

                        services.AddSingleton<IStintOverrideConsumer>(s =>
                        {
                            var svs = s.GetServices<ILapConsumer>();
                            foreach (var lc in svs)
                            {
                                var es = lc as EventService;
                                if (es != null)
                                {
                                    return es;
                                }
                            }
                            throw new InvalidOperationException("Missing dependency");
                        });

                        // Services
                        services.AddHostedService(s =>
                        {
                            var svs = s.GetServices<ILapConsumer>();
                            foreach (var lc in svs)
                            {
                                var es = lc as EventService;
                                if (es != null)
                                {
                                    return es;
                                }
                            }
                            throw new InvalidOperationException("Missing dependency");
                        });
                        services.AddHostedService<LapProcessorService>();
                        services.AddHostedService<StintOverrideService>();
                        services.AddHostedService<CarTelemetryService>();
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
