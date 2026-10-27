using Microsoft.EntityFrameworkCore;

namespace ErganiManager.Data;

/// <summary>
/// The main database is created with EnsureCreated(), which never adds new columns to
/// tables that already exist. This adds the columns introduced after the first release,
/// idempotently, for every supported provider. Safe to call on every startup.
/// </summary>
public static class DbSchemaUpdater
{
    private sealed record ColumnPatch(string Table, string Column, string Sqlite, string SqlServer, string MariaDb);

    private sealed record TablePatch(string Table, string[] Sqlite, string[] SqlServer, string[] MariaDb);

    /// <summary>New tables for databases created before the table existed (EnsureCreated skips them).</summary>
    private static readonly TablePatch[] TablePatches =
    {
        new("Leaves",
            Sqlite: new[]
            {
                @"CREATE TABLE IF NOT EXISTS Leaves (
                    Id INTEGER NOT NULL CONSTRAINT PK_Leaves PRIMARY KEY AUTOINCREMENT,
                    EmployeeId INTEGER NOT NULL,
                    BranchId INTEGER NOT NULL,
                    LeaveDate TEXT NOT NULL,
                    LeaveTypeCode TEXT NOT NULL,
                    StartTime TEXT NULL,
                    EndTime TEXT NULL,
                    ReferenceYear INTEGER NULL,
                    EntitledDays INTEGER NULL,
                    Comments TEXT NULL,
                    SubmittedToErgani INTEGER NOT NULL DEFAULT 0,
                    SubmissionId TEXT NULL,
                    Protocol TEXT NULL,
                    ResponseRawJson TEXT NULL,
                    RequestPayloadJson TEXT NULL,
                    SubmittedDate TEXT NULL,
                    HttpStatusCode INTEGER NULL,
                    LastError TEXT NULL,
                    CreatedAt TEXT NOT NULL,
                    CONSTRAINT FK_Leaves_Employees_EmployeeId FOREIGN KEY (EmployeeId) REFERENCES Employees (Id) ON DELETE CASCADE,
                    CONSTRAINT FK_Leaves_Branches_BranchId FOREIGN KEY (BranchId) REFERENCES Branches (Id) ON DELETE NO ACTION)",
                "CREATE INDEX IF NOT EXISTS IX_Leaves_EmployeeId_LeaveDate ON Leaves (EmployeeId, LeaveDate)",
                "CREATE INDEX IF NOT EXISTS IX_Leaves_BranchId ON Leaves (BranchId)"
            },
            SqlServer: new[]
            {
                @"CREATE TABLE Leaves (
                    Id int NOT NULL IDENTITY(1,1) CONSTRAINT PK_Leaves PRIMARY KEY,
                    EmployeeId int NOT NULL,
                    BranchId int NOT NULL,
                    LeaveDate date NOT NULL,
                    LeaveTypeCode nvarchar(10) NOT NULL,
                    StartTime time NULL,
                    EndTime time NULL,
                    ReferenceYear int NULL,
                    EntitledDays int NULL,
                    Comments nvarchar(200) NULL,
                    SubmittedToErgani bit NOT NULL CONSTRAINT DF_Leaves_SubmittedToErgani DEFAULT 0,
                    SubmissionId nvarchar(100) NULL,
                    Protocol nvarchar(100) NULL,
                    ResponseRawJson nvarchar(max) NULL,
                    RequestPayloadJson nvarchar(max) NULL,
                    SubmittedDate date NULL,
                    HttpStatusCode int NULL,
                    LastError nvarchar(1000) NULL,
                    CreatedAt datetime2 NOT NULL,
                    CONSTRAINT FK_Leaves_Employees_EmployeeId FOREIGN KEY (EmployeeId) REFERENCES Employees (Id) ON DELETE CASCADE,
                    CONSTRAINT FK_Leaves_Branches_BranchId FOREIGN KEY (BranchId) REFERENCES Branches (Id))",
                "CREATE INDEX IX_Leaves_EmployeeId_LeaveDate ON Leaves (EmployeeId, LeaveDate)",
                "CREATE INDEX IX_Leaves_BranchId ON Leaves (BranchId)"
            },
            MariaDb: new[]
            {
                @"CREATE TABLE IF NOT EXISTS Leaves (
                    Id INT NOT NULL AUTO_INCREMENT,
                    EmployeeId INT NOT NULL,
                    BranchId INT NOT NULL,
                    LeaveDate DATE NOT NULL,
                    LeaveTypeCode VARCHAR(10) NOT NULL,
                    StartTime TIME(6) NULL,
                    EndTime TIME(6) NULL,
                    ReferenceYear INT NULL,
                    EntitledDays INT NULL,
                    Comments VARCHAR(200) NULL,
                    SubmittedToErgani TINYINT(1) NOT NULL DEFAULT 0,
                    SubmissionId VARCHAR(100) NULL,
                    Protocol VARCHAR(100) NULL,
                    ResponseRawJson LONGTEXT NULL,
                    RequestPayloadJson LONGTEXT NULL,
                    SubmittedDate DATE NULL,
                    HttpStatusCode INT NULL,
                    LastError VARCHAR(1000) NULL,
                    CreatedAt DATETIME(6) NOT NULL,
                    PRIMARY KEY (Id),
                    INDEX IX_Leaves_EmployeeId_LeaveDate (EmployeeId, LeaveDate),
                    INDEX IX_Leaves_BranchId (BranchId),
                    CONSTRAINT FK_Leaves_Employees_EmployeeId FOREIGN KEY (EmployeeId) REFERENCES Employees (Id) ON DELETE CASCADE,
                    CONSTRAINT FK_Leaves_Branches_BranchId FOREIGN KEY (BranchId) REFERENCES Branches (Id) ON DELETE NO ACTION)"
            }),
    };

    private static readonly ColumnPatch[] Patches =
    {
        new("Companies", "AlertEvents",
            "ALTER TABLE Companies ADD COLUMN AlertEvents INTEGER NOT NULL DEFAULT 1",
            "ALTER TABLE Companies ADD AlertEvents int NOT NULL CONSTRAINT DF_Companies_AlertEvents DEFAULT 1",
            "ALTER TABLE Companies ADD COLUMN AlertEvents INT NOT NULL DEFAULT 1"),
        new("Companies", "ClockInAlertMinutes",
            "ALTER TABLE Companies ADD COLUMN ClockInAlertMinutes INTEGER NOT NULL DEFAULT 10",
            "ALTER TABLE Companies ADD ClockInAlertMinutes int NOT NULL CONSTRAINT DF_Companies_ClockInAlertMinutes DEFAULT 10",
            "ALTER TABLE Companies ADD COLUMN ClockInAlertMinutes INT NOT NULL DEFAULT 10"),
        new("Schedules", "Source",
            "ALTER TABLE Schedules ADD COLUMN Source TEXT NOT NULL DEFAULT 'Manual'",
            "ALTER TABLE Schedules ADD Source nvarchar(30) NOT NULL CONSTRAINT DF_Schedules_Source DEFAULT 'Manual'",
            "ALTER TABLE Schedules ADD COLUMN Source VARCHAR(30) NOT NULL DEFAULT 'Manual'"),
        new("Overtimes", "ResponseRawJson", "ALTER TABLE Overtimes ADD COLUMN ResponseRawJson TEXT NULL", "ALTER TABLE Overtimes ADD ResponseRawJson nvarchar(max) NULL", "ALTER TABLE Overtimes ADD COLUMN ResponseRawJson LONGTEXT NULL"),
        new("Overtimes", "RequestPayloadJson", "ALTER TABLE Overtimes ADD COLUMN RequestPayloadJson TEXT NULL", "ALTER TABLE Overtimes ADD RequestPayloadJson nvarchar(max) NULL", "ALTER TABLE Overtimes ADD COLUMN RequestPayloadJson LONGTEXT NULL"),
        new("Overtimes", "SubmittedDate", "ALTER TABLE Overtimes ADD COLUMN SubmittedDate TEXT NULL", "ALTER TABLE Overtimes ADD SubmittedDate date NULL", "ALTER TABLE Overtimes ADD COLUMN SubmittedDate DATE NULL"),
        new("Overtimes", "HttpStatusCode", "ALTER TABLE Overtimes ADD COLUMN HttpStatusCode INTEGER NULL", "ALTER TABLE Overtimes ADD HttpStatusCode int NULL", "ALTER TABLE Overtimes ADD COLUMN HttpStatusCode INT NULL"),
        new("ApiSubmissionLogs", "RetryCount",
            "ALTER TABLE ApiSubmissionLogs ADD COLUMN RetryCount INTEGER NOT NULL DEFAULT 0",
            "ALTER TABLE ApiSubmissionLogs ADD RetryCount int NOT NULL CONSTRAINT DF_ApiSubmissionLogs_RetryCount DEFAULT 0",
            "ALTER TABLE ApiSubmissionLogs ADD COLUMN RetryCount INT NOT NULL DEFAULT 0"),

        new("ApiSubmissionLogs", "LastRetryAt",
            "ALTER TABLE ApiSubmissionLogs ADD COLUMN LastRetryAt TEXT NULL",
            "ALTER TABLE ApiSubmissionLogs ADD LastRetryAt datetime2 NULL",
            "ALTER TABLE ApiSubmissionLogs ADD COLUMN LastRetryAt DATETIME(6) NULL"),

        new("ApiSubmissionLogs", "WorkCardId",
            "ALTER TABLE ApiSubmissionLogs ADD COLUMN WorkCardId INTEGER NULL",
            "ALTER TABLE ApiSubmissionLogs ADD WorkCardId int NULL",
            "ALTER TABLE ApiSubmissionLogs ADD COLUMN WorkCardId INT NULL"),

        new("ApiSubmissionLogs", "ScheduleDate",
            "ALTER TABLE ApiSubmissionLogs ADD COLUMN ScheduleDate TEXT NULL",
            "ALTER TABLE ApiSubmissionLogs ADD ScheduleDate date NULL",
            "ALTER TABLE ApiSubmissionLogs ADD COLUMN ScheduleDate DATE NULL"),
        new("LeaveTypeConfigurations", "PaymentCategory",
            "ALTER TABLE LeaveTypeConfigurations ADD COLUMN PaymentCategory TEXT NOT NULL DEFAULT ''",
            "ALTER TABLE LeaveTypeConfigurations ADD PaymentCategory nvarchar(20) NOT NULL CONSTRAINT DF_LeaveTypeConfigurations_PaymentCategory DEFAULT ''",
            "ALTER TABLE LeaveTypeConfigurations ADD COLUMN PaymentCategory VARCHAR(20) NOT NULL DEFAULT ''"),
    };

    private static string? _lastCheckedKey;

    /// <summary>Forget that the schema was already checked, so the next call checks again.</summary>
    public static void ResetCheck() => _lastCheckedKey = null;

    /// <summary>
    /// Upgrades an existing database to the current model without deleting data:
    /// first the hand-written patches below (renames etc.), then an automatic additive sync
    /// of every missing table/column found in the EF model (<see cref="ModelSchemaSync"/>).
    /// Returns a description of every change that was made (empty when already up to date).
    /// </summary>
    public static async Task<IReadOnlyList<string>> EnsureUpToDateAsync(DbConfig config)
    {
        // The connection state is re-evaluated regularly; only patch once per database.
        var key = $"{config.DatabaseProvider}|{config.SqlitePath}|{config.ConnectionString}";
        if (key == _lastCheckedKey) return Array.Empty<string>();

        var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();
        DbProviderFactory.Configure(optionsBuilder, config);
        await using var db = new AppDbContext(optionsBuilder.Options);

        var changes = new List<string>();

        // 1. Generic, additive sync FIRST: adds ANY table/column that the model has and the database
        //    lacks, so a new entity or property needs no hand-written patch. It cannot be blocked by
        //    a hand-written patch below failing.
        try
        {
            changes.AddRange(await ModelSchemaSync.EnsureAsync(db));
        }
        catch (Exception ex)
        {
            changes.Add($"{ModelSchemaSync.FailedPrefix}automatic schema sync: {ex.Message}");
        }

        // 2. Hand-written patches (kept for special cases). Each one is isolated.
        foreach (var table in TablePatches)
        {
            try
            {
                if (await TableExistsAsync(db, table.Table)) continue;

                var statements = config.DatabaseProvider switch
                {
                    DatabaseProvider.Sqlite    => table.Sqlite,
                    DatabaseProvider.SqlServer => table.SqlServer,
                    DatabaseProvider.MariaDb   => table.MariaDb,
                    _ => throw new NotSupportedException($"Unknown database provider: {config.DatabaseProvider}")
                };

                foreach (var ddl in statements)
                    await db.Database.ExecuteSqlRawAsync(ddl);
                changes.Add($"Created table {table.Table} (patch)");
            }
            catch (Exception ex)
            {
                changes.Add($"{ModelSchemaSync.FailedPrefix}table patch {table.Table}: {ex.Message}");
            }
        }

        foreach (var patch in Patches)
        {
            try
            {
                if (await ColumnExistsAsync(db, patch)) continue;

                var ddl = config.DatabaseProvider switch
                {
                    DatabaseProvider.Sqlite    => patch.Sqlite,
                    DatabaseProvider.SqlServer => patch.SqlServer,
                    DatabaseProvider.MariaDb   => patch.MariaDb,
                    _ => throw new NotSupportedException($"Unknown database provider: {config.DatabaseProvider}")
                };

                await db.Database.ExecuteSqlRawAsync(ddl);
                changes.Add($"Added column {patch.Table}.{patch.Column} (patch)");
            }
            catch (Exception ex)
            {
                changes.Add($"{ModelSchemaSync.FailedPrefix}column patch {patch.Table}.{patch.Column}: {ex.Message}");
            }
        }

        // Only a fully successful run is remembered; after a failure the next evaluation tries again.
        if (!changes.Any(c => c.StartsWith(ModelSchemaSync.FailedPrefix, StringComparison.Ordinal)))
            _lastCheckedKey = key;

        return changes;
    }

    private static async Task<bool> TableExistsAsync(AppDbContext db, string table)
    {
        try
        {
            await db.Database.ExecuteSqlRawAsync($"SELECT 1 FROM {table} WHERE 1 = 0");
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static async Task<bool> ColumnExistsAsync(AppDbContext db, ColumnPatch patch)
    {
        try
        {
            // Zero rows returned; fails only if the column (or table) is missing.
            await db.Database.ExecuteSqlRawAsync(
                $"SELECT {patch.Column} FROM {patch.Table} WHERE 1 = 0");
            return true;
        }
        catch
        {
            return false;
        }
    }
}
