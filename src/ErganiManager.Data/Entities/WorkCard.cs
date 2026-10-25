using System.ComponentModel.DataAnnotations;

namespace ErganiManager.Data.Entities;

public enum MovementType
{
    Arrival = 0,
    Departure = 1
}

public class WorkCard
{
    public int Id { get; set; }

    public int EmployeeId { get; set; }
    public Employee? Employee { get; set; }

    public int BranchId { get; set; }
    public BusinessBranch? Branch { get; set; }

    public MovementType MovementType { get; set; }

    public DateTime MovementDateTime { get; set; }
    public DateOnly SubmissionDate { get; set; }

    [MaxLength(50)]
    public string? LateJustification { get; set; }

    public bool SubmittedToErgani { get; set; } = false;
    [MaxLength(100)]
    public string? SubmissionId { get; set; }
    [MaxLength(100)]
    public string? Protocol { get; set; }

    public string? ResponseRawJson { get; set; }

    // Early departure tracking
    public bool WasEarlyDeparture { get; set; } = false;
    public int? EarlyDepartureMinutes { get; set; }
    public bool EmailAlertSent { get; set; } = false;
    public DateTime? EmailAlertSentAt { get; set; }

    // Blocked early clock-in attempts (audit only, never submitted)
    public bool WasBlockedEarlyAttempt { get; set; } = false;
    public DateTime? BlockedAttemptTime { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
