namespace ErganiManager.Core.Interfaces;

public class WorkCardSubmissionRequest
{
    public int EmployeeId { get; set; }
    public int CompanyId { get; set; }
    public int BranchId { get; set; }
    public string MovementType { get; set; } = string.Empty; // "Arrival" or "Departure"
    public DateTime MovementDateTime { get; set; }
}

public class WorkCardSubmissionOutcome
{
    public bool Success { get; set; }
    public string? Protocol { get; set; }
    public string? SubmissionId { get; set; }
    public string? ErrorMessage { get; set; }
}

/// <summary>
/// Thin contract that the cache-sync logic depends on to push a queued
/// clock-in/out into the main database and onward to Ergani, without the
/// Core.Services sync code needing to know about HTTP, the Ergani SDK, or
/// the WorkCardService's full duplicate-guard/early-block business rules
/// (those have already been validated once, at the moment of the original
/// scan, by the Terminal).
/// </summary>
public interface IWorkCardSubmitter
{
    Task<WorkCardSubmissionOutcome> SubmitAsync(WorkCardSubmissionRequest request);
}
