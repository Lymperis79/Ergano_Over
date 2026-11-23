using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using SPharmacyLog.Data;

#nullable disable

namespace SPharmacyLog.Data.Migrations;

[DbContext(typeof(PharmacyDbContext))]
[Migration("20261123100000_AddMedicineStockControl")]
public sealed class AddMedicineStockControl : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "MedicineStockMovements",
            columns: table => new
            {
                Id = table.Column<int>(nullable: false).Annotation("Sqlite:Autoincrement", true),
                MedicineId = table.Column<int>(nullable: false),
                Type = table.Column<int>(nullable: false),
                Quantity = table.Column<decimal>(nullable: false),
                CountedQuantity = table.Column<decimal>(nullable: true),
                MovementDate = table.Column<DateTime>(nullable: false),
                ExpiryDate = table.Column<DateTime>(nullable: true),
                BarcodeScanned = table.Column<string>(maxLength: 64, nullable: true),
                BatchLot = table.Column<string>(maxLength: 64, nullable: true),
                PerformedBy = table.Column<string>(maxLength: 255, nullable: false),
                Notes = table.Column<string>(nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_MedicineStockMovements", x => x.Id);
                table.ForeignKey(
                    name: "FK_MedicineStockMovements_Medicines_MedicineId",
                    column: x => x.MedicineId,
                    principalTable: "Medicines",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_MedicineStockMovements_MedicineId_ExpiryDate_MovementDate",
            table: "MedicineStockMovements",
            columns: new[] { "MedicineId", "ExpiryDate", "MovementDate" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "MedicineStockMovements");
    }
}
