using BigMission.RaceControlLog.Configuration;
using BigMission.RaceControlLog.EventStatus;
using BigMission.RaceControlLog.LogConnections;
using BigMission.RaceControlLog.LogProcessing;
using BigMission.ServiceStatusTools;
using BigMission.TestHelpers;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Services;
using Google.Apis.Sheets.v4;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NLog;
using NLog.Config;

namespace BigMission.RaceControlLog
{
    internal class Program
    {
        private static Logger? logger;

        static async Task Main()
        {
            Console.WriteLine("Hello, World!");
            try
            {
                AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
                var basePath = Directory.GetCurrentDirectory();
                var env = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");
                if (env?.ToUpper() == "PRODUCTION")
                {
                    LogManager.Configuration = new XmlLoggingConfiguration($"{basePath}{Path.DirectorySeparatorChar}nlog.Production.config");
                }
                logger = LogManager.GetCurrentClassLogger();

                logger.Info($"Starting env={env}...");
                var config = new ConfigurationBuilder()
                    .SetBasePath(basePath)
                    .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
                    .Build();

                //var googleCreds = GoogleCredential.FromFile(@"LogConnections/redmist-909d6942af1e.json");
                //var sheetsService = new SheetsService(new BaseClientService.Initializer()
                //{
                //    HttpClientInitializer = googleCreds,
                //    ApplicationName = "RedMist"
                //});

                //var vals = sheetsService.Spreadsheets.Values;
                //var s = sheetsService.Spreadsheets;
                //var sprequest = s.Get(SpreedsheetId);
                //var resp = await sprequest.ExecuteAsync();
                //foreach(var sh in resp.Sheets)
                //{
                //    if (!sh.Properties.Hidden ?? true)
                //    {
                //        Console.WriteLine(sh.Properties.Title);
                //    }
                //}
                //var request = vals.Get(SpreedsheetId, "Road Atlanta - Sunday 2022!A4:H1000");
                //var response = await request.ExecuteAsync();
                //foreach (var col in response.Values)
                //{
                //    foreach (var row in col)
                //    {
                //        Console.Write(row + "\t");
                //    }
                //    Console.WriteLine();
                //}
                var serviceStatus = new ServiceTracking(new Guid(config["ServiceId"]), "WrlRaceControlLog", config["RedisConn"], logger);

                var host = new HostBuilder()
                   .ConfigureServices((builderContext, services) =>
                   {
                       services.AddSingleton<ILogger>(logger);
                       services.AddSingleton<IConfiguration>(config);
                       services.AddTransient<IDateTimeHelper, DateTimeHelper>();
                       services.AddSingleton(serviceStatus);
                       services.AddSingleton<IControlLogConnection, GoogleSheetsControlLog>();
                       services.AddSingleton<ILogProcessor, CacheControlLog>();
                       services.AddSingleton<ILogProcessor, SmsNotification>();
                       services.AddSingleton<ConfigurationContext>();
                       services.AddSingleton<IEventStatus, EventRedisStatus>();
                       services.AddHostedService<ConfigurationService>();
                       services.AddHostedService<LogPollService>();
                   })
                   .Build();
                
                try
                {
                    await host.RunAsync();
                }
                catch (OperationCanceledException)
                {
                    // suppress
                }
            }
            catch (Exception ex)
            {
                logger?.Error(ex);
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
                    logger?.Fatal(exception, "Unhandled exception");
                }
                else
                {
                    logger?.Fatal("Unhandled exception, but unable to retrieve the exception.");
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