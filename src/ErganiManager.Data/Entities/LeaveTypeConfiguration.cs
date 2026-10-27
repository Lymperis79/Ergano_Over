using System.ComponentModel.DataAnnotations;

namespace ErganiManager.Data.Entities;

/// <summary>
/// Centrally shared leave type configuration. This table is the source of truth for all PCs
/// connected to the same main database.
/// </summary>
public sealed class LeaveTypeConfiguration
{
    [Key, MaxLength(10)]
    public string Code { get; set; } = string.Empty;

    [Required, MaxLength(200)]
    public string Description { get; set; } = string.Empty;

    public bool IsHourly { get; set; }
    public bool IsPaid { get; set; } = true;

    [MaxLength(20)]
    public string PaymentCategory { get; set; } = string.Empty;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}
