namespace ErganiManager.Data.Entities;

/// <summary>A company-scoped in-app notification generated from the same alert event as email.</summary>
public sealed class NotificationLog
{
    public long Id { get; set; }
    public int CompanyId { get; set; }
    public int EventCode { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public bool IsRead { get; set; }
}
