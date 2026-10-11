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
    public async Task<WorkCardSubmissionOutcome> SubmitAsync(WorkCardSubmissionRequest request, string? aitiologia = null)
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

        var movementTypeCode = WorkCardMovementTypeCodes.FromString(request.MovementType);

        // f_aitiologia: empty while the card is on time (≤ 10 min after the scan, i.e. the first
        // attempt); a justification code once it is late. An explicit code from the caller wins.
        if (string.IsNullOrWhiteSpace(aitiologia))
        {
            aitiologia = DateTime.Now - request.MovementDateTime > WorkCardLogRetryService.OnTimeWindow
                ? WorkCardLogRetryService.DefaultLateJustification
                : string.Empty;
        }

        var submission = new CompanyWorkCardSubmission
        {
            EmployerTaxIdentificationNumber = company.TaxId,
            BusinessBranchNumber            = branch.BranchNumber,
            Comments                        = "Submitted via offline queue sync",
            Details = new WorkCardDetails
            {
                CardDetails = new List<WorkCardEntry>
                {
                    new()
                    {
                        EmployeeTaxIdentificationNumber = employee.TaxId,
                        EmployeeLastName                = employee.LastName,
                        EmployeeFirstName               = employee.FirstName,
                        MovementType                    = movementTypeCode,
                        SubmissionDate                  = DateOnly.FromDateTime(request.MovementDateTime.Date),
                        MovementDateTime                = new DateTimeOffset(request.MovementDateTime,
                                                            TimeZoneInfo.Local.GetUtcOffset(request.MovementDateTime)),
                        LateDeclarationJustification    = aitiologia ?? string.Empty
                    }
                }
            }
        };

        var envelope = new WorkCardSubmissionEnvelope
        {
            Cards = new WorkCardCardArray { Card = new List<CompanyWorkCardSubmission> { submission } }
        };

        var credentials = new ErganiCredentials
        {
            Username = company.ErganiUsername,
            Password = _credentialProtector.Unprotect(company.ErganiPasswordEncrypted),
            Usertype = company.ErganiUsertype,
            BaseUrl  = company.ErganiBaseUrl
        };

        var callResult = await _erganiClient.SubmitWorkCardAsync(credentials, envelope);

        var firstResponse = callResult.Data?.FirstOrDefault();

        // A 200 response carrying a description but no protocol is an Ergani business error.
        var isBusinessError = callResult.IsBusinessError || firstResponse is { IsBusinessError: true };
        var succeeded = callResult.Success && !isBusinessError;
        var errorMessage = succeeded
            ? null
            : (callResult.ErrorMessage ?? firstResponse?.Description ?? "Ergani submission failed.");

        var movementEnum = movementTypeCode == WorkCardMovementTypeCodes.Arrival
            ? Data.Entities.MovementType.Arrival
            : Data.Entities.MovementType.Departure;

        // Background retries call this method repeatedly for the same scan. Reuse the existing
        // WorkCard and log row (matched by employee + movement + time) instead of adding new ones.
        var from = request.MovementDateTime.AddSeconds(-1);
        var to   = request.MovementDateTime.AddSeconds(1);

        var workCard = await db.WorkCards.FirstOrDefaultAsync(w =>
            w.EmployeeId == employee.Id &&
            w.MovementType == movementEnum &&
            w.MovementDateTime >= from &&
            w.MovementDateTime <= to &&
            !w.SubmittedToErgani);

        if (workCard == null)
        {
            workCard = new WorkCard
            {
                EmployeeId = employee.Id,
                BranchId = branch.Id,
                MovementType = movementEnum,
                MovementDateTime = request.MovementDateTime,
                SubmissionDate = DateOnly.FromDateTime(DateTime.Today),
                CreatedAt = DateTime.UtcNow
            };
            db.WorkCards.Add(workCard);
        }

        workCard.SubmittedToErgani = succeeded;
        workCard.SubmissionId = firstResponse?.SubmissionId;
        workCard.Protocol = firstResponse?.Protocol;
        workCard.ResponseRawJson = callResult.ResponseRawJson;
        workCard.LateJustification = string.IsNullOrEmpty(aitiologia) ? null : aitiologia;

        // Save first so a new WorkCard has an Id to link the log row to.
        await db.SaveChangesAsync();

        var log = await db.ApiSubmissionLogs.FirstOrDefaultAsync(l =>
            l.WorkCardId == workCard.Id && l.SubmissionType == "WorkCard");

        if (log == null)
        {
            log = new ApiSubmissionLog
            {
                CompanyId = company.Id,
                EmployeeId = employee.Id,
                SubmissionType = "WorkCard",
                WorkCardId = workCard.Id,
                SubmissionDate = DateTime.UtcNow   // first attempt — never changed afterwards
            };
            db.ApiSubmissionLogs.Add(log);
        }
        else
        {
            log.RetryCount++;
            log.LastRetryAt = DateTime.UtcNow;
        }

        log.RequestPayloadJson = callResult.RequestPayloadJson;
        log.ResponseRawJson = callResult.ResponseRawJson;
        log.SubmissionId = firstResponse?.SubmissionId;
        log.Protocol = firstResponse?.Protocol;
        log.HttpStatusCode = callResult.HttpStatusCode;
        log.Success = succeeded;
        log.ErrorMessage = errorMessage;
        log.DurationMs = callResult.DurationMs;

        await db.SaveChangesAsync();

        if (!succeeded)
        {
            _logger.LogWarning(
                "Ergani work card submission failed for employee {EmployeeId}: {Error}",
                employee.Id, errorMessage);
        }

        return new WorkCardSubmissionOutcome
        {
            Success = succeeded,
            Protocol = firstResponse?.Protocol,
            SubmissionId = firstResponse?.SubmissionId,
            ErrorMessage = errorMessage,
            IsBusinessError = isBusinessError
        };
    }
}
