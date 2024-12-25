using BigMission.Database.Models;
using BigMission.Database.V2.Models.UI.Channels.CarStatusTable;

namespace BigMission.UI.Channels.Models;

public class CarStatusSettings
{
    public Configuration TableConfiguration { get; set; } = new Configuration();
    public List<ChannelDefinition> Channels { get; set; } = [];
    public List<Car> Cars { get; set; } = [];
}
