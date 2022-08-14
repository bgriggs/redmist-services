using BigMission.Cache.Models;

namespace BigMission.RaceControlLog.EventStatus
{
    /// <summary>
    /// Get Race Hero Status.
    /// </summary>
    internal interface IEventStatus
    {
        /// <summary>
        /// Requests Race Hero Event status, such as from race hero directly or local cache.
        /// </summary>
        /// <param name="rhEventId"></param>
        /// <returns></returns>
        public Task<RaceHeroEventStatus?> GetEventStatusAsync(int rhEventId);
    }
}
