using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using SPharmacyLog.Data;

#nullable disable

namespace SPharmacyLog.Data.Migrations;

/// <summary>
/// Adds alternate medicine barcodes and GS1 serialized-pack tracking.
/// Existing medicine and borrowing rows are retained; the new pack link is nullable.
/// Provider-specific SQL keeps identity columns and date types correct on all supported databases.
/// </summary>
[DbContext(typeof(PharmacyDbContext))]
[Migration("20261121160000_AddGs1SerializedPackTracking")]
public sealed class AddGs1SerializedPackTracking : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        if (ActiveProvider.Contains("Sqlite", StringComparison.OrdinalIgnoreCase))
        {
            migrationBuilder.Sql("""
                CREATE TABLE MedicineBarcodes (
                    Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                    MedicineId INTEGER NOT NULL,
                    BarcodeValue TEXT NOT NULL,
                    BarcodeType TEXT NOT NULL,
                    CONSTRAINT FK_MedicineBarcodes_Medicines_MedicineId
                        FOREIGN KEY (MedicineId) REFERENCES Medicines(Id) ON DELETE CASCADE
                );
                CREATE UNIQUE INDEX IX_MedicineBarcodes_BarcodeValue ON MedicineBarcodes(BarcodeValue);
                CREATE INDEX IX_MedicineBarcodes_MedicineId ON MedicineBarcodes(MedicineId);

                CREATE TABLE SerializedPackScans (
                    Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                    RawGs1String TEXT NOT NULL CHECK(length(RawGs1String) <= 255),
                    Gtin14 TEXT NOT NULL CHECK(length(Gtin14) = 14),
                    EofEan13 TEXT NULL CHECK(EofEan13 IS NULL OR length(EofEan13) = 13),
                    SerialNumber TEXT NOT NULL CHECK(length(SerialNumber) <= 20),
                    ExpiryDate TEXT NOT NULL,
                    BatchLot TEXT NOT NULL CHECK(length(BatchLot) <= 20),
                    HmvoStatus TEXT NOT NULL DEFAULT 'ACTIVE',
                    ScannedAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
                    MedicineId INTEGER NULL,
                    CONSTRAINT FK_SerializedPackScans_Medicines_MedicineId
                        FOREIGN KEY (MedicineId) REFERENCES Medicines(Id) ON DELETE SET NULL
                );
                CREATE UNIQUE INDEX IX_SerializedPackScans_Gtin14_SerialNumber
                    ON SerializedPackScans(Gtin14, SerialNumber);
                CREATE INDEX IX_SerializedPackScans_EofEan13 ON SerializedPackScans(EofEan13);
                CREATE INDEX IX_SerializedPackScans_MedicineId ON SerializedPackScans(MedicineId);

                ALTER TABLE Borrowings
                    ADD COLUMN SerializedPackScanId INTEGER NULL
                    REFERENCES SerializedPackScans(Id) ON DELETE RESTRICT;
                CREATE INDEX IX_Borrowings_SerializedPackScanId
                    ON Borrowings(SerializedPackScanId);
                """);
            return;
        }

        if (ActiveProvider.Contains("SqlServer", StringComparison.OrdinalIgnoreCase))
        {
            migrationBuilder.Sql("""
                CREATE TABLE [MedicineBarcodes] (
                    [Id] int IDENTITY(1,1) NOT NULL CONSTRAINT [PK_MedicineBarcodes] PRIMARY KEY,
                    [MedicineId] int NOT NULL,
                    [BarcodeValue] nvarchar(64) NOT NULL,
                    [BarcodeType] nvarchar(24) NOT NULL,
                    CONSTRAINT [FK_MedicineBarcodes_Medicines_MedicineId]
                        FOREIGN KEY ([MedicineId]) REFERENCES [Medicines]([Id]) ON DELETE CASCADE
                );
                CREATE UNIQUE INDEX [IX_MedicineBarcodes_BarcodeValue] ON [MedicineBarcodes]([BarcodeValue]);
                CREATE INDEX [IX_MedicineBarcodes_MedicineId] ON [MedicineBarcodes]([MedicineId]);

                CREATE TABLE [SerializedPackScans] (
                    [Id] int IDENTITY(1,1) NOT NULL CONSTRAINT [PK_SerializedPackScans] PRIMARY KEY,
                    [RawGs1String] varchar(255) NOT NULL,
                    [Gtin14] char(14) NOT NULL,
                    [EofEan13] char(13) NULL,
                    [SerialNumber] varchar(20) NOT NULL,
                    [ExpiryDate] date NOT NULL,
                    [BatchLot] varchar(20) NOT NULL,
                    [HmvoStatus] varchar(20) NOT NULL CONSTRAINT [DF_SerializedPackScans_HmvoStatus] DEFAULT ('ACTIVE'),
                    [ScannedAt] datetime2 NOT NULL CONSTRAINT [DF_SerializedPackScans_ScannedAt] DEFAULT (CURRENT_TIMESTAMP),
                    [MedicineId] int NULL,
                    CONSTRAINT [FK_SerializedPackScans_Medicines_MedicineId]
                        FOREIGN KEY ([MedicineId]) REFERENCES [Medicines]([Id]) ON DELETE SET NULL
                );
                CREATE UNIQUE INDEX [IX_SerializedPackScans_Gtin14_SerialNumber]
                    ON [SerializedPackScans]([Gtin14], [SerialNumber]);
                CREATE INDEX [IX_SerializedPackScans_EofEan13] ON [SerializedPackScans]([EofEan13]);
                CREATE INDEX [IX_SerializedPackScans_MedicineId] ON [SerializedPackScans]([MedicineId]);

                ALTER TABLE [Borrowings] ADD [SerializedPackScanId] int NULL;
                ALTER TABLE [Borrowings] ADD CONSTRAINT [FK_Borrowings_SerializedPackScans_SerializedPackScanId]
                    FOREIGN KEY ([SerializedPackScanId]) REFERENCES [SerializedPackScans]([Id]) ON DELETE NO ACTION;
                CREATE INDEX [IX_Borrowings_SerializedPackScanId] ON [Borrowings]([SerializedPackScanId]);
                """);
            return;
        }

        if (ActiveProvider.Contains("MySql", StringComparison.OrdinalIgnoreCase))
        {
            migrationBuilder.Sql("""
                CREATE TABLE MedicineBarcodes (
                    Id int NOT NULL AUTO_INCREMENT PRIMARY KEY,
                    MedicineId int NOT NULL,
                    BarcodeValue varchar(64) NOT NULL,
                    BarcodeType varchar(24) NOT NULL,
                    INDEX IX_MedicineBarcodes_MedicineId (MedicineId),
                    UNIQUE INDEX IX_MedicineBarcodes_BarcodeValue (BarcodeValue),
                    CONSTRAINT FK_MedicineBarcodes_Medicines_MedicineId
                        FOREIGN KEY (MedicineId) REFERENCES Medicines(Id) ON DELETE CASCADE
                ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

                CREATE TABLE SerializedPackScans (
                    Id int NOT NULL AUTO_INCREMENT PRIMARY KEY,
                    RawGs1String varchar(255) NOT NULL,
                    Gtin14 char(14) NOT NULL,
                    EofEan13 char(13) NULL,
                    SerialNumber varchar(20) NOT NULL,
                    ExpiryDate date NOT NULL,
                    BatchLot varchar(20) NOT NULL,
                    HmvoStatus varchar(20) NOT NULL DEFAULT 'ACTIVE',
                    ScannedAt datetime(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
                    MedicineId int NULL,
                    UNIQUE INDEX IX_SerializedPackScans_Gtin14_SerialNumber (Gtin14, SerialNumber),
                    INDEX IX_SerializedPackScans_EofEan13 (EofEan13),
                    INDEX IX_SerializedPackScans_MedicineId (MedicineId),
                    CONSTRAINT FK_SerializedPackScans_Medicines_MedicineId
                        FOREIGN KEY (MedicineId) REFERENCES Medicines(Id) ON DELETE SET NULL
                ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

                ALTER TABLE Borrowings ADD COLUMN SerializedPackScanId int NULL;
                ALTER TABLE Borrowings ADD INDEX IX_Borrowings_SerializedPackScanId (SerializedPackScanId);
                ALTER TABLE Borrowings ADD CONSTRAINT FK_Borrowings_SerializedPackScans_SerializedPackScanId
                    FOREIGN KEY (SerializedPackScanId) REFERENCES SerializedPackScans(Id) ON DELETE RESTRICT;
                """);
            return;
        }

        throw new NotSupportedException($"The GS1 serialized-pack migration does not support provider '{ActiveProvider}'.");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        if (ActiveProvider.Contains("Sqlite", StringComparison.OrdinalIgnoreCase))
        {
            migrationBuilder.Sql("""
                DROP INDEX IF EXISTS IX_Borrowings_SerializedPackScanId;
                ALTER TABLE Borrowings DROP COLUMN SerializedPackScanId;
                DROP TABLE IF EXISTS SerializedPackScans;
                DROP TABLE IF EXISTS MedicineBarcodes;
                """);
            return;
        }

        if (ActiveProvider.Contains("SqlServer", StringComparison.OrdinalIgnoreCase))
        {
            migrationBuilder.Sql("""
                ALTER TABLE [Borrowings] DROP CONSTRAINT [FK_Borrowings_SerializedPackScans_SerializedPackScanId];
                DROP INDEX [IX_Borrowings_SerializedPackScanId] ON [Borrowings];
                ALTER TABLE [Borrowings] DROP COLUMN [SerializedPackScanId];
                DROP TABLE [SerializedPackScans];
                DROP TABLE [MedicineBarcodes];
                """);
            return;
        }

        if (ActiveProvider.Contains("MySql", StringComparison.OrdinalIgnoreCase))
        {
            migrationBuilder.Sql("""
                ALTER TABLE Borrowings DROP FOREIGN KEY FK_Borrowings_SerializedPackScans_SerializedPackScanId;
                ALTER TABLE Borrowings DROP INDEX IX_Borrowings_SerializedPackScanId;
                ALTER TABLE Borrowings DROP COLUMN SerializedPackScanId;
                DROP TABLE SerializedPackScans;
                DROP TABLE MedicineBarcodes;
                """);
            return;
        }

        throw new NotSupportedException($"The GS1 serialized-pack migration does not support provider '{ActiveProvider}'.");
    }
}
