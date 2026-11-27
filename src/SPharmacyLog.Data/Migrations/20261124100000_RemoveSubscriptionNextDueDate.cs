using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using SPharmacyLog.Data;

#nullable disable

namespace SPharmacyLog.Data.Migrations;

[DbContext(typeof(PharmacyDbContext))]
[Migration("20261124100000_RemoveSubscriptionNextDueDate")]
public sealed class RemoveSubscriptionNextDueDate : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        var dateColumnType = ActiveProvider.Contains("Sqlite", StringComparison.OrdinalIgnoreCase)
            ? "TEXT"
            : "datetime2";

        // Preserve old records before removing the obsolete scheduled-date column.
        migrationBuilder.Sql("UPDATE Subscriptions SET ExpiryDate = NextDueDate WHERE ExpiryDate IS NULL");

        migrationBuilder.AlterColumn<DateTime>(
            name: "ExpiryDate",
            table: "Subscriptions",
            type: dateColumnType,
            nullable: false,
            oldClrType: typeof(DateTime),
            oldType: dateColumnType,
            oldNullable: true);

        migrationBuilder.DropColumn(name: "NextDueDate", table: "Subscriptions");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        var dateColumnType = ActiveProvider.Contains("Sqlite", StringComparison.OrdinalIgnoreCase)
            ? "TEXT"
            : "datetime2";

        migrationBuilder.AddColumn<DateTime>(
            name: "NextDueDate",
            table: "Subscriptions",
            type: dateColumnType,
            nullable: false,
            defaultValue: new DateTime(1970, 1, 1));

        migrationBuilder.Sql("UPDATE Subscriptions SET NextDueDate = ExpiryDate");

        migrationBuilder.AlterColumn<DateTime>(
            name: "ExpiryDate",
            table: "Subscriptions",
            type: dateColumnType,
            nullable: true,
            oldClrType: typeof(DateTime),
            oldType: dateColumnType);
    }
}
