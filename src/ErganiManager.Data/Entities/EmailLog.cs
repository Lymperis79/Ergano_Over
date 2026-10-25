using System.ComponentModel.DataAnnotations;

namespace ErganiManager.Data.Entities;

/// <summary>
/// One alert mail the application tried to send: what it said, to whom, and whether it arrived.
/// </summary>
public class EmailLog
{
    public int Id { get; set; }

    public int CompanyId { get; set; }

    /// <summary>The alert event (bit value of AlertEvent: early departure, Ergani down, ...).</summary>
    public int EventCode { get; set; }

    [MaxLength(300)]
    public string Title { get; set; } = string.Empty;

    [MaxLength(300)]
    public string Subject { get; set; } = string.Empty;

    /// <summary>The recipients at the time of sending (comma-separated).</summary>
    [MaxLength(500)]
    public string? Recipients { get; set; }

    /// <summary>The full text of the mail.</summary>
    public string Body { get; set; } = string.Empty;

    /// <summary>Sent, Queued (will be sent when the connection is back) or Failed.</summary>
    [MaxLength(20)]
    public string Status { get; set; } = "Sent";

    [MaxLength(1000)]
    public string? ErrorMessage { get; set; }

    public int Attempts { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? SentAt { get; set; }
}
