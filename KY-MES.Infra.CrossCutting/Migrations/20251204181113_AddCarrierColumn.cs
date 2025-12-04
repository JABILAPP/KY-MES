using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KY_MES.Infra.CrossCutting.Migrations
{
    /// <inheritdoc />
    public partial class AddCarrierColumn : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "inspection_runs",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    InspectionBarcode = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Result = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    Program = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Side = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    Stencil = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Machine = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    User = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    StartTime = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    EndTime = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ManufacturingArea = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    RawJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()"),
                    Carrier = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_inspection_runs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "inspection_units",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    InspectionRunId = table.Column<long>(type: "bigint", nullable: false),
                    ArrayIndex = table.Column<int>(type: "int", nullable: false),
                    UnitBarcode = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Result = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    Side = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    Machine = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    User = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    StartTime = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    EndTime = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ManufacturingArea = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()"),
                    Carrier = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_inspection_units", x => x.Id);
                    table.ForeignKey(
                        name: "FK_inspection_units_inspection_runs_InspectionRunId",
                        column: x => x.InspectionRunId,
                        principalTable: "inspection_runs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "inspection_defects",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    InspectionUnitId = table.Column<long>(type: "bigint", nullable: false),
                    Comp = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Part = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    DefectCode = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()"),
                    Carrier = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_inspection_defects", x => x.Id);
                    table.ForeignKey(
                        name: "FK_inspection_defects_inspection_units_InspectionUnitId",
                        column: x => x.InspectionUnitId,
                        principalTable: "inspection_units",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_inspection_defects_Carrier",
                table: "inspection_defects",
                column: "Carrier");

            migrationBuilder.CreateIndex(
                name: "IX_inspection_defects_DefectCode",
                table: "inspection_defects",
                column: "DefectCode");

            migrationBuilder.CreateIndex(
                name: "IX_inspection_defects_InspectionUnitId",
                table: "inspection_defects",
                column: "InspectionUnitId");

            migrationBuilder.CreateIndex(
                name: "IX_inspection_runs_Carrier",
                table: "inspection_runs",
                column: "Carrier");

            migrationBuilder.CreateIndex(
                name: "IX_inspection_runs_InspectionBarcode",
                table: "inspection_runs",
                column: "InspectionBarcode");

            migrationBuilder.CreateIndex(
                name: "IX_inspection_units_Carrier",
                table: "inspection_units",
                column: "Carrier");

            migrationBuilder.CreateIndex(
                name: "IX_inspection_units_InspectionRunId",
                table: "inspection_units",
                column: "InspectionRunId");

            migrationBuilder.CreateIndex(
                name: "IX_inspection_units_InspectionRunId_ArrayIndex",
                table: "inspection_units",
                columns: new[] { "InspectionRunId", "ArrayIndex" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_inspection_units_UnitBarcode",
                table: "inspection_units",
                column: "UnitBarcode");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "inspection_defects");

            migrationBuilder.DropTable(
                name: "inspection_units");

            migrationBuilder.DropTable(
                name: "inspection_runs");
        }
    }
}
