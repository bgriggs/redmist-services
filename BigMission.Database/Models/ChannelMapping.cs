using System;
using System.Collections.Generic;

namespace BigMission.Database.Models
{
    public partial class ChannelMapping
    {
        public int Id { get; set; }
        public int DeviceAppId { get; set; }
        public string ReservedName { get; set; }
        public string ChannelName { get; set; }
        public long CanId { get; set; }
        public byte Offset { get; set; }
        public byte Length { get; set; }
        public long Mask { get; set; }
        public string SourceType { get; set; }
        public bool IsBigEndian { get; set; }
        public double FormulaMultipler { get; set; }
        public double FormulaDivider { get; set; }
        public double FormulaConst { get; set; }
        public string Conversion { get; set; }
        public double LowRange { get; set; }
        public double HighRange { get; set; }
        public bool IsVirtual { get; set; }
        public int VirtualFrequencyMs { get; set; }
        public string GroupTag { get; set; }
    }
}
