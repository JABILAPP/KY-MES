using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KY_MES.Infra.CrossCutting.DataMigrations
{
    /// <inheritdoc />
    public partial class Init : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Pallet",
                table: "inspection_units",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Pallet",
                table: "inspection_runs",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Pallet",
                table: "inspection_defects",
                type: "nvarchar(max)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Pallet",
                table: "inspection_units");

            migrationBuilder.DropColumn(
                name: "Pallet",
                table: "inspection_runs");

            migrationBuilder.DropColumn(
                name: "Pallet",
                table: "inspection_defects");
        }
    }
}
