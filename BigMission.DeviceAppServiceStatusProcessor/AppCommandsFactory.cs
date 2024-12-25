using BigMission.CommandTools;

namespace BigMission.DeviceAppServiceStatusProcessor;

public class AppCommandsFactory(IConfiguration configuration, ILoggerFactory loggerFactory) : IAppCommandsFactory
{
    private readonly IConfiguration configuration = configuration;
    private readonly ILoggerFactory loggerFactory = loggerFactory;

    public AppCommands CreateAppCommands()
    {
        _ = Guid.TryParse(configuration["SERVICEID"], out Guid serviceId);
        return new AppCommands(serviceId, configuration["APIKEY"], configuration["AESKEY"], configuration["APIURL"], loggerFactory);
    }
}
