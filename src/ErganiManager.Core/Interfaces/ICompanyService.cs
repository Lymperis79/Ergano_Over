namespace ErganiManager.Core.Interfaces;

public class CompanyDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string TaxId { get; set; } = string.Empty;
    public string ErganiUsername { get; set; } = string.Empty;
    public string? ErganiPasswordPlainText { get; set; } // only set when updating; never returned on read
    public string ErganiBaseUrl { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;

    public int EarlyClockInBlockMinutes { get; set; } = 15;
    public int EarlyDepartureAlertMinutes { get; set; } = 10;
    public bool BlockClockInWithoutSchedule { get; set; } = true;

    public bool AlertEmailEnabled { get; set; }
    public bool AutoRetryFailedSubmissions { get; set; } = true;
    public string? AlertEmailRecipients { get; set; }
    public string? SmtpHost { get; set; }
    public int? SmtpPort { get; set; }
    public string? SmtpUser { get; set; }
    public string? SmtpPasswordPlainText { get; set; }
    public bool SmtpUseTls { get; set; } = true;
}

public interface ICompanyService
{
    Task<List<CompanyDto>> GetAllAsync();
    Task<CompanyDto?> GetByIdAsync(int id);
    Task<int> CreateAsync(CompanyDto dto);
    Task UpdateAsync(CompanyDto dto);
    Task SetActiveAsync(int id, bool isActive);
    Task DeleteAsync(int id);
}
