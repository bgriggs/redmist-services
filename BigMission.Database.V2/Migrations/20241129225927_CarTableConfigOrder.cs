using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BigMission.Database.V2.Migrations
{
    /// <inheritdoc />
    public partial class CarTableConfigOrder : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "ChannelName",
                schema: "dbo2",
                table: "CarStatusTableColumnDefinitions",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(200)",
                oldMaxLength: 200);

            migrationBuilder.AddColumn<int>(
                name: "Order",
                schema: "dbo2",
                table: "CarStatusTableColumnDefinitions",
                type: "int",
                maxLength: 200,
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Order",
                schema: "dbo2",
                table: "CarStatusTableColumnDefinitions");

            migrationBuilder.AlterColumn<string>(
                name: "ChannelName",
                schema: "dbo2",
                table: "CarStatusTableColumnDefinitions",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");
        }
    }
}
