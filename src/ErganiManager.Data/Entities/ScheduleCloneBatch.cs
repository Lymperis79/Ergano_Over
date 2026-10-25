using System.ComponentModel.DataAnnotations;

namespace ErganiManager.Data.Entities;

/// <summary>
/// One "clone schedule to other employees" operation (the clone history).
/// It keeps a snapshot of the destination employees' schedule rows that the clone replaced
/// (including their Ergani status, protocol and submission id), so the clone can be undone
/// as long as the copied entries have not been submitted to Ergani.
/// </summary>
public class ScheduleCloneBatch
{
    public int Id { get; set; }

    public int CompanyId { get; set; }

    public int SourceEmployeeId { get; set; }

    [MaxLength(250)]
    public string SourceEmployeeName { get; set; } = string.Empty;

    /// <summary>Username of the person who made the clone.</summary>
    [MaxLength(100)]
    public string? PerformedBy { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // ── Scope of the clone ────────────────────────────────────────────────
    public DateOnly FirstDate { get; set; }
    public DateOnly LastDate { get; set; }
    public int DayCount { get; set; }

    /// <summary>True when no days were selected and the whole calendar period was copied.</summary>
    public bool IsWholePeriod { get; set; }

    /// <summary>JSON array of every date the clone covered ("2026-10-01", ...).</summary>
    public string DatesJson { get; set; } = "[]";

    /// <summary>JSON array of the destination employee ids.</summary>
    public string TargetEmployeeIdsJson { get; set; } = "[]";

    /// <summary>Destination employee names, separated by "; " (for display only).</summary>
    public string TargetNames { get; set; } = string.Empty;

    // ── Numbers shown in the history ──────────────────────────────────────
    public int EntriesWritten { get; set; }
    public int ReplacedEntries { get; set; }
    public int ReplacedSubmittedEntries { get; set; }

    /// <summary>Unsent overtime records (hours over 8) of the destination employees that the clone removed.</summary>
    public int ReplacedOvertimeEntries { get; set; }

    /// <summary>JSON snapshot of those unsent overtime records. Restored by Undo.</summary>
    public string OvertimeSnapshotJson { get; set; } = "[]";

    /// <summary>True when the holidays (leave days) of the source were copied too.</summary>
    public bool IncludesHolidays { get; set; }

    /// <summary>Holiday days created for the destination employees.</summary>
    public int CopiedLeaveEntries { get; set; }

    /// <summary>Unsent holiday days of the destination employees that the clone replaced.</summary>
    public int ReplacedLeaveEntries { get; set; }

    /// <summary>JSON snapshot of those unsent holiday days. Restored by Undo.</summary>
    public string LeaveSnapshotJson { get; set; } = "[]";

    /// <summary>JSON snapshot of the destination rows that the clone replaced. Restored by Undo.</summary>
    public string SnapshotJson { get; set; } = "[]";

    public DateTime? UndoneAt { get; set; }

    [MaxLength(100)]
    public string? UndoneBy { get; set; }
}
