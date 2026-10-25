using System;
using System.ComponentModel.DataAnnotations;

namespace ErganiManager.LocalCache.Entities;

/// <summary>
/// A work card submission that reached Ergani but was rejected — either HTTP 5xx,
/// timeout, or a business-level error in a 200 response. Held here until the
/// background retry service confirms the API is back and resubmits with
/// EMPLOYER_SYSTEMS_UNAVAILABLE justification.
/// </summary>
public class FailedSubmission
{
    public int Id { get; set; }

    public int EmployeeId { get; set; }
    public int CompanyId { get; set; }
    public int BranchId { get; set; }

    [Required, MaxLength(20)]
    public string MovementType { get; set; } = string.Empty;

    public DateTime OriginalScannedAt { get; set; }

    [MaxLength(30)]
    public string FailureReason { get; set; } = string.Empty;

    public string? ErrorDescription { get; set; }

    public int RetryCount { get; set; } = 0;
    public DateTime? LastRetryAt { get; set; }
    public string? LastRetryError { get; set; }

    public bool Resolved { get; set; } = false;
    public DateTime? ResolvedAt { get; set; }

    [MaxLength(100)]
    public string? ResolvedProtocol { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
