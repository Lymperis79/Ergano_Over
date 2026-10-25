using System.ComponentModel.DataAnnotations;

namespace ErganiManager.Data.Entities;

/// <summary>
/// One employee/day of leave (holiday), submitted to Ergani through Documents/WTOLeave.
/// A multi-day holiday is stored as one row per day so each day can be retried or deleted.
/// </summary>
public class Leave
{
    public int Id { get; set; }

    public int EmployeeId { get; set; }
    public Employee? Employee { get; set; }

    public int BranchId { get; set; }
    public BusinessBranch? Branch { get; set; }

    public DateOnly LeaveDate { get; set; }

    /// <summary>Ergani leave code, e.g. ΑΔΚΑΝ (f_type).</summary>
    [MaxLength(10)]
    public string LeaveTypeCode { get; set; } = string.Empty;

    /// <summary>Only for hourly leave types (ΩΑ…): the hours of the day that are leave.</summary>
    public TimeOnly? StartTime { get; set; }
    public TimeOnly? EndTime { get; set; }

    /// <summary>f_year — the year the leave refers to (optional).</summary>
    public int? ReferenceYear { get; set; }

    /// <summary>f_req_days — number of leave days the employee is entitled to (optional).</summary>
    public int? EntitledDays { get; set; }

    [MaxLength(200)]
    public string? Comments { get; set; }

    public bool SubmittedToErgani { get; set; } = false;
    [MaxLength(100)]
    public string? SubmissionId { get; set; }
    [MaxLength(100)]
    public string? Protocol { get; set; }

    public string? ResponseRawJson { get; set; }
    public string? RequestPayloadJson { get; set; }
    public DateOnly? SubmittedDate { get; set; }
    public int? HttpStatusCode { get; set; }

    /// <summary>Why the last submission attempt failed (null when it succeeded or was never sent).</summary>
    [MaxLength(1000)]
    public string? LastError { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
