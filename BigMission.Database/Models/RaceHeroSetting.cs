using System;
using System.Collections.Generic;

namespace BigMission.Database.Models
{
    public partial class RaceHeroSetting
    {
        public int Id { get; set; }
        public string ApiUrl { get; set; }
        public string ApiKey { get; set; }
    }
}
