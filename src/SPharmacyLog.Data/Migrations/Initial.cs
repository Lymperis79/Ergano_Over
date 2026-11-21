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
        if ((ActiveProvider ?? string.Empty).Contains("Sqlite", StringComparison.OrdinalIgnoreCase))
        {
            CreateSqliteSchema(migrationBuilder);
            return;
        }

        if (ActiveProvider.Contains("SqlServer", StringComparison.OrdinalIgnoreCase))
        {
            migrationBuilder.Sql("""
                CREATE TABLE [Users] (
                    [Id] int IDENTITY(1,1) NOT NULL PRIMARY KEY,
                    [Username] nvarchar(450) NOT NULL,
                    [PasswordHash] nvarchar(max) NOT NULL,
                    [Role] int NOT NULL,
                    [AllowedMenus] nvarchar(max) NULL,
                    [IsActive] bit NOT NULL,
                    [CanSaveCredentials] bit NOT NULL,
                    [CreatedAt] datetime2 NOT NULL
                );
                CREATE UNIQUE INDEX [IX_Users_Username] ON [Users]([Username]);

                CREATE TABLE [Customers] (
                    [Id] int IDENTITY(1,1) NOT NULL PRIMARY KEY,
                    [CustomerNumber] nvarchar(450) NOT NULL,
                    [FullName] nvarchar(max) NOT NULL,
                    [Phone] nvarchar(max) NOT NULL,
                    [Address] nvarchar(max) NULL,
                    [Notes] nvarchar(max) NULL,
                    [IsActive] bit NOT NULL,
                    [CreatedAt] datetime2 NOT NULL
                );
                CREATE UNIQUE INDEX [IX_Customers_CustomerNumber] ON [Customers]([CustomerNumber]);

                CREATE TABLE [Prescriptions] (
                    [Id] int IDENTITY(1,1) NOT NULL PRIMARY KEY,
                    [CustomerId] int NOT NULL,
                    [PrescriptionNumber] nvarchar(max) NULL,
                    [MedicineName] nvarchar(max) NOT NULL,
                    [Barcode] nvarchar(max) NULL,
                    [StartDate] datetime2 NOT NULL,
                    [ExpiryDate] datetime2 NOT NULL,
                    [Status] int NOT NULL,
                    [Notes] nvarchar(max) NULL,
                    [CreatedAt] datetime2 NOT NULL,
                    CONSTRAINT [FK_Prescriptions_Customers_CustomerId] FOREIGN KEY ([CustomerId]) REFERENCES [Customers]([Id]) ON DELETE CASCADE
                );
                CREATE INDEX [IX_Prescriptions_ExpiryDate] ON [Prescriptions]([ExpiryDate]);

                CREATE TABLE [Medicines] (
                    [Id] int IDENTITY(1,1) NOT NULL PRIMARY KEY,
                    [Name] nvarchar(max) NOT NULL,
                    [Barcode] nvarchar(max) NULL,
                    [Notes] nvarchar(max) NULL,
                    [IsActive] bit NOT NULL
                );

                CREATE TABLE [Borrowings] (
                    [Id] int IDENTITY(1,1) NOT NULL PRIMARY KEY,
                    [CustomerId] int NOT NULL,
                    [MedicineName] nvarchar(max) NOT NULL,
                    [OriginalBarcode] nvarchar(max) NULL,
                    [InternalBarcode] nvarchar(450) NOT NULL,
                    [Quantity] int NOT NULL,
                    [BorrowedAt] datetime2 NOT NULL,
                    [DueDate] datetime2 NULL,
                    [ReturnedAt] datetime2 NULL,
                    [Status] int NOT NULL,
                    [Value] decimal(18,2) NULL,
                    [Notes] nvarchar(max) NULL,
                    CONSTRAINT [FK_Borrowings_Customers_CustomerId] FOREIGN KEY ([CustomerId]) REFERENCES [Customers]([Id]) ON DELETE CASCADE
                );
                CREATE UNIQUE INDEX [IX_Borrowings_InternalBarcode] ON [Borrowings]([InternalBarcode]);

                CREATE TABLE [Subscriptions] (
                    [Id] int IDENTITY(1,1) NOT NULL PRIMARY KEY,
                    [CustomerId] int NOT NULL,
                    [Title] nvarchar(max) NOT NULL,
                    [Barcode] nvarchar(max) NULL,
                    [StartDate] datetime2 NOT NULL,
                    [NextDueDate] datetime2 NOT NULL,
                    [ExpiryDate] datetime2 NULL,
                    [Frequency] nvarchar(max) NOT NULL,
                    [Status] int NOT NULL,
                    [Notes] nvarchar(max) NULL,
                    CONSTRAINT [FK_Subscriptions_Customers_CustomerId] FOREIGN KEY ([CustomerId]) REFERENCES [Customers]([Id]) ON DELETE CASCADE
                );

                CREATE TABLE [Payments] (
                    [Id] int IDENTITY(1,1) NOT NULL PRIMARY KEY,
                    [CustomerId] int NOT NULL,
                    [Amount] decimal(18,2) NOT NULL,
                    [PaidAt] datetime2 NOT NULL,
                    [Method] nvarchar(max) NOT NULL,
                    [Reference] nvarchar(max) NULL,
                    [Notes] nvarchar(max) NULL,
                    [RecordedBy] nvarchar(max) NOT NULL,
                    CONSTRAINT [FK_Payments_Customers_CustomerId] FOREIGN KEY ([CustomerId]) REFERENCES [Customers]([Id]) ON DELETE CASCADE
                );

                CREATE TABLE [LedgerEntries] (
                    [Id] int IDENTITY(1,1) NOT NULL PRIMARY KEY,
                    [CustomerId] int NOT NULL,
                    [Type] int NOT NULL,
                    [Amount] decimal(18,2) NOT NULL,
                    [EntryDate] datetime2 NOT NULL,
                    [Description] nvarchar(max) NOT NULL,
                    [SourceReference] nvarchar(max) NULL,
                    [RecordedBy] nvarchar(max) NOT NULL,
                    CONSTRAINT [FK_LedgerEntries_Customers_CustomerId] FOREIGN KEY ([CustomerId]) REFERENCES [Customers]([Id]) ON DELETE CASCADE
                );
                CREATE INDEX [IX_LedgerEntries_CustomerId_EntryDate] ON [LedgerEntries]([CustomerId], [EntryDate]);
                """);
            return;
        }

        if (ActiveProvider.Contains("MySql", StringComparison.OrdinalIgnoreCase))
        {
            migrationBuilder.Sql("""
                CREATE TABLE Users (
                    Id int NOT NULL AUTO_INCREMENT PRIMARY KEY,
                    Username varchar(191) NOT NULL,
                    PasswordHash longtext NOT NULL,
                    Role int NOT NULL,
                    AllowedMenus longtext NULL,
                    IsActive tinyint(1) NOT NULL,
                    CanSaveCredentials tinyint(1) NOT NULL,
                    CreatedAt datetime(6) NOT NULL,
                    UNIQUE INDEX IX_Users_Username (Username)
                ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

                CREATE TABLE Customers (
                    Id int NOT NULL AUTO_INCREMENT PRIMARY KEY,
                    CustomerNumber varchar(191) NOT NULL,
                    FullName longtext NOT NULL,
                    Phone longtext NOT NULL,
                    Address longtext NULL,
                    Notes longtext NULL,
                    IsActive tinyint(1) NOT NULL,
                    CreatedAt datetime(6) NOT NULL,
                    UNIQUE INDEX IX_Customers_CustomerNumber (CustomerNumber)
                ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

                CREATE TABLE Prescriptions (
                    Id int NOT NULL AUTO_INCREMENT PRIMARY KEY,
                    CustomerId int NOT NULL,
                    PrescriptionNumber longtext NULL,
                    MedicineName longtext NOT NULL,
                    Barcode longtext NULL,
                    StartDate datetime(6) NOT NULL,
                    ExpiryDate datetime(6) NOT NULL,
                    Status int NOT NULL,
                    Notes longtext NULL,
                    CreatedAt datetime(6) NOT NULL,
                    INDEX IX_Prescriptions_ExpiryDate (ExpiryDate),
                    CONSTRAINT FK_Prescriptions_Customers_CustomerId FOREIGN KEY (CustomerId) REFERENCES Customers(Id) ON DELETE CASCADE
                ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

                CREATE TABLE Medicines (
                    Id int NOT NULL AUTO_INCREMENT PRIMARY KEY,
                    Name longtext NOT NULL,
                    Barcode longtext NULL,
                    Notes longtext NULL,
                    IsActive tinyint(1) NOT NULL
                ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

                CREATE TABLE Borrowings (
                    Id int NOT NULL AUTO_INCREMENT PRIMARY KEY,
                    CustomerId int NOT NULL,
                    MedicineName longtext NOT NULL,
                    OriginalBarcode longtext NULL,
                    InternalBarcode varchar(191) NOT NULL,
                    Quantity int NOT NULL,
                    BorrowedAt datetime(6) NOT NULL,
                    DueDate datetime(6) NULL,
                    ReturnedAt datetime(6) NULL,
                    Status int NOT NULL,
                    Value decimal(18,2) NULL,
                    Notes longtext NULL,
                    UNIQUE INDEX IX_Borrowings_InternalBarcode (InternalBarcode),
                    CONSTRAINT FK_Borrowings_Customers_CustomerId FOREIGN KEY (CustomerId) REFERENCES Customers(Id) ON DELETE CASCADE
                ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

                CREATE TABLE Subscriptions (
                    Id int NOT NULL AUTO_INCREMENT PRIMARY KEY,
                    CustomerId int NOT NULL,
                    Title longtext NOT NULL,
                    Barcode longtext NULL,
                    StartDate datetime(6) NOT NULL,
                    NextDueDate datetime(6) NOT NULL,
                    ExpiryDate datetime(6) NULL,
                    Frequency longtext NOT NULL,
                    Status int NOT NULL,
                    Notes longtext NULL,
                    CONSTRAINT FK_Subscriptions_Customers_CustomerId FOREIGN KEY (CustomerId) REFERENCES Customers(Id) ON DELETE CASCADE
                ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

                CREATE TABLE Payments (
                    Id int NOT NULL AUTO_INCREMENT PRIMARY KEY,
                    CustomerId int NOT NULL,
                    Amount decimal(18,2) NOT NULL,
                    PaidAt datetime(6) NOT NULL,
                    Method longtext NOT NULL,
                    Reference longtext NULL,
                    Notes longtext NULL,
                    RecordedBy longtext NOT NULL,
                    CONSTRAINT FK_Payments_Customers_CustomerId FOREIGN KEY (CustomerId) REFERENCES Customers(Id) ON DELETE CASCADE
                ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

                CREATE TABLE LedgerEntries (
                    Id int NOT NULL AUTO_INCREMENT PRIMARY KEY,
                    CustomerId int NOT NULL,
                    Type int NOT NULL,
                    Amount decimal(18,2) NOT NULL,
                    EntryDate datetime(6) NOT NULL,
                    Description longtext NOT NULL,
                    SourceReference longtext NULL,
                    RecordedBy longtext NOT NULL,
                    INDEX IX_LedgerEntries_CustomerId_EntryDate (CustomerId, EntryDate),
                    CONSTRAINT FK_LedgerEntries_Customers_CustomerId FOREIGN KEY (CustomerId) REFERENCES Customers(Id) ON DELETE CASCADE
                ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
                """);
            return;
        }

        throw new NotSupportedException($"The initial pharmacy migration does not support provider '{ActiveProvider}'.");
    }

    private static void CreateSqliteSchema(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
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
            CREATE INDEX IX_LedgerEntries_CustomerId_EntryDate ON LedgerEntries(CustomerId, EntryDate);
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        if (ActiveProvider.Contains("SqlServer", StringComparison.OrdinalIgnoreCase))
        {
            migrationBuilder.Sql("""
                DROP TABLE [LedgerEntries];
                DROP TABLE [Payments];
                DROP TABLE [Subscriptions];
                DROP TABLE [Borrowings];
                DROP TABLE [Prescriptions];
                DROP TABLE [Medicines];
                DROP TABLE [Customers];
                DROP TABLE [Users];
                """);
            return;
        }

        migrationBuilder.Sql("""
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
