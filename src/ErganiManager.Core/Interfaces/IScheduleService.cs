namespace ErganiManager.Core.Interfaces;

public enum AppWorkType
{
    Office,
    Home,
    Rest,
    Absent
}

public enum ScheduleSource
{
    Manual,
    ErganiImported
}

public sealed class ScheduleWorkingPeriodDto
{
    public int PeriodNumber { get; set; } = 1;
    public TimeOnly StartTime { get; set; }
    public TimeOnly EndTime { get; set; }
    public double DurationHours => EndTime > StartTime ? (EndTime - StartTime).TotalHours : 0;
}

public class ScheduleDayDto
{
    public int Id { get; set; }
    public int EmployeeId { get; set; }
    public int BranchId { get; set; }
    public DateOnly ScheduleDate { get; set; }
    public AppWorkType WorkType { get; set; } = AppWorkType.Office;
    public ScheduleSource Source { get; set; } = ScheduleSource.Manual;
    public TimeOnly? StartTime { get; set; }
    public TimeOnly? EndTime { get; set; }
    /// <summary>Continuous work periods for this day. Empty on old callers means use StartTime/EndTime.</summary>
    public List<ScheduleWorkingPeriodDto> WorkingPeriods { get; set; } = new();
    public bool SubmittedToErgani { get; set; }
    public bool HasPendingChange { get; set; }
    public AppWorkType? PreviousWorkType { get; set; }
    public List<ScheduleWorkingPeriodDto> PreviousWorkingPeriods { get; set; } = new();
    public string? Protocol { get; set; }
    public string? Comments { get; set; }

    // Read-only, populated from actual WorkCards for that day if any exist —
    // lets the day-edit dialog show "what actually happened" next to the plan.
    public DateTime? ActualArrival { get; set; }
    public DateTime? ActualDeparture { get; set; }
}

/// <summary>One Ergani schedule submission attempt, read back from the API log.</summary>
public class ScheduleSubmissionLogDto
{
    public int Id { get; set; }
    public DateOnly? ScheduleDate { get; set; }
    /// <summary>UTC time of the attempt.</summary>
    public DateTime SubmissionDate { get; set; }
    public bool Success { get; set; }
    public string? Protocol { get; set; }
    public string? ErrorMessage { get; set; }
    public int? HttpStatusCode { get; set; }
    public string? ResponseRawJson { get; set; }
}

/// <summary>How much of a destination employee's existing schedule a clone would replace.</summary>
public class CloneImpactDto
{
    public int EmployeeId { get; set; }
    public int ExistingEntries { get; set; }
    /// <summary>Of the existing entries, how many were already submitted to Ergani (or imported from it).</summary>
    public int SubmittedEntries { get; set; }
    /// <summary>Overtime records (hours over 8) on these dates that were not sent yet; a clone removes them.</summary>
    public int UnsentOvertimeEntries { get; set; }
    /// <summary>Overtime records on these dates already sent to Ergani; a clone leaves them untouched.</summary>
    public int SubmittedOvertimeEntries { get; set; }
    /// <summary>Holiday days on these dates not sent yet; a clone that includes holidays replaces them.</summary>
    public int UnsentLeaveEntries { get; set; }
    /// <summary>Holiday days on these dates already sent to Ergani; a clone never touches them.</summary>
    public int SubmittedLeaveEntries { get; set; }
}

/// <summary>One entry of the clone history.</summary>
public class CloneBatchInfoDto
{
    public int Id { get; set; }
    public int SourceEmployeeId { get; set; }
    public string SourceEmployeeName { get; set; } = string.Empty;
    public string? PerformedBy { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateOnly FirstDate { get; set; }
    public DateOnly LastDate { get; set; }
    public int DayCount { get; set; }
    public bool IsWholePeriod { get; set; }
    public int TargetCount { get; set; }
    public List<string> TargetNames { get; set; } = new();
    /// <summary>Entries the clone created.</summary>
    public int EntriesWritten { get; set; }
    /// <summary>Entries of the destination employees that the clone replaced (and Undo restores).</summary>
    public int ReplacedEntries { get; set; }
    /// <summary>Of the replaced entries, how many had already been sent to Ergani.</summary>
    public int ReplacedSubmittedEntries { get; set; }
    /// <summary>Unsent overtime records that the clone removed (and Undo restores).</summary>
    public int ReplacedOvertimeEntries { get; set; }
    public bool IncludesHolidays { get; set; }
    /// <summary>Holiday days the clone created.</summary>
    public int CopiedLeaveEntries { get; set; }
    /// <summary>Unsent holiday days that the clone replaced (and Undo restores).</summary>
    public int ReplacedLeaveEntries { get; set; }
    public DateTime? UndoneAtUtc { get; set; }
    public string? UndoneBy { get; set; }
    public bool IsUndone => UndoneAtUtc != null;

    /// <summary>Only filled by <see cref="IScheduleService.GetLastCloneAsync"/>: how many entries on the
    /// cloned dates of the destination employees have been submitted to Ergani since the clone.
    /// When this is above zero the clone can no longer be undone.</summary>
    public int SubmittedEntries { get; set; }
    public bool CanUndo => !IsUndone && SubmittedEntries == 0;
}

public enum CloneUndoStatus
{
    /// <summary>The clone was undone and the previous schedules were restored.</summary>
    Done,
    /// <summary>Some of the cloned entries were already submitted to Ergani; nothing was changed.</summary>
    BlockedSubmitted,
    /// <summary>A newer clone exists; only the most recent clone can be undone. Nothing was changed.</summary>
    NewerCloneExists,
    /// <summary>The clone does not exist or was already undone. Nothing was changed.</summary>
    NothingToUndo
}

public class CloneUndoResult
{
    public CloneUndoStatus Status { get; set; }
    public int RemovedEntries { get; set; }
    public int RestoredEntries { get; set; }
    public int RestoredOvertimeEntries { get; set; }
    public int RestoredLeaveEntries { get; set; }
    /// <summary>For <see cref="CloneUndoStatus.BlockedSubmitted"/>: how many entries are already submitted.</summary>
    public int SubmittedEntries { get; set; }
}

public interface IScheduleService
{
    /// <summary>Returns one entry per day in the given month that has a
    /// schedule on file (days with none are simply absent from the result —
    /// the calendar UI renders those as blank).</summary>
    Task<List<ScheduleDayDto>> GetMonthAsync(int employeeId, int year, int month);

    Task<ScheduleDayDto?> GetByDateAsync(int employeeId, DateOnly date);

    /// <summary>Creates or updates the schedule for a single employee/date —
    /// the day-edit dialog always calls this one method regardless of whether
    /// a schedule already existed for that day.</summary>
    Task<int> UpsertDayAsync(ScheduleDayDto dto);

    Task DeleteDayAsync(int scheduleId);

    /// <summary>Deletes the schedule of one or more days for an employee (local records only —
    /// nothing is withdrawn from Ergani). Returns how many schedule rows were removed.</summary>
    Task<int> DeleteDaysAsync(int employeeId, IReadOnlyCollection<DateOnly> dates);

    /// <summary>Ergani schedule submission attempts (successful and failed) for an employee,
    /// newest first.</summary>
    Task<List<ScheduleSubmissionLogDto>> GetSubmissionLogAsync(int companyId, int employeeId, int take = 200);

    /// <summary>Applies the same work type/time range to every day in a date
    /// range that matches the given days-of-week filter — used for "set this
    /// whole week" or "set this whole month" bulk entry.</summary>
    Task<int> BulkSetAsync(int employeeId, int branchId, DateOnly fromDate, DateOnly toDate,
        AppWorkType workType, TimeOnly? startTime, TimeOnly? endTime, IReadOnlySet<DayOfWeek>? onlyDaysOfWeek = null);

    /// <summary>Copies the complete schedule for the given date range from one employee
    /// to a list of target employees. Existing entries for the target employees on those
    /// dates are replaced (dates with no schedule on the source are left empty).
    /// What was replaced is saved in the clone history so the clone can be undone.
    /// Returns total rows written (0 = the source has no schedule in the range; nothing changed).</summary>
    Task<int> CopyToEmployeesAsync(int sourceEmployeeId, DateOnly fromDate, DateOnly toDate,
        IReadOnlyList<int> targetEmployeeIds, string? performedBy = null, bool includeHolidays = false);

    /// <summary>Copies only the specified dates; entries on all other target dates are preserved.
    /// Saved in the clone history like the range overload.</summary>
    /// <remarks>With <paramref name="includeHolidays"/> the source's holiday days on these dates are copied too
    /// (the destination's unsent holidays on those dates are replaced; sent ones are kept and their dates skipped).
    /// The returned count includes the copied holiday days.</remarks>
    Task<int> CopyToEmployeesAsync(int sourceEmployeeId, IReadOnlyList<DateOnly> dates,
        IReadOnlyList<int> targetEmployeeIds, string? performedBy = null, bool includeHolidays = false);

    /// <summary>Before cloning: per target employee, how many existing entries on these dates
    /// would be replaced and how many of them were already sent to Ergani.</summary>
    Task<List<CloneImpactDto>> GetCloneImpactAsync(IReadOnlyList<int> targetEmployeeIds,
        IReadOnlyCollection<DateOnly> dates);

    /// <summary>The most recent clone of the company that has not been undone (null if none),
    /// with <see cref="CloneBatchInfoDto.SubmittedEntries"/> filled in.</summary>
    Task<CloneBatchInfoDto?> GetLastCloneAsync(int companyId);

    /// <summary>The recent clone history of the company, newest first.</summary>
    Task<List<CloneBatchInfoDto>> GetCloneHistoryAsync(int companyId, int take = 10);

    /// <summary>Undoes a clone: removes the cloned entries and puts back the destination
    /// employees' previous schedules, including their Ergani status, protocol and import source.
    /// Refused (nothing changed) when any entry on the cloned dates was submitted to Ergani since,
    /// because a local undo cannot withdraw a submission from Ergani.</summary>
    Task<CloneUndoResult> UndoCloneAsync(int batchId, string? performedBy = null);
}
