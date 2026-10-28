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
        ?? throw new InvalidOperationException(ErganiManager.Data.UiText.T("Database is not configured."));

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
                // Never delete or overwrite an existing backup. The user must choose a new path.
                if (File.Exists(destinationPath))
                    throw new IOException("A file already exists at the selected backup path. Choose a new filename so an existing backup is never destroyed.");
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "VACUUM INTO $path";
                var p = cmd.CreateParameter();
                p.ParameterName = "$path";
                p.Value = destinationPath;
                cmd.Parameters.Add(p);
                await cmd.ExecuteNonQueryAsync();

                try
                {
                    await VerifySqliteBackupAsync(destinationPath);
                }
                catch
                {
                    try { File.Delete(destinationPath); } catch { /* preserve verification failure */ }
                    throw;
                }

                return $"Backup verified successfully and saved to {destinationPath} ({new FileInfo(destinationPath).Length:N0} bytes).";
            }

            case DatabaseProvider.SqlServer:
            {
                // SQL Server interprets this path on the SERVER, not on this PC.
                // Do not use WITH INIT: that could overwrite a previously good backup.
                var dbName = conn.Database.Replace("]", "]]");
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandTimeout = 600;
                    cmd.CommandText = $"BACKUP DATABASE [{dbName}] TO DISK = @path";
                    var p = cmd.CreateParameter();
                    p.ParameterName = "@path";
                    p.Value = destinationPath;
                    cmd.Parameters.Add(p);
                    await cmd.ExecuteNonQueryAsync();
                }

                using (var verify = conn.CreateCommand())
                {
                    verify.CommandTimeout = 600;
                    verify.CommandText = "RESTORE VERIFYONLY FROM DISK = @path";
                    var p = verify.CreateParameter();
                    p.ParameterName = "@path";
                    p.Value = destinationPath;
                    verify.Parameters.Add(p);
                    await verify.ExecuteNonQueryAsync();
                }

                return $"SQL Server backup verification succeeded. Backup is on the SQL Server machine: {destinationPath}";
            }

            default:
                throw new NotSupportedException(
                    "Built-in backup supports SQLite and SQL Server. For MariaDB use mysqldump.");
        }
    }

    private static async Task VerifySqliteBackupAsync(string backupPath)
    {
        if (!File.Exists(backupPath) || new FileInfo(backupPath).Length == 0)
            throw new InvalidDataException("The backup file is missing or empty.");

        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = backupPath,
            Mode = SqliteOpenMode.ReadOnly
        };
        await using var backupConnection = new SqliteConnection(builder.ToString());
        await backupConnection.OpenAsync();

        await using var command = backupConnection.CreateCommand();
        command.CommandText = "PRAGMA integrity_check;";
        var result = Convert.ToString(await command.ExecuteScalarAsync());
        if (!string.Equals(result, "ok", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"SQLite backup integrity check failed: {result}");
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
