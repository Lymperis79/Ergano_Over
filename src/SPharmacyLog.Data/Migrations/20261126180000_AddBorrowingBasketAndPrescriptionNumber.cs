using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using SPharmacyLog.Data;

#nullable disable

namespace SPharmacyLog.Data.Migrations;

[DbContext(typeof(PharmacyDbContext))]
[Migration("20261126180000_AddBorrowingBasketAndPrescriptionNumber")]
public sealed class AddBorrowingBasketAndPrescriptionNumber : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        var textColumnType = ActiveProvider.Contains("Sqlite", StringComparison.OrdinalIgnoreCase)
            ? "TEXT"
            : "nvarchar(max)";

        migrationBuilder.AddColumn<string>(
            name: "PrescriptionNumber",
            table: "Borrowings",
            type: textColumnType,
            nullable: true);

        migrationBuilder.AlterColumn<int>(
            name: "CustomerId",
            table: "Borrowings",
            type: ActiveProvider.Contains("Sqlite", StringComparison.OrdinalIgnoreCase) ? "INTEGER" : "int",
            nullable: true,
            oldClrType: typeof(int),
            oldType: ActiveProvider.Contains("Sqlite", StringComparison.OrdinalIgnoreCase) ? "INTEGER" : "int");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AlterColumn<int>(
            name: "CustomerId",
            table: "Borrowings",
            type: ActiveProvider.Contains("Sqlite", StringComparison.OrdinalIgnoreCase) ? "INTEGER" : "int",
            nullable: false,
            defaultValue: 0,
            oldClrType: typeof(int),
            oldType: ActiveProvider.Contains("Sqlite", StringComparison.OrdinalIgnoreCase) ? "INTEGER" : "int",
            oldNullable: true);

        migrationBuilder.DropColumn(name: "PrescriptionNumber", table: "Borrowings");
    }
}
