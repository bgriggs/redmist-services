using BigMission.Cache.Models.FuelRange;

namespace BigMission.FuelStatistics;

public interface IStintOverrideConsumer
{
    Task ProcessStintOverride(RangeUpdate stint);
}
