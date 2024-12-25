using System.ComponentModel.DataAnnotations.Schema;

namespace BigMission.Database.V2.Models.UI.Channels.CarStatusTable;

[Table("CarStatusTableConfiguration")]
public class Configuration
{
    public int Id { get; set; }
    public int Version { get; set; }
    public DateTime LastUpdated { get; set; }
    public List<ColumnDefinition> Columns { get; set; } = [];
    public List<ColumnDefinitionOverride> ColumnOverrides { get; set; } = [];
}
