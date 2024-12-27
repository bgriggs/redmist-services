namespace BigMission.Streaming.Shared.Models;

public class RequestResponse
{
    public string RequestId{get; set; } = string.Empty;
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public object Data { get; set; } = string.Empty;
}
