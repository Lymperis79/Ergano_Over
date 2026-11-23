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
        if (ActiveProvider.Contains("Sqlite", StringComparison.OrdinalIgnoreCase))
        {
            migrationBuilder.Sql("""
                CREATE TABLE MedicineStockMovements (
                    Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                    MedicineId INTEGER NOT NULL,
                    Type INTEGER NOT NULL,
                    Quantity TEXT NOT NULL,
                    CountedQuantity TEXT NULL,
                    MovementDate TEXT NOT NULL,
                    ExpiryDate TEXT NULL,
                    BarcodeScanned TEXT NULL,
                    BatchLot TEXT NULL,
                    PerformedBy TEXT NOT NULL,
                    Notes TEXT NULL,
                    CONSTRAINT FK_MedicineStockMovements_Medicines_MedicineId
                        FOREIGN KEY (MedicineId) REFERENCES Medicines (Id) ON DELETE RESTRICT
                );
                CREATE INDEX IX_MedicineStockMovements_MedicineId_ExpiryDate_MovementDate
                    ON MedicineStockMovements (MedicineId, ExpiryDate, MovementDate);
                """);
            return;
        }

        if (ActiveProvider.Contains("SqlServer", StringComparison.OrdinalIgnoreCase))
        {
            migrationBuilder.Sql("""
                CREATE TABLE [MedicineStockMovements] (
                    [Id] int IDENTITY(1,1) NOT NULL CONSTRAINT [PK_MedicineStockMovements] PRIMARY KEY,
                    [MedicineId] int NOT NULL,
                    [Type] int NOT NULL,
                    [Quantity] decimal(18,2) NOT NULL,
                    [CountedQuantity] decimal(18,2) NULL,
                    [MovementDate] datetime2 NOT NULL,
                    [ExpiryDate] datetime2 NULL,
                    [BarcodeScanned] nvarchar(64) NULL,
                    [BatchLot] nvarchar(64) NULL,
                    [PerformedBy] nvarchar(255) NOT NULL,
                    [Notes] nvarchar(max) NULL,
                    CONSTRAINT [FK_MedicineStockMovements_Medicines_MedicineId]
                        FOREIGN KEY ([MedicineId]) REFERENCES [Medicines] ([Id]) ON DELETE NO ACTION
                );
                CREATE INDEX [IX_MedicineStockMovements_MedicineId_ExpiryDate_MovementDate]
                    ON [MedicineStockMovements] ([MedicineId], [ExpiryDate], [MovementDate]);
                """);
            return;
        }

        if (ActiveProvider.Contains("MySql", StringComparison.OrdinalIgnoreCase))
        {
            migrationBuilder.Sql("""
                CREATE TABLE MedicineStockMovements (
                    Id int NOT NULL AUTO_INCREMENT PRIMARY KEY,
                    MedicineId int NOT NULL,
                    Type int NOT NULL,
                    Quantity decimal(18,2) NOT NULL,
                    CountedQuantity decimal(18,2) NULL,
                    MovementDate datetime(6) NOT NULL,
                    ExpiryDate datetime(6) NULL,
                    BarcodeScanned varchar(64) NULL,
                    BatchLot varchar(64) NULL,
                    PerformedBy varchar(255) NOT NULL,
                    Notes text NULL,
                    CONSTRAINT FK_MedicineStockMovements_Medicines_MedicineId
                        FOREIGN KEY (MedicineId) REFERENCES Medicines (Id) ON DELETE RESTRICT,
                    INDEX IX_MedicineStockMovements_MedicineId_ExpiryDate_MovementDate (MedicineId, ExpiryDate, MovementDate)
                );
                """);
            return;
        }

        throw new NotSupportedException($"The stock migration does not support provider '{ActiveProvider}'.");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "MedicineStockMovements");
    }
}
