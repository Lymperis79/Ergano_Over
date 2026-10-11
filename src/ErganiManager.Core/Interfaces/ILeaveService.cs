namespace ErganiManager.Core.Interfaces;

public class LeaveDto
{
    public int Id { get; set; }
    public int EmployeeId { get; set; }
    public int BranchId { get; set; }
    public DateOnly LeaveDate { get; set; }
    public string LeaveTypeCode { get; set; } = string.Empty;
    public TimeOnly? StartTime { get; set; }
    public TimeOnly? EndTime { get; set; }
    public int? ReferenceYear { get; set; }
    public int? EntitledDays { get; set; }
    public string? Comments { get; set; }

    public bool SubmittedToErgani { get; set; }
    public string? Protocol { get; set; }
    public string? SubmissionId { get; set; }
    public string? ResponseRawJson { get; set; }
    public DateOnly? SubmittedDate { get; set; }
    public string? LastError { get; set; }
    public DateTime CreatedAt { get; set; }
}

/// <summary>What <see cref="ILeaveService.CreateRangeAsync"/> did.</summary>
public sealed class LeaveCreateResult
{
    public int Created { get; set; }
    /// <summary>Days skipped because the same leave already exists for that employee/day.</summary>
    public int Duplicates { get; set; }
    public List<int> CreatedIds { get; } = new();
}

public interface ILeaveService
{
    /// <summary>Leave records of one employee, newest first.</summary>
    Task<List<LeaveDto>> GetByEmployeeAsync(int companyId, int employeeId, int take = 300);

    /// <summary>Gets the employee holidays in an inclusive date range.</summary>
    Task<List<LeaveDto>> GetByEmployeeDateRangeAsync(int companyId, int employeeId, DateOnly from, DateOnly to);

    /// <summary>Creates one record per day from <paramref name="from"/> to <paramref name="to"/>
    /// (weekends optionally skipped). Days that already have the same leave are skipped.</summary>
    Task<LeaveCreateResult> CreateRangeAsync(LeaveDto template, DateOnly from, DateOnly to, bool skipWeekends);

    /// <summary>Deletes a leave record that has not been sent to Ergani yet.
    /// Returns false when it was already submitted (those stay as the record of what was sent).</summary>
    Task<bool> DeleteUnsentAsync(int id);

    /// <summary>Deletes a holiday record whether or not it was submitted. Used only when the user
    /// explicitly chose to replace existing data during an Ergani import.</summary>
    Task DeleteAsync(int id);
}

public interface ILeaveSubmitter
{
    /// <summary>Submits the given unsent leave records to Ergani (Documents/WTOLeave). Records of the
    /// same employee, type, hours and comment go in one submission, which is written to the API log.</summary>
    Task<LeaveBatchSubmissionResult> SubmitAsync(int companyId, IReadOnlyList<int> leaveIds, CancellationToken ct = default);
}

public sealed class LeaveBatchSubmissionResult
{
    /// <summary>Leave days accepted by Ergani.</summary>
    public int SubmittedCount { get; set; }
    /// <summary>Leave days Ergani rejected or that could not be sent.</summary>
    public int FailedCount { get; set; }
    /// <summary>First failure message (set when <see cref="FailedCount"/> &gt; 0 or nothing could be sent).</summary>
    public string? ErrorMessage { get; set; }
}
