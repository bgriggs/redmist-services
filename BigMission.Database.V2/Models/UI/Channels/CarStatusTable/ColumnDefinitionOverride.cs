using System.ComponentModel.DataAnnotations.Schema;

namespace BigMission.Database.V2.Models.UI.Channels.CarStatusTable;

[Table("CarStatusTableColumnOverrides")]
public class ColumnDefinitionOverride : ColumnDefinition
{
    public int CarId { get; set; }
}
