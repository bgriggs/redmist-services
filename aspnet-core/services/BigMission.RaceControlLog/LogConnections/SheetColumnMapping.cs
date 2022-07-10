using BigMission.Cache.Models.ControlLog;
using System.Reflection;

namespace BigMission.RaceControlLog.LogConnections
{
    /// <summary>
    /// Represents a WRL google sheet column mapping.
    /// </summary>
    internal class SheetColumnMapping
    {
        public string SheetColumn { get; set; } = string.Empty;
        public bool IsRequired { get; set; }
        public string PropertyName { get; set; } = string.Empty;
        public Func<string, object>? Convert { get; set; }

        public bool SetEntryValue(RaceControlLogEntry entry, string value)
        {
            var prop = entry.GetType().GetProperty(PropertyName, BindingFlags.Instance | BindingFlags.Public);
            if (prop != null)
            {
                if (Convert != null)
                {
                    try
                    {
                        var v = Convert(value);
                        prop.SetValue(entry, v);
                    }
                    catch
                    {
                        return false;
                    }
                }
                else
                {
                    prop.SetValue(entry, value);
                }
                return true;
            }
            else
            {
                return false;
            }
        }
    }
}
