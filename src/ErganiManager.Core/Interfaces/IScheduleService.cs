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
    public bool SubmittedToErgani { get; set; }
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
    /// dates are replaced. Returns total rows written.</summary>
    Task<int> CopyToEmployeesAsync(int sourceEmployeeId, DateOnly fromDate, DateOnly toDate,
        IReadOnlyList<int> targetEmployeeIds);
}
