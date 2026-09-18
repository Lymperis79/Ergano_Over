namespace ErganiManager.LocalCache.Entities;

/// <summary>
/// Cached copy of the time-rule settings needed by the Terminal to keep
/// enforcing early-clock-in blocking and early-departure detection even
/// when the main database is unreachable.
/// </summary>
public class CachedCompanySettings
{
    public int CompanyId { get; set; } // primary key, matches Company.Id in main DB

    public int EarlyClockInBlockMinutes { get; set; } = 15;
    public int EarlyDepartureAlertMinutes { get; set; } = 10;
    public bool BlockClockInWithoutSchedule { get; set; } = true;

    public DateTime LastSyncAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Minimal cached employee info needed for offline barcode lookup at the terminal.
/// </summary>
public class CachedEmployee
{
    public int Id { get; set; } // matches Employee.Id in main DB

    public int CompanyId { get; set; }
    public int BranchId { get; set; }

    public string BarcodeId { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;

    public DateTime LastSyncAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Cached schedule entries needed for offline early-clock-in / early-departure checks.
/// </summary>
public class CachedSchedule
{
    public int Id { get; set; }

    public int EmployeeId { get; set; }
    public DateOnly ScheduleDate { get; set; }

    public TimeOnly? StartTime { get; set; }
    public TimeOnly? EndTime { get; set; }

    public DateTime LastSyncAt { get; set; } = DateTime.UtcNow;
}
