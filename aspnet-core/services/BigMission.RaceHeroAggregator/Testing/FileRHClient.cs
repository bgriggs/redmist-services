using BigMission.RaceHeroSdk;
using BigMission.RaceHeroSdk.Models;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace BigMission.RaceHeroAggregator.Testing
{
    /// <summary>
    /// Used for testing by loading an old event from files.
    /// </summary>
    class FileRHClient : IRaceHeroClient
    {
        private readonly List<Tuple<DateTime, Event>> eventData = new List<Tuple<DateTime, Event>>();
        private readonly List<Tuple<DateTime, Leaderboard>> lbData = new List<Tuple<DateTime, Leaderboard>>();
        private DateTime lastLb;


        public FileRHClient(string eventPath, string leaderboardPath)
        {
            // Load Event files
            var evtPolls = new List<Tuple<DateTime, Event>>();
            var evtFiles = Directory.GetFiles(eventPath);
            foreach (var f in evtFiles)
            {
                var fi = new FileInfo(f);
                var ts = fi.Name.Remove(0, 4);
                ts = ts.Replace(".json", "");
                var dt = DateTime.FromFileTimeUtc(long.Parse(ts));
                var json = File.ReadAllText(f);
                var evt = JsonConvert.DeserializeObject<Event>(json);
                var p = Tuple.Create(dt, evt);
                evtPolls.Add(p);
            }

            var sequencedEvtPolls = evtPolls.OrderBy(p => p.Item1);
            foreach (var sp in sequencedEvtPolls)
            {
                eventData.Add(sp);
            }

            // Load leaderboard files
            var lbPolls = new List<Tuple<DateTime, Leaderboard>>();
            var lbFiles = Directory.GetFiles(leaderboardPath);
            foreach (var f in lbFiles)
            {
                var fi = new FileInfo(f);
                var ts = fi.Name.Remove(0, 3);
                ts = ts.Replace(".json", "");
                var dt = DateTime.FromFileTimeUtc(long.Parse(ts));
                var json = File.ReadAllText(f);
                var lb = JsonConvert.DeserializeObject<Leaderboard>(json);
                var p = Tuple.Create(dt, lb);
                lbPolls.Add(p);
            }

            var sequencedLbPolls = lbPolls.OrderBy(p => p.Item1);
            foreach (var sp in sequencedLbPolls)
            {
                lbData.Add(sp);
            }
        }

        public Task<Event> GetEvent(string eventId)
        {
            if (eventData.Any())
            {
                for (int i = 0; i < eventData.Count; i++)
                {
                    int nextIndex = i + 1;
                    if (nextIndex < eventData.Count)
                    {
                        if (lastLb >= eventData[i].Item1 && lastLb < eventData[nextIndex].Item1)
                        {
                            return Task.FromResult(eventData[i].Item2);
                        }
                    }
                    else
                    {
                        var evt = eventData[i].Item2;
                        if (lastLb == default)
                        {
                            // Pop off event entries until the event starts
                            eventData.RemoveAt(0);
                        }
                        return Task.FromResult(evt);
                    }
                }
            }
            return Task.FromResult(new Event());
        }

        public Task<Events> GetEvents(int limit = 25, int offset = 0, bool live = false)
        {
            throw new NotImplementedException();
        }

        public Task<Leaderboard> GetLeaderboard(string eventId)
        {
            if (lbData.Any())
            {
                var p = lbData.First();
                lbData.RemoveAt(0);
                lastLb = p.Item1;
                return Task.FromResult(p.Item2);
            }

            return Task.FromResult(new Leaderboard());
        }
    }
}
