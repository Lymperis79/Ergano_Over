using System.ComponentModel.DataAnnotations;

namespace ErganiManager.Data.Entities;

/// <summary>Immutable audit entry for a schedule edit.</summary>
public class ScheduleChangeHistory
{
    public int Id { get; set; }
    public int EmployeeId { get; set; }
    public DateOnly ScheduleDate { get; set; }
    [MaxLength(30)] public string ChangeSource { get; set; } = "Manual";
    public string? PreviousSnapshotJson { get; set; }
    [MaxLength(30)] public string NewWorkType { get; set; } = string.Empty;
    public string NewPeriodsJson { get; set; } = "[]";
    public DateTime ChangedAtUtc { get; set; } = DateTime.UtcNow;
}
