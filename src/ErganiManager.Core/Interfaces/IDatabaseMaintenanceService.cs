namespace ErganiManager.Core.Interfaces;

public sealed record LocalCacheStatus(int PendingScans, int FailedSubmissions);

/// <summary>Administrator tools: database backup/delete and local cache reset.</summary>
public interface IDatabaseMaintenanceService
{
    /// <summary>Human-readable description of the configured database (provider + location).</summary>
    string DescribeDatabase();

    /// <summary>Suggested backup file name, e.g. erganimanager-20261007-1530.db</summary>
    string SuggestBackupFileName();

    /// <summary>Backs up the main database to <paramref name="destinationPath"/>. Returns a message.</summary>
    Task<string> BackupAsync(string destinationPath);

    /// <summary>Permanently deletes the main database (the schema is recreated by the setup wizard).</summary>
    Task DeleteDatabaseAsync();

    LocalCacheStatus GetLocalCacheStatus();

    /// <summary>Deletes the temporary local cache (a .bak copy is kept). Returns the backup path.</summary>
    string? DeleteLocalCache();
}
