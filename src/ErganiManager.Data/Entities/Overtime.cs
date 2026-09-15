using System.ComponentModel.DataAnnotations;

namespace ErganiManager.Data.Entities;

public enum OvertimeJustification
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

public class Overtime
{
    public int Id { get; set; }

    public int EmployeeId { get; set; }
    public Employee? Employee { get; set; }

    public int BranchId { get; set; }
    public BusinessBranch? Branch { get; set; }

    public DateOnly OvertimeDate { get; set; }
    public TimeOnly StartTime { get; set; }
    public TimeOnly EndTime { get; set; }

    public OvertimeJustification Justification { get; set; }
    public int WeeklyWorkdaysNumber { get; set; } = 5; // 5 or 6

    [MaxLength(100)]
    public string? AseeApproval { get; set; }

    public bool IsCancelled { get; set; } = false;

    public bool SubmittedToErgani { get; set; } = false;
    [MaxLength(100)]
    public string? SubmissionId { get; set; }
    [MaxLength(100)]
    public string? Protocol { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
