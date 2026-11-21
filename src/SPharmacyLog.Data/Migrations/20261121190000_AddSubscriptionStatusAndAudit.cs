using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using SPharmacyLog.Data;

#nullable disable

namespace SPharmacyLog.Data.Migrations;

/// <summary>
/// Adds the Executed subscription state and immutable subscription audit history.
/// Existing subscriptions are retained and receive system audit metadata.
/// </summary>
[DbContext(typeof(PharmacyDbContext))]
[Migration("20261121190000_AddSubscriptionStatusAndAudit")]
public sealed class AddSubscriptionStatusAndAudit : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        if (ActiveProvider.Contains("Sqlite", StringComparison.OrdinalIgnoreCase))
        {
            migrationBuilder.Sql("""
                ALTER TABLE Subscriptions ADD COLUMN CreatedAt TEXT NOT NULL DEFAULT '1970-01-01T00:00:00.0000000Z';
                ALTER TABLE Subscriptions ADD COLUMN CreatedBy TEXT NOT NULL DEFAULT 'system';
                ALTER TABLE Subscriptions ADD COLUMN LastModifiedAt TEXT NOT NULL DEFAULT '1970-01-01T00:00:00.0000000Z';
                ALTER TABLE Subscriptions ADD COLUMN LastModifiedBy TEXT NOT NULL DEFAULT 'system';
                UPDATE Subscriptions SET CreatedAt = CURRENT_TIMESTAMP, LastModifiedAt = CURRENT_TIMESTAMP;

                UPDATE Subscriptions SET Status = CASE WHEN Status = 0 THEN 0 ELSE 1 END;
                CREATE INDEX IX_Subscriptions_Status ON Subscriptions(Status);

                CREATE TABLE SubscriptionAuditEntries (
                    Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                    SubscriptionId INTEGER NOT NULL,
                    Action TEXT NOT NULL,
                    PreviousStatus INTEGER NULL,
                    NewStatus INTEGER NOT NULL,
                    ChangedBy TEXT NOT NULL,
                    ChangedAt TEXT NOT NULL,
                    Details TEXT NULL,
                    CONSTRAINT FK_SubscriptionAuditEntries_Subscriptions_SubscriptionId
                        FOREIGN KEY (SubscriptionId) REFERENCES Subscriptions(Id) ON DELETE RESTRICT
                );
                CREATE INDEX IX_SubscriptionAuditEntries_SubscriptionId_ChangedAt
                    ON SubscriptionAuditEntries(SubscriptionId, ChangedAt);
                INSERT INTO SubscriptionAuditEntries
                    (SubscriptionId, Action, PreviousStatus, NewStatus, ChangedBy, ChangedAt, Details)
                SELECT Id, 'Imported existing record', NULL, Status, CreatedBy, CreatedAt,
                    'Original creator unavailable; audit metadata initialized during migration.'
                FROM Subscriptions;
                """);
            return;
        }

        if (ActiveProvider.Contains("SqlServer", StringComparison.OrdinalIgnoreCase))
        {
            migrationBuilder.Sql("""
                ALTER TABLE [Subscriptions] ADD
                    [CreatedAt] datetime2 NOT NULL CONSTRAINT [DF_Subscriptions_CreatedAt] DEFAULT (SYSUTCDATETIME()),
                    [CreatedBy] nvarchar(255) NOT NULL CONSTRAINT [DF_Subscriptions_CreatedBy] DEFAULT (N'system'),
                    [LastModifiedAt] datetime2 NOT NULL CONSTRAINT [DF_Subscriptions_LastModifiedAt] DEFAULT (SYSUTCDATETIME()),
                    [LastModifiedBy] nvarchar(255) NOT NULL CONSTRAINT [DF_Subscriptions_LastModifiedBy] DEFAULT (N'system');

                UPDATE [Subscriptions] SET [Status] = CASE WHEN [Status] = 0 THEN 0 ELSE 1 END;
                CREATE INDEX [IX_Subscriptions_Status] ON [Subscriptions]([Status]);

                CREATE TABLE [SubscriptionAuditEntries] (
                    [Id] int IDENTITY(1,1) NOT NULL CONSTRAINT [PK_SubscriptionAuditEntries] PRIMARY KEY,
                    [SubscriptionId] int NOT NULL,
                    [Action] nvarchar(255) NOT NULL,
                    [PreviousStatus] int NULL,
                    [NewStatus] int NOT NULL,
                    [ChangedBy] nvarchar(255) NOT NULL,
                    [ChangedAt] datetime2 NOT NULL,
                    [Details] nvarchar(max) NULL,
                    CONSTRAINT [FK_SubscriptionAuditEntries_Subscriptions_SubscriptionId]
                        FOREIGN KEY ([SubscriptionId]) REFERENCES [Subscriptions]([Id]) ON DELETE NO ACTION
                );
                CREATE INDEX [IX_SubscriptionAuditEntries_SubscriptionId_ChangedAt]
                    ON [SubscriptionAuditEntries]([SubscriptionId], [ChangedAt]);
                INSERT INTO [SubscriptionAuditEntries]
                    ([SubscriptionId], [Action], [PreviousStatus], [NewStatus], [ChangedBy], [ChangedAt], [Details])
                SELECT [Id], N'Imported existing record', NULL, [Status], [CreatedBy], [CreatedAt],
                    N'Original creator unavailable; audit metadata initialized during migration.'
                FROM [Subscriptions];
                """);
            return;
        }

        if (ActiveProvider.Contains("MySql", StringComparison.OrdinalIgnoreCase))
        {
            migrationBuilder.Sql("""
                ALTER TABLE Subscriptions
                    ADD COLUMN CreatedAt datetime(6) NOT NULL DEFAULT '1970-01-01 00:00:00.000000',
                    ADD COLUMN CreatedBy varchar(255) NOT NULL DEFAULT 'system',
                    ADD COLUMN LastModifiedAt datetime(6) NOT NULL DEFAULT '1970-01-01 00:00:00.000000',
                    ADD COLUMN LastModifiedBy varchar(255) NOT NULL DEFAULT 'system';
                UPDATE Subscriptions SET CreatedAt = UTC_TIMESTAMP(6), LastModifiedAt = UTC_TIMESTAMP(6);
                UPDATE Subscriptions SET Status = CASE WHEN Status = 0 THEN 0 ELSE 1 END;
                CREATE INDEX IX_Subscriptions_Status ON Subscriptions(Status);

                CREATE TABLE SubscriptionAuditEntries (
                    Id int NOT NULL AUTO_INCREMENT PRIMARY KEY,
                    SubscriptionId int NOT NULL,
                    Action varchar(255) NOT NULL,
                    PreviousStatus int NULL,
                    NewStatus int NOT NULL,
                    ChangedBy varchar(255) NOT NULL,
                    ChangedAt datetime(6) NOT NULL,
                    Details text NULL,
                    INDEX IX_SubscriptionAuditEntries_SubscriptionId_ChangedAt (SubscriptionId, ChangedAt),
                    CONSTRAINT FK_SubscriptionAuditEntries_Subscriptions_SubscriptionId
                        FOREIGN KEY (SubscriptionId) REFERENCES Subscriptions(Id) ON DELETE CASCADE
                ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
                INSERT INTO SubscriptionAuditEntries
                    (SubscriptionId, Action, PreviousStatus, NewStatus, ChangedBy, ChangedAt, Details)
                SELECT Id, 'Imported existing record', NULL, Status, CreatedBy, CreatedAt,
                    'Original creator unavailable; audit metadata initialized during migration.'
                FROM Subscriptions;
                """);
            return;
        }

        throw new NotSupportedException($"The subscription audit migration does not support provider '{ActiveProvider}'.");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        if (ActiveProvider.Contains("Sqlite", StringComparison.OrdinalIgnoreCase))
        {
            migrationBuilder.Sql("""
                DROP TABLE IF EXISTS SubscriptionAuditEntries;
                DROP INDEX IF EXISTS IX_Subscriptions_Status;
                ALTER TABLE Subscriptions DROP COLUMN CreatedAt;
                ALTER TABLE Subscriptions DROP COLUMN CreatedBy;
                ALTER TABLE Subscriptions DROP COLUMN LastModifiedAt;
                ALTER TABLE Subscriptions DROP COLUMN LastModifiedBy;
                """);
            return;
        }

        if (ActiveProvider.Contains("SqlServer", StringComparison.OrdinalIgnoreCase))
        {
            migrationBuilder.Sql("""
                DROP TABLE [SubscriptionAuditEntries];
                DROP INDEX [IX_Subscriptions_Status] ON [Subscriptions];
                ALTER TABLE [Subscriptions] DROP CONSTRAINT [DF_Subscriptions_CreatedAt];
                ALTER TABLE [Subscriptions] DROP CONSTRAINT [DF_Subscriptions_CreatedBy];
                ALTER TABLE [Subscriptions] DROP CONSTRAINT [DF_Subscriptions_LastModifiedAt];
                ALTER TABLE [Subscriptions] DROP CONSTRAINT [DF_Subscriptions_LastModifiedBy];
                ALTER TABLE [Subscriptions] DROP COLUMN [CreatedAt], [CreatedBy], [LastModifiedAt], [LastModifiedBy];
                """);
            return;
        }

        if (ActiveProvider.Contains("MySql", StringComparison.OrdinalIgnoreCase))
        {
            migrationBuilder.Sql("""
                DROP TABLE SubscriptionAuditEntries;
                DROP INDEX IX_Subscriptions_Status ON Subscriptions;
                ALTER TABLE Subscriptions
                    DROP COLUMN CreatedAt,
                    DROP COLUMN CreatedBy,
                    DROP COLUMN LastModifiedAt,
                    DROP COLUMN LastModifiedBy;
                """);
            return;
        }

        throw new NotSupportedException($"The subscription audit migration does not support provider '{ActiveProvider}'.");
    }
}
