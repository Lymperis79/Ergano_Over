namespace ErganiManager.Core.Interfaces;

public class WorkCardHistoryDto
{
    public int Id { get; set; }
    public int EmployeeId { get; set; }
    public string EmployeeFullName { get; set; } = string.Empty;
    public string EmployeeTaxId { get; set; } = string.Empty;
    public string BranchName { get; set; } = string.Empty;
    public string MovementType { get; set; } = string.Empty; // "Arrival" / "Departure"
    public DateTime MovementDateTime { get; set; }
    public bool SubmittedToErgani { get; set; }
    public string? Protocol { get; set; }
    public bool WasEarlyDeparture { get; set; }
    public int? EarlyDepartureMinutes { get; set; }
    public bool EmailAlertSent { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class WorkCardHistoryFilter
{
    public DateOnly? FromDate { get; set; }
    public DateOnly? ToDate { get; set; }
    public int? EmployeeId { get; set; }
    public int? BranchId { get; set; }
    public string? MovementType { get; set; } // null = both
    public bool? EarlyDepartureOnly { get; set; }
}

public interface IWorkCardHistoryService
{
    Task<List<WorkCardHistoryDto>> GetAsync(int companyId, WorkCardHistoryFilter filter);
}
