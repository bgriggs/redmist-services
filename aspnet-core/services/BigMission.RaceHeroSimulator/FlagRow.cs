using System;
using static BigMission.RaceHeroSdk.RaceHeroClient;

namespace BigMission.RaceHeroSimulator
{
    public class FlagRow
    {
        public TimeSpan TimeOffset { get; set; }
        public Flag Flag { get; set; }
    }
}
