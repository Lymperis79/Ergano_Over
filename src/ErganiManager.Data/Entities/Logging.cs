using System.ComponentModel.DataAnnotations;

namespace ErganiManager.Data.Entities;

public class ApiSubmissionLog
{
    public int Id { get; set; }

    public int CompanyId { get; set; }
    public int? EmployeeId { get; set; }

    [Required, MaxLength(50)]
    public string SubmissionType { get; set; } = string.Empty; // WorkCard, DailySchedule, Overtime, etc.

    public string? RequestPayloadJson { get; set; }
    public string? ResponseRawJson { get; set; }

    [MaxLength(100)]
    public string? SubmissionId { get; set; }
    [MaxLength(100)]
    public string? Protocol { get; set; }

    public DateTime SubmissionDate { get; set; } = DateTime.UtcNow;
    public int? HttpStatusCode { get; set; }
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public long DurationMs { get; set; }
}

public class AppLog
{
    public int Id { get; set; }

    [Required, MaxLength(20)]
    public string Level { get; set; } = string.Empty;

    [Required]
    public string Message { get; set; } = string.Empty;

    public string? ExceptionDetail { get; set; }

    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    public int? UserId { get; set; }
}
