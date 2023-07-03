using BigMission.Database.Models;

namespace BigMission.RaceControlLog.Configuration
{
    internal class ConfigurationEventData
    {
        public RaceEventSetting[] Events { get; set; } = Array.Empty<RaceEventSetting>();
        public Dictionary<int, Car> Cars { get; set; } = new Dictionary<int, Car>();
        public Dictionary<long, AbpUser> Users { get; set; } = new Dictionary<long, AbpUser>();

        public static long[] GetIds(string idStr)
        {
            var ids = new List<long>();
            var items = idStr.Split(";");
            foreach (var i in items)
            {
                if (!string.IsNullOrWhiteSpace(i))
                {
                    if (long.TryParse(i, out long id))
                    {
                        ids.Add(id);
                    }
                }
            }
            return ids.ToArray();
        }

        public Dictionary<(int sysEvent, string car), AbpUser[]> GetCarSmsSubscriptions()
        {
            var subs = new Dictionary<(int sysEvent, string car), AbpUser[]>();

            foreach (var evt in Events)
            {
                var carIds = GetIds(evt.CarIds ?? string.Empty);
                var userIds = GetIds(evt.ControlLogSmsUserSubscriptions ?? string.Empty);
                var users = GetUsers(userIds);
                foreach (int cid in carIds)
                {
                    if (Cars.TryGetValue(cid, out var car))
                    {
                        var key = (evt.Id, car.Number.ToUpper());
                        subs[key] = users;
                    }
                }
            }
            return subs;
        }

        private AbpUser[] GetUsers(long[] ids)
        {
            var users = new List<AbpUser>();
            foreach (var id in ids)
            {
                if (Users.TryGetValue(id, out var user))
                {
                    users.Add(user);
                }
            }
            return users.ToArray();
        }
    }
}