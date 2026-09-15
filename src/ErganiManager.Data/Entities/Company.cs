using System.ComponentModel.DataAnnotations;

namespace ErganiManager.Data.Entities;

public class Company
{
    public int Id { get; set; }

    [Required, MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    [Required, MaxLength(9)]
    public string TaxId { get; set; } = string.Empty; // AFM

    // Ergani API credentials
    [Required, MaxLength(100)]
    public string ErganiUsername { get; set; } = string.Empty;

    [Required]
    public string ErganiPasswordEncrypted { get; set; } = string.Empty;

    [MaxLength(300)]
    // Default mirrors ErganiEndpoints.TrialBaseUrl in ErganiManager.ErganiApi.
    // Kept as a plain string here (not a cross-project constant reference)
    // because ErganiManager.Data must not depend on ErganiManager.ErganiApi.
    public string ErganiBaseUrl { get; set; } = "https://trialeservices.yeka.gr/WebServicesAPI/api";

    public bool IsActive { get; set; } = true;

    // Time rules
    public int EarlyClockInBlockMinutes { get; set; } = 15;
    public int EarlyDepartureAlertMinutes { get; set; } = 10;
    public bool BlockClockInWithoutSchedule { get; set; } = true;

    // Email / SMTP settings
    public bool AlertEmailEnabled { get; set; } = false;

    /// <summary>When true the background retry service automatically resubmits
    /// failed clock-in/out records with EMPLOYER_SYSTEMS_UNAVAILABLE justification
    /// once the Ergani API comes back online. Set to false to review manually.</summary>
    public bool AutoRetryFailedSubmissions { get; set; } = true;
    [MaxLength(500)]
    public string? AlertEmailRecipients { get; set; } // comma-separated

    [MaxLength(200)]
    public string? SmtpHost { get; set; }
    public int? SmtpPort { get; set; }
    [MaxLength(200)]
    public string? SmtpUser { get; set; }
    public string? SmtpPasswordEncrypted { get; set; }
    public bool SmtpUseTls { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    public List<BusinessBranch> Branches { get; set; } = new();
    public List<AppUser> Users { get; set; } = new();
    public List<Employee> Employees { get; set; } = new();
}
