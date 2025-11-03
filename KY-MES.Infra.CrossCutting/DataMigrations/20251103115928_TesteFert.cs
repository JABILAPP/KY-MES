using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KY_MES.Infra.CrossCutting.DataMigrations
{
    /// <inheritdoc />
    public partial class TesteFert : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Fert",
                table: "inspection_units",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Fert",
                table: "inspection_runs",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Fert",
                table: "inspection_defects",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Fert",
                table: "inspection_units");

            migrationBuilder.DropColumn(
                name: "Fert",
                table: "inspection_runs");

            migrationBuilder.DropColumn(
                name: "Fert",
                table: "inspection_defects");
        }
    }
}
