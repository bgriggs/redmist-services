using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BigMission.Database.V2.Models.UI.Channels.CarStatusTable;

[Table("CarStatusTableColumnDefinitions")]
public class ColumnDefinition
{
    public int Id { get; set; }
    [MaxLength(200)]
    public string Header { get; set; } = string.Empty;
    public int Order { get; set; }
    [MaxLength(200)]
    public string ChannelName { get; set; } = string.Empty;
    public int DecimalPlaces { get; set; }
    public int WidthPx { get; set; }
}
