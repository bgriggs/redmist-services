namespace BigMission.Database.Models
{
    public partial class RaceEventSetting
    {
        public int[] GetCarIds()
        {
            if (!string.IsNullOrWhiteSpace(CarIds))
            {
                var idstr = CarIds.Split(';').Where(s => !string.IsNullOrWhiteSpace(s));
                return idstr.Select(i => int.Parse(i)).ToArray();
            }
            return Array.Empty<int>();
        }
    }
}
