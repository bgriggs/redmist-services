namespace BigMission.Backend.Shared;

public static class Consts
{
    public const string CHANNEL_TELEM = "ch_telem";
    /// <summary>
    /// Consumer group for CarTelemetryProcessor services.
    /// </summary>
    public const string CHANNEL_TELEM_CAR_TELEM_PROC_GRP = "car_telem_proc_grp";

    public const string HEARTBEAT_TELEM = "hb_telem";
    /// <summary>
    /// Consumer group for DevAppServiceStatusProcessor services.
    /// </summary>
    public const string DEV_APP_PROC_GRP = "dev_app_proc_grp";
}
