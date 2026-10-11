using ErganiManager.Core.Models;

namespace ErganiManager.Core.Interfaces;

/// <summary>Outcome for one schedule day that was sent to Ergani.</summary>
public sealed class ScheduleSubmitDayResult
{
    public int EmployeeId { get; init; }
    public DateOnly Date { get; init; }
    public bool Success { get; init; }
    public string? Protocol { get; init; }
    public string? Error { get; init; }

    /// <summary>Set when the day is longer than 8 hours: the part over 8h that was sent as overtime.</summary>
    public TimeOnly? OvertimeFrom { get; set; }
    public TimeOnly? OvertimeTo { get; set; }
    public bool OvertimeSubmitted { get; set; }
    public string? OvertimeProtocol { get; set; }
    public string? OvertimeError { get; set; }
}

public sealed class ScheduleSubmitResult
{
    /// <summary>Schedule days accepted by Ergani.</summary>
    public int Submitted { get; set; }

    /// <summary>Schedule days that were rejected or not sent.</summary>
    public int Failed { get; set; }

    /// <summary>Overtime records (hours over 8) accepted by Ergani.</summary>
    public int OvertimeSubmitted { get; set; }

    /// <summary>Overtime records that could not be sent. They stay saved on the Overtime page.</summary>
    public int OvertimeFailed { get; set; }

    /// <summary>Set when the run could not even start (e.g. company not found).</summary>
    public string? Error { get; set; }

    public List<ScheduleSubmitDayResult> Days { get; } = new();
}

public interface IScheduleSubmitter
{
    /// <summary>Submits exactly the days passed in. Each date is one Ergani submission and
    /// is written to the API submission log (request, response, protocol, errors).</summary>
    Task<ScheduleSubmitResult> SubmitScheduleDaysAsync(
        int companyId, IReadOnlyList<ScheduleDayDto> days);

    /// <summary>Submits only the overtime (hours over 8) of the given days, without touching the
    /// normal schedule. The overtime comes from the schedule hours; days with no scheduled overtime
    /// fall back to the clocked-in hours. Unsent records from earlier attempts are retried and
    /// already-sent ones are not sent twice.</summary>
    Task<ScheduleSubmitResult> SubmitOvertimeFromScheduleAsync(
        int companyId, IReadOnlyList<ScheduleDayDto> days);

    Task<(int Submitted, string? Error)> SubmitOvertimeDaysAsync(
        int companyId, IReadOnlyList<ScheduleDayDto> overtimeDays);
}
