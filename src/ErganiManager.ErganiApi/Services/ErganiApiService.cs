using ErganiManager.Core.Interfaces;
using ErganiManager.Data;
using ErganiManager.Data.Entities;
using ErganiManager.ErganiApi.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ErganiManager.ErganiApi.Services;

/// <summary>
/// High-level Ergani integration service. Bridges Company/Employee data from
/// the main database to the low-level ErganiClient, and persists every call
/// (success or failure) to ApiSubmissionLog for audit purposes. Implements
/// IWorkCardSubmitter so it can be wired directly into Core's CacheSyncService
/// for flushing queued offline scans.
/// </summary>
public class ErganiApiService : IWorkCardSubmitter
{
    private readonly IErganiClient _erganiClient;
    private readonly ICredentialProtector _credentialProtector;
    private readonly IConnectionStateService _connectionState;
    private readonly ILogger<ErganiApiService> _logger;

    public ErganiApiService(
        IErganiClient erganiClient,
        ICredentialProtector credentialProtector,
        IConnectionStateService connectionState,
        ILogger<ErganiApiService> logger)
    {
        _erganiClient = erganiClient;
        _credentialProtector = credentialProtector;
        _connectionState = connectionState;
        _logger = logger;
    }

    /// <summary>
    /// Implements IWorkCardSubmitter for Core's CacheSyncService. Looks up the
    /// employee/company/branch fresh from the database (the queued request only
    /// carries IDs), builds the Ergani payload, submits, and writes a new
    /// WorkCard + ApiSubmissionLog row reflecting the outcome.
    /// </summary>
    public async Task<WorkCardSubmissionOutcome> SubmitAsync(WorkCardSubmissionRequest request)
    {
        var config = _connectionState.LoadConfig();
        if (config == null)
            return new WorkCardSubmissionOutcome { Success = false, ErrorMessage = "Database not configured." };

        var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();
        DbProviderFactory.Configure(optionsBuilder, config);

        await using var db = new AppDbContext(optionsBuilder.Options);

        var employee = await db.Employees.FindAsync(request.EmployeeId);
        var company = await db.Companies.FindAsync(request.CompanyId);
        var branch = await db.Branches.FindAsync(request.BranchId);

        if (employee == null || company == null || branch == null)
        {
            return new WorkCardSubmissionOutcome
            {
                Success = false,
                ErrorMessage = "Employee, company, or branch no longer exists — cannot submit queued scan."
            };
        }

        var movementType = request.MovementType == "Arrival"
            ? WorkCardMovementType.ARRIVAL
            : WorkCardMovementType.DEPARTURE;

        var submission = new CompanyWorkCardSubmission
        {
            EmployerTaxIdentificationNumber = company.TaxId,
            BusinessBranchNumber = branch.BranchNumber,
            Comments = "Submitted via offline queue sync",
            CardDetails = new List<WorkCardEntry>
            {
                new()
                {
                    EmployeeTaxIdentificationNumber = employee.TaxId,
                    EmployeeLastName = employee.LastName,
                    EmployeeFirstName = employee.FirstName,
                    MovementType = movementType,
                    SubmissionDate = DateOnly.FromDateTime(DateTime.Today),
                    MovementDateTime = request.MovementDateTime
                }
            }
        };

        var credentials = new ErganiCredentials
        {
            Username = company.ErganiUsername,
            Password = _credentialProtector.Unprotect(company.ErganiPasswordEncrypted),
            BaseUrl = company.ErganiBaseUrl
        };

        var callResult = await _erganiClient.SubmitWorkCardAsync(credentials, new List<CompanyWorkCardSubmission> { submission });

        var firstResponse = callResult.Data?.FirstOrDefault();

        // Persist the WorkCard record regardless of outcome — failed/blocked
        // submissions are still useful audit history.
        var workCard = new WorkCard
        {
            EmployeeId = employee.Id,
            BranchId = branch.Id,
            MovementType = movementType == WorkCardMovementType.ARRIVAL
                ? Data.Entities.MovementType.Arrival
                : Data.Entities.MovementType.Departure,
            MovementDateTime = request.MovementDateTime,
            SubmissionDate = DateOnly.FromDateTime(DateTime.Today),
            SubmittedToErgani = callResult.Success,
            SubmissionId = firstResponse?.SubmissionId,
            Protocol = firstResponse?.Protocol,
            ResponseRawJson = callResult.ResponseRawJson,
            CreatedAt = DateTime.UtcNow
        };
        db.WorkCards.Add(workCard);

        db.ApiSubmissionLogs.Add(new ApiSubmissionLog
        {
            CompanyId = company.Id,
            EmployeeId = employee.Id,
            SubmissionType = "WorkCard",
            RequestPayloadJson = callResult.RequestPayloadJson,
            ResponseRawJson = callResult.ResponseRawJson,
            SubmissionId = firstResponse?.SubmissionId,
            Protocol = firstResponse?.Protocol,
            SubmissionDate = DateTime.UtcNow,
            HttpStatusCode = callResult.HttpStatusCode,
            Success = callResult.Success,
            ErrorMessage = callResult.ErrorMessage,
            DurationMs = callResult.DurationMs
        });

        await db.SaveChangesAsync();

        if (!callResult.Success)
        {
            _logger.LogWarning(
                "Ergani work card submission failed for employee {EmployeeId}: {Error}",
                employee.Id, callResult.ErrorMessage);
        }

        return new WorkCardSubmissionOutcome
        {
            Success = callResult.Success,
            Protocol = firstResponse?.Protocol,
            SubmissionId = firstResponse?.SubmissionId,
            ErrorMessage = callResult.ErrorMessage
        };
    }
}
