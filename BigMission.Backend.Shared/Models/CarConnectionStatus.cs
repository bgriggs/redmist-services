using Newtonsoft.Json;
using System.Text.Json.Serialization;

namespace BigMission.Backend.Shared.Models;

public class CarConnectionStatus
{
    [JsonProperty("cid")]
    [JsonPropertyName("cid")]
    public int CarId { get; set; }

    [JsonProperty("dcs")]
    [JsonPropertyName("dcs")]
    public List<DeviceConnectionStatus> DeviceConnectionStatuses { get; set; } = [];
}

public class DeviceConnectionStatus
{
    [JsonProperty("did")]
    [JsonPropertyName("did")]
    public int DeviceAppId { get; set; }

    [JsonProperty("lthbs")]
    [JsonPropertyName("lthbs")]
    public List<DateTime> LastThreeHeartbeats { get; set; } = [];

    [JsonProperty("hbint")]
    [JsonPropertyName("hbint")]
    public int HeartbeatIntervalMs { get; set; }
}

public static class CarConnectionCacheConst
{
    public const string CAR_CONN_STATUS_SUBSCRIPTION = "car-conn-status-updated";
    public const string GROUP_NAME = "web-status";
    public const string CAR_STATUS = "car-conn-status";
    public const string DEVICE_ASSIGNED_CAR = "device-{0}-car";
    public const string CAR_DEVICES_LOOKUP = "car{0}-devices";
}