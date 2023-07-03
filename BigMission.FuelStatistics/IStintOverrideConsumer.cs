using BigMission.Cache.Models.FuelRange;
using System.Threading.Tasks;

namespace BigMission.FuelStatistics
{
    public interface IStintOverrideConsumer
    {
        Task ProcessStintOverride(RangeUpdate stint);
    }
}
