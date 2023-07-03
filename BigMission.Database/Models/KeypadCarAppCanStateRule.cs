using System;
using System.Collections.Generic;

namespace BigMission.Database.Models
{
    public partial class KeypadCarAppCanStateRule
    {
        public int Id { get; set; }
        public int KeypadId { get; set; }
        public int CanId { get; set; }
        public int Offset { get; set; }
        public int Length { get; set; }
        public int Value { get; set; }
        public int ButtonNumber { get; set; }
        public int LedNumber { get; set; }
        public int Blink { get; set; }
    }
}
