using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using SPharmacyLog.Data;

namespace SPharmacyLog.Data.Migrations;

[DbContext(typeof(PharmacyDbContext))]
[Migration("202611210001_Initial")]
public sealed partial class Initial : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            CREATE TABLE Users (
                Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                Username TEXT NOT NULL,
                PasswordHash TEXT NOT NULL,
                Role INTEGER NOT NULL,
                AllowedMenus TEXT NULL,
                IsActive INTEGER NOT NULL,
                CanSaveCredentials INTEGER NOT NULL,
                CreatedAt TEXT NOT NULL
            );
            CREATE UNIQUE INDEX IX_Users_Username ON Users(Username);

            CREATE TABLE Customers (
                Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                CustomerNumber TEXT NOT NULL,
                FullName TEXT NOT NULL,
                Phone TEXT NOT NULL,
                Address TEXT NULL,
                Notes TEXT NULL,
                IsActive INTEGER NOT NULL,
                CreatedAt TEXT NOT NULL
            );
            CREATE UNIQUE INDEX IX_Customers_CustomerNumber ON Customers(CustomerNumber);

            CREATE TABLE Prescriptions (
                Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                CustomerId INTEGER NOT NULL,
                PrescriptionNumber TEXT NULL,
                MedicineName TEXT NOT NULL,
                Barcode TEXT NULL,
                StartDate TEXT NOT NULL,
                ExpiryDate TEXT NOT NULL,
                Status INTEGER NOT NULL,
                Notes TEXT NULL,
                CreatedAt TEXT NOT NULL,
                FOREIGN KEY(CustomerId) REFERENCES Customers(Id) ON DELETE CASCADE
            );
            CREATE INDEX IX_Prescriptions_ExpiryDate ON Prescriptions(ExpiryDate);

            CREATE TABLE Medicines (
                Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                Name TEXT NOT NULL,
                Barcode TEXT NULL,
                Notes TEXT NULL,
                IsActive INTEGER NOT NULL
            );

            CREATE TABLE Borrowings (
                Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                CustomerId INTEGER NOT NULL,
                MedicineName TEXT NOT NULL,
                OriginalBarcode TEXT NULL,
                InternalBarcode TEXT NOT NULL,
                Quantity INTEGER NOT NULL,
                BorrowedAt TEXT NOT NULL,
                DueDate TEXT NULL,
                ReturnedAt TEXT NULL,
                Status INTEGER NOT NULL,
                Value TEXT NULL,
                Notes TEXT NULL,
                FOREIGN KEY(CustomerId) REFERENCES Customers(Id) ON DELETE CASCADE
            );
            CREATE UNIQUE INDEX IX_Borrowings_InternalBarcode ON Borrowings(InternalBarcode);

            CREATE TABLE Subscriptions (
                Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                CustomerId INTEGER NOT NULL,
                Title TEXT NOT NULL,
                Barcode TEXT NULL,
                StartDate TEXT NOT NULL,
                NextDueDate TEXT NOT NULL,
                ExpiryDate TEXT NULL,
                Frequency TEXT NOT NULL,
                Status INTEGER NOT NULL,
                Notes TEXT NULL,
                FOREIGN KEY(CustomerId) REFERENCES Customers(Id) ON DELETE CASCADE
            );

            CREATE TABLE Payments (
                Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                CustomerId INTEGER NOT NULL,
                Amount TEXT NOT NULL,
                PaidAt TEXT NOT NULL,
                Method TEXT NOT NULL,
                Reference TEXT NULL,
                Notes TEXT NULL,
                RecordedBy TEXT NOT NULL,
                FOREIGN KEY(CustomerId) REFERENCES Customers(Id) ON DELETE CASCADE
            );

            CREATE TABLE LedgerEntries (
                Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                CustomerId INTEGER NOT NULL,
                Type INTEGER NOT NULL,
                Amount TEXT NOT NULL,
                EntryDate TEXT NOT NULL,
                Description TEXT NOT NULL,
                SourceReference TEXT NULL,
                RecordedBy TEXT NOT NULL,
                FOREIGN KEY(CustomerId) REFERENCES Customers(Id) ON DELETE CASCADE
            );
            CREATE INDEX IX_LedgerEntries_CustomerId_EntryDate
                ON LedgerEntries(CustomerId, EntryDate);
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            DROP TABLE IF EXISTS LedgerEntries;
            DROP TABLE IF EXISTS Payments;
            DROP TABLE IF EXISTS Subscriptions;
            DROP TABLE IF EXISTS Borrowings;
            DROP TABLE IF EXISTS Prescriptions;
            DROP TABLE IF EXISTS Medicines;
            DROP TABLE IF EXISTS Customers;
            DROP TABLE IF EXISTS Users;
            """);
    }
}
