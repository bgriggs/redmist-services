using BigMission.Cache.FuelRange;
using System.Threading.Tasks;

namespace BigMission.FuelStatistics
{
    public interface IStintOverrideConsumer
    {
        Task ProcessStintOverride(FuelRangeUpdate stint);
    }
}
