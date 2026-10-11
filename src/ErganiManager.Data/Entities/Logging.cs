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

    /// <summary>
    /// How many manual retries have been made on THIS row (0 = original attempt only).
    /// Retries update the row in place instead of creating a new log line.
    /// </summary>
    public int RetryCount { get; set; }

    /// <summary>UTC time of the most recent manual retry.</summary>
    public DateTime? LastRetryAt { get; set; }

    /// <summary>The WorkCard this attempt belongs to, so retries update one row instead of adding new ones.</summary>
    public int? WorkCardId { get; set; }

    /// <summary>For DailySchedule submissions: the schedule day this attempt was for.</summary>
    public DateOnly? ScheduleDate { get; set; }
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
