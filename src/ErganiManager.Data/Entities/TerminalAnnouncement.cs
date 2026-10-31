using System.ComponentModel.DataAnnotations;

namespace ErganiManager.Data.Entities;

/// <summary>
/// Company-wide message displayed on employee clock-in/out terminals.
/// Announcements are retained for audit/history; inactive or expired messages are not displayed.
/// </summary>
public class TerminalAnnouncement
{
    public int Id { get; set; }
    public int CompanyId { get; set; }

    [Required, MaxLength(200)]
    public string Message { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? ExpiresAtUtc { get; set; }
}
