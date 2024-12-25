namespace BigMission.UI.Channels.Models;

public class ChannelDefinition
{
    public int ChannelId { get; set; }
    public string ChannelName { get; set; } = string.Empty;
    public int CarId { get; set; }
    public string CarNumber { get; set; } = string.Empty;
    public int DeviceAppId { get; set; }
}
