using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using SPharmacyLog.Data;

#nullable disable

namespace SPharmacyLog.Data.Migrations;

/// <summary>
/// Adds alternate medicine barcodes and GS1 serialized-pack tracking.
/// Existing medicine and borrowing rows are retained; the new pack link is nullable.
/// </summary>
[DbContext(typeof(PharmacyDbContext))]
[Migration("20261121160000_AddGs1SerializedPackTracking")]
public sealed class AddGs1SerializedPackTracking : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<int>(
            name: "SerializedPackScanId",
            table: "Borrowings",
            nullable: true);

        migrationBuilder.CreateTable(
            name: "MedicineBarcodes",
            columns: table => new
            {
                Id = table.Column<int>(nullable: false).Annotation("Sqlite:Autoincrement", true),
                MedicineId = table.Column<int>(nullable: false),
                BarcodeValue = table.Column<string>(maxLength: 64, nullable: false),
                BarcodeType = table.Column<string>(maxLength: 24, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_MedicineBarcodes", x => x.Id);
                table.ForeignKey(
                    name: "FK_MedicineBarcodes_Medicines_MedicineId",
                    column: x => x.MedicineId,
                    principalTable: "Medicines",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "SerializedPackScans",
            columns: table => new
            {
                Id = table.Column<int>(nullable: false).Annotation("Sqlite:Autoincrement", true),
                RawGs1String = table.Column<string>(maxLength: 255, nullable: false),
                Gtin14 = table.Column<string>(fixedLength: true, maxLength: 14, nullable: false),
                EofEan13 = table.Column<string>(fixedLength: true, maxLength: 13, nullable: true),
                SerialNumber = table.Column<string>(maxLength: 20, nullable: false),
                ExpiryDate = table.Column<DateTime>(type: "date", nullable: false),
                BatchLot = table.Column<string>(maxLength: 20, nullable: false),
                HmvoStatus = table.Column<string>(maxLength: 20, nullable: false, defaultValue: "ACTIVE"),
                ScannedAt = table.Column<DateTime>(nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                MedicineId = table.Column<int>(nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_SerializedPackScans", x => x.Id);
                table.ForeignKey(
                    name: "FK_SerializedPackScans_Medicines_MedicineId",
                    column: x => x.MedicineId,
                    principalTable: "Medicines",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.SetNull);
            });

        migrationBuilder.CreateIndex(
            name: "IX_Borrowings_SerializedPackScanId",
            table: "Borrowings",
            column: "SerializedPackScanId");

        migrationBuilder.AddForeignKey(
            name: "FK_Borrowings_SerializedPackScans_SerializedPackScanId",
            table: "Borrowings",
            column: "SerializedPackScanId",
            principalTable: "SerializedPackScans",
            principalColumn: "Id",
            onDelete: ReferentialAction.Restrict);

        migrationBuilder.CreateIndex(
            name: "IX_MedicineBarcodes_BarcodeValue",
            table: "MedicineBarcodes",
            column: "BarcodeValue",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_MedicineBarcodes_MedicineId",
            table: "MedicineBarcodes",
            column: "MedicineId");

        migrationBuilder.CreateIndex(
            name: "IX_SerializedPackScans_Gtin14_SerialNumber",
            table: "SerializedPackScans",
            columns: new[] { "Gtin14", "SerialNumber" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_SerializedPackScans_EofEan13",
            table: "SerializedPackScans",
            column: "EofEan13");

        migrationBuilder.CreateIndex(
            name: "IX_SerializedPackScans_MedicineId",
            table: "SerializedPackScans",
            column: "MedicineId");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropForeignKey(
            name: "FK_Borrowings_SerializedPackScans_SerializedPackScanId",
            table: "Borrowings");

        migrationBuilder.DropIndex(
            name: "IX_Borrowings_SerializedPackScanId",
            table: "Borrowings");

        migrationBuilder.DropColumn(
            name: "SerializedPackScanId",
            table: "Borrowings");

        migrationBuilder.DropTable(name: "SerializedPackScans");
        migrationBuilder.DropTable(name: "MedicineBarcodes");
    }
}
