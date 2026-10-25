namespace ErganiManager.Core.Interfaces;

public enum AppOvertimeJustification
{
    AccidentPreventionOrDamageRestoration,
    UrgentSeasonalTasks,
    ExceptionalWorkload,
    SupplementaryTasks,
    LostHoursSuddenCauses,
    LostHoursOfficialHolidays,
    LostHoursWeatherConditions,
    EmergencyClosureDay,
    NonWorkdayTasks
}

public class OvertimeDto
{
    public int Id { get; set; }
    public int EmployeeId { get; set; }
    public string EmployeeFullName { get; set; } = string.Empty;
    public int BranchId { get; set; }
    public string BranchName { get; set; } = string.Empty;
    public DateOnly OvertimeDate { get; set; }
    public TimeOnly StartTime { get; set; }
    public TimeOnly EndTime { get; set; }
    public AppOvertimeJustification Justification { get; set; }
    public int WeeklyWorkdaysNumber { get; set; } = 5;
    public string? AseeApproval { get; set; }
    public bool IsCancelled { get; set; }
    public bool SubmittedToErgani { get; set; }
    public string? Protocol { get; set; }
    public string? SubmissionId { get; set; }
    public string? ResponseRawJson { get; set; }
    public DateOnly? SubmittedDate { get; set; }
    public DateTime CreatedAt { get; set; }

    public string JustificationLabel => Justification switch
    {
        AppOvertimeJustification.AccidentPreventionOrDamageRestoration => ErganiManager.Data.UiText.T("Accident prevention / damage restoration"),
        AppOvertimeJustification.UrgentSeasonalTasks                   => ErganiManager.Data.UiText.T("Urgent seasonal tasks"),
        AppOvertimeJustification.ExceptionalWorkload                   => ErganiManager.Data.UiText.T("Exceptional workload"),
        AppOvertimeJustification.SupplementaryTasks                    => ErganiManager.Data.UiText.T("Supplementary tasks"),
        AppOvertimeJustification.LostHoursSuddenCauses                 => ErganiManager.Data.UiText.T("Lost hours — sudden causes"),
        AppOvertimeJustification.LostHoursOfficialHolidays             => ErganiManager.Data.UiText.T("Lost hours — official holidays"),
        AppOvertimeJustification.LostHoursWeatherConditions            => ErganiManager.Data.UiText.T("Lost hours — weather conditions"),
        AppOvertimeJustification.EmergencyClosureDay                   => ErganiManager.Data.UiText.T("Emergency closure day"),
        AppOvertimeJustification.NonWorkdayTasks                       => ErganiManager.Data.UiText.T("Non-workday tasks"),
        _                                                              => Justification.ToString()
    };

    public double HoursWorked =>
        (EndTime.ToTimeSpan() - StartTime.ToTimeSpan()).TotalHours;
}

public interface IOvertimeService
{
    Task<List<OvertimeDto>> GetByCompanyAsync(int companyId, DateOnly? fromDate = null, DateOnly? toDate = null);
    Task<int> CreateAsync(OvertimeDto dto, int companyId);
    Task CancelAsync(int id);
    Task DeleteAsync(int id);
}
