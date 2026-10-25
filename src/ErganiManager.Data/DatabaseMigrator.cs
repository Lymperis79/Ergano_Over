using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace ErganiManager.Data;

/// <summary>Result of <see cref="DatabaseMigrator.ApplyAsync"/>.</summary>
public sealed record MigrationOutcome(bool UsedMigrations, IReadOnlyList<string> Applied)
{
    /// <summary>True when the schema changed during this call (new database or migrations applied).</summary>
    public bool SchemaChanged => Applied.Count > 0;
}

/// <summary>
/// Creates or upgrades the main database.
///
///  * If the project contains EF Core migrations (dotnet ef migrations add ...) they are applied.
///    Databases that were created earlier with EnsureCreated are first brought up to date with
///    <see cref="DbSchemaUpdater"/> and then "baselined": the first migration is recorded as
///    already applied so it does not try to create tables that exist.
///  * If there are no migrations yet it falls back to the previous behaviour (EnsureCreated).
/// </summary>
public static class DatabaseMigrator
{
    public static async Task<MigrationOutcome> ApplyAsync(DbConfig config)
    {
        var builder = new DbContextOptionsBuilder<AppDbContext>();
        DbProviderFactory.Configure(builder, config);
        await using var db = new AppDbContext(builder.Options);

        var all = db.Database.GetMigrations().ToList();
        if (all.Count == 0)
        {
            await db.Database.EnsureCreatedAsync();
            return new MigrationOutcome(false, Array.Empty<string>());
        }

        var applied = (await db.Database.GetAppliedMigrationsAsync()).ToList();
        if (applied.Count == 0 && await LegacySchemaExistsAsync(db))
        {
            // Database made by EnsureCreated: patch missing columns, then baseline.
            await DbSchemaUpdater.EnsureUpToDateAsync(config);
            await BaselineAsync(db, all[0]);
            applied = (await db.Database.GetAppliedMigrationsAsync()).ToList();
        }

        var pending = (await db.Database.GetPendingMigrationsAsync()).ToList();
        if (pending.Count > 0)
            await db.Database.MigrateAsync();

        return new MigrationOutcome(true, pending);
    }

    private static async Task<bool> LegacySchemaExistsAsync(AppDbContext db)
    {
        try { _ = await db.Companies.AnyAsync(); return true; }
        catch { return false; }
    }

    private static async Task BaselineAsync(AppDbContext db, string migrationId)
    {
        var history = db.GetService<IHistoryRepository>();
        await db.Database.ExecuteSqlRawAsync(history.GetCreateIfNotExistsScript());
        await db.Database.ExecuteSqlRawAsync(history.GetInsertScript(new HistoryRow(migrationId, "10.0.0")));
    }
}
