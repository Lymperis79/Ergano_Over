namespace ErganiManager.Core.Interfaces;

public class EarlyDepartureAlertRequest
{
    public string EmployeeFullName { get; set; } = string.Empty;
    public string EmployeeTaxId { get; set; } = string.Empty;
    public string BranchName { get; set; } = string.Empty;
    public string CompanyName { get; set; } = string.Empty;
    public TimeOnly ScheduledEndTime { get; set; }
    public DateTime ActualDeparture { get; set; }
    public int EarlyMinutes { get; set; }
    public string? Protocol { get; set; }
}

public interface IEmailAlertService
{
    /// <summary>
    /// Sends an early-departure alert email using the SMTP settings stored
    /// on the company. Returns (true, null) on success or (false, errorMessage)
    /// on failure — never throws, so a failed email doesn't roll back the
    /// WorkCard submission that triggered it.
    /// </summary>
    Task<(bool Success, string? ErrorMessage)> SendEarlyDepartureAlertAsync(
        int companyId, EarlyDepartureAlertRequest request);
}
