namespace BigMission.Streaming.Shared.Models;

public class ObsStatus
{
    public string HostName { get; set; } = string.Empty;
    public string HostIp { get; set; } = string.Empty;
    public bool WebsocketConnected { get; set; }
    public double CpuUsage { get; set; }
    public double MemoryUsage { get; set; }
    public bool IsObsRunning { get; set; }
    public bool IsSrtMonitorRunning { get; set; }
    public string CurrentScene { get; set; } = string.Empty;
    public string VideoSrtSource { get; set; } = string.Empty;
    public bool IsStreaming { get; set; }
    public long StreamingBitrate { get; set; }    
    public long StreamDurationMs { get; set; }
}
