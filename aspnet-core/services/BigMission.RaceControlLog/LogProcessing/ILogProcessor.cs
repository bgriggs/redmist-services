using BigMission.Cache.Models.ControlLog;
using BigMission.RaceControlLog.Configuration;

namespace BigMission.RaceControlLog.LogProcessing
{
    /// <summary>
    /// Process a control log poll update.
    /// </summary>
    internal interface ILogProcessor
    {
        public Task Process(int eventId, IEnumerable<RaceControlLogEntry> log, ConfigurationEventData configurationEventData);
    }
}
