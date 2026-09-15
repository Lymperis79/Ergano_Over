using System.ComponentModel.DataAnnotations;

namespace ErganiManager.Data.Entities;

public enum WorkType
{
    Office = 0,
    Home = 1,
    Rest = 2,
    Absent = 3
}

public enum ScheduleType
{
    Daily = 0,
    Weekly = 1
}

public class EmployeeSchedule
{
    public int Id { get; set; }

    public int EmployeeId { get; set; }
    public Employee? Employee { get; set; }

    public int BranchId { get; set; }
    public BusinessBranch? Branch { get; set; }

    public DateOnly ScheduleDate { get; set; }

    public WorkType WorkType { get; set; } = WorkType.Office;
    public ScheduleType ScheduleType { get; set; } = ScheduleType.Daily;

    public TimeOnly? StartTime { get; set; }
    public TimeOnly? EndTime { get; set; }

    public bool SubmittedToErgani { get; set; } = false;
    [MaxLength(100)]
    public string? SubmissionId { get; set; }
    [MaxLength(100)]
    public string? Protocol { get; set; }

    [MaxLength(500)]
    public string? Comments { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
}
