using System.ComponentModel.DataAnnotations;

namespace ErganiManager.LocalCache.Entities;

/// <summary>
/// A clock-in/out scan that was captured while the main database (and/or the
/// Ergani API) was unreachable. Held here until a background sync job can
/// process it against the main database and submit it to Ergani.
/// </summary>
public class PendingSubmission
{
    public int Id { get; set; }

    public int EmployeeId { get; set; } // matches Employee.Id in main DB
    public int CompanyId { get; set; }
    public int BranchId { get; set; }

    [Required, MaxLength(50)]
    public string EmployeeBarcodeId { get; set; } = string.Empty;

    [Required, MaxLength(20)]
    public string MovementType { get; set; } = string.Empty; // "Arrival" or "Departure"

    public DateTime ScannedAt { get; set; } = DateTime.UtcNow;

    public bool Synced { get; set; } = false;
    public DateTime? SyncedAt { get; set; }

    public int SyncAttempts { get; set; } = 0;
    public string? LastSyncError { get; set; }
}
