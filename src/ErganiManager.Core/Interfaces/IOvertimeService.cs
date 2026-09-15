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
    public DateTime CreatedAt { get; set; }

    public string JustificationLabel => Justification switch
    {
        AppOvertimeJustification.AccidentPreventionOrDamageRestoration => "Accident prevention / damage restoration",
        AppOvertimeJustification.UrgentSeasonalTasks                   => "Urgent seasonal tasks",
        AppOvertimeJustification.ExceptionalWorkload                   => "Exceptional workload",
        AppOvertimeJustification.SupplementaryTasks                    => "Supplementary tasks",
        AppOvertimeJustification.LostHoursSuddenCauses                 => "Lost hours — sudden causes",
        AppOvertimeJustification.LostHoursOfficialHolidays             => "Lost hours — official holidays",
        AppOvertimeJustification.LostHoursWeatherConditions            => "Lost hours — weather conditions",
        AppOvertimeJustification.EmergencyClosureDay                   => "Emergency closure day",
        AppOvertimeJustification.NonWorkdayTasks                       => "Non-workday tasks",
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
