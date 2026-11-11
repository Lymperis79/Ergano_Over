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
    public string ErganiUsertype { get; set; } = "01";
    // Explicit company setting; never silently fall back to the trial API.
    public string ErganiBaseUrl { get; set; } = "https://eservices.yeka.gr/WebServicesApi/api";

    public bool IsActive { get; set; } = true;

    // Time rules
    /// <summary>Daily hours after which scanned working time is counted as overtime in reports.</summary>
    public double DailyOvertimeThresholdHours { get; set; } = 8.0;

    public int EarlyClockInBlockMinutes { get; set; } = 15;
    public int EarlyDepartureAlertMinutes { get; set; } = 10;

    /// <summary>Warn when an arrival and its following departure are closer than this threshold.</summary>
    public bool ShortClockInOutAlertEnabled { get; set; }
    public int ShortClockInOutAlertMinutes { get; set; } = 10;

    /// <summary>Warn when two consecutive arrivals for the same employee are closer than this threshold.</summary>
    public bool RepeatedClockInAlertEnabled { get; set; }
    public int RepeatedClockInAlertMinutes { get; set; } = 10;
    public bool BlockClockInWithoutSchedule { get; set; } = true;

    /// <summary>When true (default) clock-in/out is blocked on days the schedule marks as Rest or Absent.
    /// When false the scan is accepted on those days.</summary>
    public bool BlockClockInOnRestOrAbsentDay { get; set; } = true;

    /// <summary>JSON array of enabled official holiday dates (yyyy-MM-dd), configured per company.</summary>
    public string OfficialHolidayDatesJson { get; set; } = "[]";

    /// <summary>Bit mask of the events that send an alert mail (1 = early departure, the original behaviour).</summary>
    public int AlertEvents { get; set; } = 1;

    /// <summary>Clock-in earlier/later than the scheduled start by more than this many minutes sends an alert (0 = off).</summary>
    public int ClockInAlertMinutes { get; set; } = 10;

    // Email / SMTP settings
    public bool AlertEmailEnabled { get; set; } = false;

    /// <summary>When enabled, the same configured alert events are also stored for in-app notifications.</summary>
    public bool AlertNotificationEnabled { get; set; } = false;

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

    /// <summary>Optional company picture (PNG/JPEG bytes, already scaled down by the UI).
    /// Shown in the login window and the terminal window.</summary>
    public byte[]? Logo { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    public List<BusinessBranch> Branches { get; set; } = new();
    public List<TerminalAnnouncement> TerminalAnnouncements { get; set; } = new();
    public List<AppUser> Users { get; set; } = new();
    public List<Employee> Employees { get; set; } = new();
}
