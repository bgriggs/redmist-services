using BigMission.CommandTools;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;

namespace BigMission.DeviceAppServiceStatusProcessor
{
    public class AppCommandsFactory : IAppCommandsFactory
    {
        private readonly IConfiguration configuration;
        private readonly ILoggerFactory loggerFactory;

        public AppCommandsFactory(IConfiguration configuration, ILoggerFactory loggerFactory)
        {
            this.configuration = configuration;
            this.loggerFactory = loggerFactory;
        }

        public AppCommands CreateAppCommands()
        {
            _ = Guid.TryParse(configuration["SERVICEID"], out Guid serviceId);
            return new AppCommands(serviceId, configuration["APIKEY"], configuration["APIURL"], loggerFactory);
        }
    }
}
