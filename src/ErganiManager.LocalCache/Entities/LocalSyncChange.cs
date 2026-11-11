using System.ComponentModel.DataAnnotations;

namespace ErganiManager.LocalCache.Entities;

/// <summary>
/// Durable outbox record for a local create/update/delete that must be applied
/// to the authoritative company database when connectivity returns.
/// Payload is a versioned JSON snapshot/command; it is never silently discarded.
/// </summary>
public sealed class LocalSyncChange
{
    public long Id { get; set; }

    /// <summary>Globally unique idempotency key generated on the originating device.</summary>
    [Required, MaxLength(64)]
    public string ChangeId { get; set; } = Guid.NewGuid().ToString("N");

    [Required, MaxLength(80)]
    public string EntityType { get; set; } = string.Empty;

    [Required, MaxLength(20)]
    public string Operation { get; set; } = string.Empty; // Create, Update, Delete

    /// <summary>Stable source identifier. Must not depend on a database-generated local integer.</summary>
    [Required, MaxLength(100)]
    public string EntityKey { get; set; } = string.Empty;

    public int CompanyId { get; set; }

    [Required, MaxLength(80)]
    public string DeviceId { get; set; } = string.Empty;

    /// <summary>Version of the serialized payload contract.</summary>
    public int PayloadVersion { get; set; } = 1;

    [Required]
    public string PayloadJson { get; set; } = "{}";

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? LastAttemptAtUtc { get; set; }
    public DateTime? LeaseUntilUtc { get; set; }

    [Required, MaxLength(20)]
    public string Status { get; set; } = "Pending"; // Pending, Processing, Applied, Conflict, Rejected

    public int AttemptCount { get; set; }
    public string? LastError { get; set; }

    /// <summary>Authoritative server version/row stamp observed when the change was created.</summary>
    [MaxLength(200)]
    public string? BaseServerVersion { get; set; }

    public DateTime? AppliedAtUtc { get; set; }
    public string? ServerResultJson { get; set; }
}

/// <summary>Per-company/per-device synchronization watermark and health state.</summary>
public sealed class LocalSyncCheckpoint
{
    [Required, MaxLength(160)]
    public string Key { get; set; } = string.Empty;

    public int CompanyId { get; set; }

    [Required, MaxLength(80)]
    public string DeviceId { get; set; } = string.Empty;

    public DateTime? LastPullAtUtc { get; set; }
    public DateTime? LastPushAtUtc { get; set; }
    public DateTime? LastSuccessfulSyncAtUtc { get; set; }
    public string? LastError { get; set; }
}
