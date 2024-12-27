namespace BigMission.Streaming.Shared.Models;

public class NginxStreamPush
{
    public string StreamKey { get; set; } = string.Empty;
    public Platform Platform { get; set; }
    public string RequestId { get; set; } = string.Empty;
}

public enum Platform
{
    YouTube,
    Facebook,
}
