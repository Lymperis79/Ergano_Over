using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using SPharmacyLog.Data;

#nullable disable

namespace SPharmacyLog.Data.Migrations;

[DbContext(typeof(PharmacyDbContext))]
[Migration("20261121144200_AddCustomerDetails")]
public sealed class AddCustomerDetails : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(name: "FirstName", table: "Customers", nullable: true);
        migrationBuilder.AddColumn<string>(name: "Surname", table: "Customers", nullable: true);
        migrationBuilder.AddColumn<string>(name: "Telephone", table: "Customers", nullable: true);
        migrationBuilder.AddColumn<string>(name: "Mobile", table: "Customers", nullable: true);
        migrationBuilder.AddColumn<string>(name: "Email", table: "Customers", nullable: true);
        migrationBuilder.AddColumn<string>(name: "Amka", table: "Customers", nullable: true);
        migrationBuilder.AddColumn<string>(name: "Insurance", table: "Customers", nullable: true);
        migrationBuilder.AddColumn<string>(name: "City", table: "Customers", nullable: true);
        migrationBuilder.AddColumn<string>(name: "Postcode", table: "Customers", nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "FirstName", table: "Customers");
        migrationBuilder.DropColumn(name: "Surname", table: "Customers");
        migrationBuilder.DropColumn(name: "Telephone", table: "Customers");
        migrationBuilder.DropColumn(name: "Mobile", table: "Customers");
        migrationBuilder.DropColumn(name: "Email", table: "Customers");
        migrationBuilder.DropColumn(name: "Amka", table: "Customers");
        migrationBuilder.DropColumn(name: "Insurance", table: "Customers");
        migrationBuilder.DropColumn(name: "City", table: "Customers");
        migrationBuilder.DropColumn(name: "Postcode", table: "Customers");
    }
}