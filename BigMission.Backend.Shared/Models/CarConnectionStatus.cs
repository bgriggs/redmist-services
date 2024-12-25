using Newtonsoft.Json;

namespace BigMission.Backend.Shared.Models;

public class CarConnectionStatus
{
    [JsonProperty("cid")]
    public int CarId { get; set; }
    [JsonProperty("dcs")]
    public List<DeviceConnectionStatus> DeviceConnectionStatuses { get; set; } = [];
}

public class DeviceConnectionStatus
{
    [JsonProperty("did")]
    public int DeviceAppId { get; set; }
    [JsonProperty("lthbs")]
    public List<DateTime> LastThreeHeartbeats { get; set; } = [];
    [JsonProperty("hbint")]
    public int HeartbeatIntervalMs { get; set; }
}

public static class CarConnectionCacheConst
{
    public const string CAR_STATUS_SUBSCRIPTION = "car-status-updated";
    public const string GROUP_NAME = "web-status";
    public const string CAR_STATUS = "car-conn-status";
    public const string DEVICE_ASSIGNED_CAR = "device-{0}-car";
    public const string CAR_DEVICES_LOOKUP = "car{0}-devices";
}