namespace ErganiManager.Core.Interfaces;

public class SyncResult
{
    public bool Success { get; set; }
    public int EmployeesSynced { get; set; }
    public int SchedulesSynced { get; set; }
    public int PendingSubmissionsFlushed { get; set; }
    public int PendingSubmissionsRemaining { get; set; }
    public string? ErrorMessage { get; set; }
}

public interface ICacheSyncService
{
    /// <summary>
    /// Pulls fresh reference data (employees, today/upcoming schedules, company
    /// time-rule settings) from the main database into the local cache, for the
    /// active company. Call this periodically while online so offline mode has
    /// recent data to work with.
    /// </summary>
    Task<SyncResult> RefreshCacheFromMainDatabaseAsync(int companyId);

    /// <summary>
    /// Attempts to push any unsynced PendingSubmissions from the local cache
    /// into the main database (and from there, onward to Ergani). Safe to call
    /// repeatedly; already-synced rows are skipped.
    /// </summary>
    Task<SyncResult> FlushPendingSubmissionsAsync();
}
