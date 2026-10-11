using ErganiManager.Core.Interfaces;
using ErganiManager.Data;
using ErganiManager.LocalCache;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace ErganiManager.Core.Services;

public class DatabaseMaintenanceService : IDatabaseMaintenanceService
{
    private readonly IConnectionStateService _connection;

    public DatabaseMaintenanceService(IConnectionStateService connection) => _connection = connection;

    private DbConfig Config => _connection.LoadConfig()
        ?? throw new InvalidOperationException("Database is not configured.");

    public string DescribeDatabase()
    {
        var c = _connection.LoadConfig();
        if (c == null) return "Not configured";
        return c.DatabaseProvider == DatabaseProvider.Sqlite
            ? $"SQLite — {c.SqlitePath}"
            : c.DatabaseProvider.ToString();
    }

    public string SuggestBackupFileName()
    {
        var ext = Config.DatabaseProvider switch
        {
            DatabaseProvider.Sqlite    => "db",
            DatabaseProvider.SqlServer => "bak",
            _                          => "sql"
        };
        return $"erganimanager-{DateTime.Now:yyyyMMdd-HHmm}.{ext}";
    }

    public async Task<string> BackupAsync(string destinationPath)
    {
        var config = Config;
        await using var db = new AppDbContext(_connection.GetDbOptions());
        var conn = db.Database.GetDbConnection();
        await conn.OpenAsync();

        switch (config.DatabaseProvider)
        {
            case DatabaseProvider.Sqlite:
            {
                // VACUUM INTO writes a consistent copy even while the database is in use,
                // but refuses to overwrite an existing file.
                if (File.Exists(destinationPath)) File.Delete(destinationPath);
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "VACUUM INTO $path";
                var p = cmd.CreateParameter();
                p.ParameterName = "$path";
                p.Value = destinationPath;
                cmd.Parameters.Add(p);
                await cmd.ExecuteNonQueryAsync();
                return $"Backup saved to {destinationPath}";
            }

            case DatabaseProvider.SqlServer:
            {
                // Runs on the SQL Server machine, so the path is on the SERVER, not on this PC.
                var dbName = conn.Database.Replace("]", "]]");
                using var cmd = conn.CreateCommand();
                cmd.CommandTimeout = 600;
                cmd.CommandText = $"BACKUP DATABASE [{dbName}] TO DISK = @path WITH INIT";
                var p = cmd.CreateParameter();
                p.ParameterName = "@path";
                p.Value = destinationPath;
                cmd.Parameters.Add(p);
                await cmd.ExecuteNonQueryAsync();
                return $"Backup written on the SQL Server machine: {destinationPath}";
            }

            default:
                throw new NotSupportedException(
                    "Built-in backup supports SQLite and SQL Server. For MariaDB use mysqldump.");
        }
    }

    public async Task DeleteDatabaseAsync()
    {
        var config = Config;
        await using (var db = new AppDbContext(_connection.GetDbOptions()))
        {
            await db.Database.EnsureDeletedAsync();
        }

        if (config.DatabaseProvider == DatabaseProvider.Sqlite)
        {
            SqliteConnection.ClearAllPools();
            if (!string.IsNullOrWhiteSpace(config.SqlitePath))
                foreach (var f in new[] { config.SqlitePath, config.SqlitePath + "-wal", config.SqlitePath + "-shm" })
                    try { if (File.Exists(f)) File.Delete(f); } catch { /* best effort */ }
        }

        // The cache was built from the deleted database
        LocalCacheDbContextFactory.DeleteLocalCache(backup: true);
    }

    public LocalCacheStatus GetLocalCacheStatus()
    {
        var (pending, failed) = LocalCacheDbContextFactory.GetUnsyncedCounts();
        return new LocalCacheStatus(pending, failed);
    }

    public string? DeleteLocalCache() => LocalCacheDbContextFactory.DeleteLocalCache(backup: true);
}
