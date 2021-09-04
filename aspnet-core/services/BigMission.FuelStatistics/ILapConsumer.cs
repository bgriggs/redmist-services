using System.Collections.Generic;
using System.Threading.Tasks;

namespace BigMission.FuelStatistics
{
    public interface ILapConsumer
    {
        int[] EventIds { get; }
        Task UpdateLaps(int eventId, List<Lap> laps);
    }
}
