using System.ComponentModel.DataAnnotations;

namespace ErganiManager.Data.Entities;

/// <summary>
/// One continuous working interval belonging to an employee's schedule day.
/// Existing schedules are migrated to a single period; split shifts use multiple rows.
/// </summary>
public class ScheduleWorkingPeriod
{
    public int Id { get; set; }

    public int ScheduleId { get; set; }
    public EmployeeSchedule? Schedule { get; set; }

    /// <summary>Stable display order within a schedule day, starting at 1.</summary>
    public int PeriodNumber { get; set; } = 1;

    public TimeOnly StartTime { get; set; }
    public TimeOnly EndTime { get; set; }
}
