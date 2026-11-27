using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using SPharmacyLog.Data;

#nullable disable

namespace SPharmacyLog.Data.Migrations;

[DbContext(typeof(PharmacyDbContext))]
[Migration("20261121114543_DiagnosePharmacyModel")]
public partial class DiagnosePharmacyModel : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateIndex(name: "IX_Borrowings_CustomerId", table: "Borrowings", column: "CustomerId");
        migrationBuilder.CreateIndex(name: "IX_Payments_CustomerId", table: "Payments", column: "CustomerId");
        migrationBuilder.CreateIndex(name: "IX_Prescriptions_CustomerId", table: "Prescriptions", column: "CustomerId");
        migrationBuilder.CreateIndex(name: "IX_Subscriptions_CustomerId", table: "Subscriptions", column: "CustomerId");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(name: "IX_Borrowings_CustomerId", table: "Borrowings");
        migrationBuilder.DropIndex(name: "IX_Payments_CustomerId", table: "Payments");
        migrationBuilder.DropIndex(name: "IX_Prescriptions_CustomerId", table: "Prescriptions");
        migrationBuilder.DropIndex(name: "IX_Subscriptions_CustomerId", table: "Subscriptions");
    }
}
