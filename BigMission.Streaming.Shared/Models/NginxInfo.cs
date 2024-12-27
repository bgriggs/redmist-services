namespace BigMission.Streaming.Shared.Models;

public class NginxInfo
{
    public string HostName { get; set; } = string.Empty;
    public string IP { get; set; } = string.Empty;
    public List<NginxStreamPush> StreamDestinations { get; set; } = [];
    public string RequestId { get; set; } = string.Empty;
}
