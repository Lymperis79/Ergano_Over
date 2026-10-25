namespace ErganiManager.Core.Interfaces;

public class CompanyDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string TaxId { get; set; } = string.Empty;
    public string ErganiUsername { get; set; } = string.Empty;
    public string? ErganiPasswordPlainText { get; set; } // only set when updating; never returned on read
    public string ErganiUsertype { get; set; } = "01";
    public string ErganiBaseUrl { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;

    public int EarlyClockInBlockMinutes { get; set; } = 15;
    public int EarlyDepartureAlertMinutes { get; set; } = 10;
    public bool BlockClockInWithoutSchedule { get; set; } = true;
    public bool BlockClockInOnRestOrAbsentDay { get; set; } = true;

    /// <summary>Which events send an alert mail (bit mask of <see cref="AlertEvent"/>).</summary>
    public int AlertEvents { get; set; } = (int)AlertEvent.Default;
    /// <summary>Clock-in earlier/later than the scheduled start by more than this many minutes sends an alert (0 = off).</summary>
    public int ClockInAlertMinutes { get; set; } = 10;

    public bool AlertEmailEnabled { get; set; }
    public bool AutoRetryFailedSubmissions { get; set; } = true;
    public string? AlertEmailRecipients { get; set; }
    public string? SmtpHost { get; set; }
    public int? SmtpPort { get; set; }
    public string? SmtpUser { get; set; }
    public string? SmtpPasswordPlainText { get; set; }
    public bool SmtpUseTls { get; set; } = true;

    /// <summary>Company picture (null = none).</summary>
    public byte[]? Logo { get; set; }

    /// <summary>Set to true when <see cref="Logo"/> was changed (or removed) and must be saved.
    /// Callers that only edit other fields leave it false, so an existing picture is never wiped.</summary>
    public bool UpdateLogo { get; set; }
}

public interface ICompanyService
{
    Task<List<CompanyDto>> GetAllAsync();
    Task<CompanyDto?> GetByIdAsync(int id);
    Task<int> CreateAsync(CompanyDto dto);
    Task UpdateAsync(CompanyDto dto);
    Task SetActiveAsync(int id, bool isActive);
    Task DeleteAsync(int id);

    /// <summary>
    /// Re-saves the Ergani and mail passwords that older versions encrypted for one PC only, in the format all PCs
    /// of the database can read. Passwords this PC cannot read are left for the PC that saved them. Never throws.
    /// </summary>
    Task MigrateCredentialsAsync();
}
