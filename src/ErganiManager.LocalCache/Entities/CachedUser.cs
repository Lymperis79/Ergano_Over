using System.ComponentModel.DataAnnotations;

namespace ErganiManager.LocalCache.Entities;

/// <summary>
/// A locally cached, encrypted copy of Admin/Operator credentials.
/// Refreshed automatically every time the app successfully connects
/// to the main database. Used to allow login (in degraded/offline mode)
/// when the main database is unreachable.
/// </summary>
public class CachedUser
{
    public int Id { get; set; }

    /// <summary>The Id of the AppUser in the main database (ErganiManager.Data).</summary>
    public int SourceUserId { get; set; }

    [Required, MaxLength(100)]
    public string Username { get; set; } = string.Empty;

    [Required]
    public string PasswordHash { get; set; } = string.Empty;

    [Required, MaxLength(20)]
    public string Role { get; set; } = string.Empty; // "Admin" or "Operator"

    public int? CompanyId { get; set; }

    [MaxLength(200)]
    public string? CompanyName { get; set; }

    public int? BranchId { get; set; }

    [MaxLength(200)]
    public string? BranchName { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTime LastSyncAt { get; set; } = DateTime.UtcNow;
}
