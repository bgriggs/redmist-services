using BigMission.Cache.Models.ControlLog;

namespace BigMission.RaceControlLog.LogConnections;

/// <summary>
/// Connection to a series control log.
/// </summary>
internal interface IControlLogConnection
{
    public string Type { get; }
    Task<IEnumerable<RaceControlLogEntry>> LoadControlLogAsync(string parameter);
}
