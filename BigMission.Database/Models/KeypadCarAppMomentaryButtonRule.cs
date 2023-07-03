using System;
using System.Collections.Generic;

namespace BigMission.Database.Models
{
    public partial class KeypadCarAppMomentaryButtonRule
    {
        public int Id { get; set; }
        public int KeypadId { get; set; }
        public int ButtonNumber { get; set; }
        public int LedNumber { get; set; }
        public int Blink { get; set; }
    }
}
