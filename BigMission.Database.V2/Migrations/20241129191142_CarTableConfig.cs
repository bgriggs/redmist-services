using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BigMission.Database.V2.Migrations
{
    /// <inheritdoc />
    public partial class CarTableConfig : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "dbo2");

            migrationBuilder.CreateTable(
                name: "CarStatusTableConfiguration",
                schema: "dbo2",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Version = table.Column<int>(type: "int", nullable: false),
                    LastUpdated = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CarStatusTableConfiguration", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CarStatusTableColumnDefinitions",
                schema: "dbo2",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Header = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    ChannelName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    DecimalPlaces = table.Column<int>(type: "int", nullable: false),
                    ConfigurationId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CarStatusTableColumnDefinitions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CarStatusTableColumnDefinitions_CarStatusTableConfiguration_ConfigurationId",
                        column: x => x.ConfigurationId,
                        principalSchema: "dbo2",
                        principalTable: "CarStatusTableConfiguration",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "CarStatusTableColumnOverrides",
                schema: "dbo2",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false),
                    CarId = table.Column<int>(type: "int", nullable: false),
                    ConfigurationId1 = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CarStatusTableColumnOverrides", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CarStatusTableColumnOverrides_CarStatusTableColumnDefinitions_Id",
                        column: x => x.Id,
                        principalSchema: "dbo2",
                        principalTable: "CarStatusTableColumnDefinitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CarStatusTableColumnOverrides_CarStatusTableConfiguration_ConfigurationId1",
                        column: x => x.ConfigurationId1,
                        principalSchema: "dbo2",
                        principalTable: "CarStatusTableConfiguration",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_CarStatusTableColumnDefinitions_ConfigurationId",
                schema: "dbo2",
                table: "CarStatusTableColumnDefinitions",
                column: "ConfigurationId");

            migrationBuilder.CreateIndex(
                name: "IX_CarStatusTableColumnOverrides_ConfigurationId1",
                schema: "dbo2",
                table: "CarStatusTableColumnOverrides",
                column: "ConfigurationId1");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CarStatusTableColumnOverrides",
                schema: "dbo2");

            migrationBuilder.DropTable(
                name: "CarStatusTableColumnDefinitions",
                schema: "dbo2");

            migrationBuilder.DropTable(
                name: "CarStatusTableConfiguration",
                schema: "dbo2");
        }
    }
}
