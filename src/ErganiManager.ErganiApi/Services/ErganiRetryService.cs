using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ErganiManager.Core.Interfaces;
using ErganiManager.Data;
using ErganiManager.Data.Entities;
using ErganiManager.ErganiApi.Models;
using ErganiManager.LocalCache;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ErganiManager.ErganiApi.Services;

public class ErganiRetryService
{
    private readonly IErganiClient _erganiClient;
    private readonly IErganiHealthCheckService _healthCheck;
    private readonly IConnectionStateService _connectionState;
    private readonly ICredentialProtector _credentialProtector;
    private readonly IWorkCardSubmitter _workCardSubmitter;
    private readonly ILogger<ErganiRetryService> _logger;

    private CancellationTokenSource? _cts;
    private Task? _runnerTask;
    private bool _started;

    private static readonly TimeSpan HealthCheckInterval = TimeSpan.FromMinutes(2);
    private const int MaxRetryAttempts = 10;

    public ErganiRetryService(
        IErganiClient erganiClient,
        IErganiHealthCheckService healthCheck,
        IConnectionStateService connectionState,
        ICredentialProtector credentialProtector,
        IWorkCardSubmitter workCardSubmitter,
        ILogger<ErganiRetryService> logger)
    {
        _erganiClient = erganiClient;
        _healthCheck = healthCheck;
        _connectionState = connectionState;
        _credentialProtector = credentialProtector;
        _workCardSubmitter = workCardSubmitter;
        _logger = logger;
    }

    /// <summary>Idempotent — safe to call multiple times.</summary>
    public void Start()
    {
        if (_started) return;
        _started = true;
        _cts = new CancellationTokenSource();
        _runnerTask = Task.Run(() => RunAsync(_cts.Token));
        _logger.LogInformation("Ergani retry service started.");
    }

    public async Task StopAsync()
    {
        _cts?.Cancel();
        if (_runnerTask != null)
            try { await _runnerTask; } catch (OperationCanceledException) { }
        _logger.LogInformation("Ergani retry service stopped.");
    }

    private async Task RunAsync(CancellationToken ct)
    {
        // Wait before first run so we don't compete with app startup and login
        await Task.Delay(TimeSpan.FromSeconds(30), ct).ConfigureAwait(false);

        while (!ct.IsCancellationRequested)
        {
            try { await ProcessAllCompaniesAsync(ct); }
            catch (OperationCanceledException) { break; }
            catch (Exception ex) { _logger.LogError(ex, "Error in Ergani retry loop."); }

            await Task.Delay(HealthCheckInterval, ct).ConfigureAwait(false);
        }
    }

    private async Task ProcessAllCompaniesAsync(CancellationToken ct)
    {
        await using var db = new AppDbContext(_connectionState.GetDbOptions());

        var companies = await db.Companies.Where(c => c.IsActive).ToListAsync(ct);
        using var cache = LocalCacheDbContextFactory.Create();

        foreach (var company in companies)
        {
            if (ct.IsCancellationRequested) break;
            await ProcessCompanyAsync(company, db, cache, ct);
        }
    }

    private async Task ProcessCompanyAsync(
        Company company, AppDbContext db,
        LocalCache.LocalCacheDbContext cache, CancellationToken ct)
    {
        if (!company.AutoRetryFailedSubmissions)
        {
            _logger.LogDebug("Auto-retry disabled for company {Id}.", company.Id);
            return;
        }

        var credentials = new ErganiCredentials
        {
            Username = company.ErganiUsername,
            Password = _credentialProtector.Unprotect(company.ErganiPasswordEncrypted),
            BaseUrl  = company.ErganiBaseUrl
        };

        var status = await _healthCheck.CheckAsync(credentials, ct);
        if (status == ErganiServiceStatus.Offline)
        {
            _logger.LogDebug("Ergani offline for company {Id} — retry deferred.", company.Id);
            return;
        }

        await FlushFailedSubmissionsAsync(company, credentials, db, cache, ct);
        await FlushPendingSubmissionsAsync(company, cache, ct);
    }

    private async Task FlushFailedSubmissionsAsync(
        Company company, ErganiCredentials credentials,
        AppDbContext db, LocalCache.LocalCacheDbContext cache, CancellationToken ct)
    {
        var failed = await cache.FailedSubmissions
            .Where(f => f.CompanyId == company.Id && !f.Resolved && f.RetryCount < MaxRetryAttempts)
            .OrderBy(f => f.OriginalScannedAt)
            .ToListAsync(ct);

        if (failed.Count == 0) return;
        _logger.LogInformation("Retrying {Count} failed submission(s) for company {Id}.", failed.Count, company.Id);

        foreach (var item in failed)
        {
            if (ct.IsCancellationRequested) break;
            try
            {
                var employee = await db.Employees.FindAsync(new object[] { item.EmployeeId }, ct);
                var branch   = await db.Branches.FindAsync(new object[] { item.BranchId }, ct);

                if (employee == null || branch == null)
                {
                    item.Resolved = true;
                    item.ResolvedAt = DateTime.UtcNow;
                    item.LastRetryError = "Employee or branch deleted.";
                    await cache.SaveChangesAsync(ct);
                    continue;
                }

                var movementType = item.MovementType == "Arrival"
                    ? WorkCardMovementType.ARRIVAL
                    : WorkCardMovementType.DEPARTURE;

                var submission = new CompanyWorkCardSubmission
                {
                    EmployerTaxIdentificationNumber = company.TaxId,
                    BusinessBranchNumber = branch.BranchNumber,
                    Comments = $"Retry — original error: {item.FailureReason}",
                    CardDetails = new List<WorkCardEntry>
                    {
                        new()
                        {
                            EmployeeTaxIdentificationNumber = employee.TaxId,
                            EmployeeLastName  = employee.LastName,
                            EmployeeFirstName = employee.FirstName,
                            MovementType      = movementType,
                            SubmissionDate    = DateOnly.FromDateTime(DateTime.Today),
                            MovementDateTime  = item.OriginalScannedAt,
                            LateDeclarationJustification = LateDeclarationJustification.EMPLOYER_SYSTEMS_UNAVAILABLE
                        }
                    }
                };

                var result = await _erganiClient.SubmitWorkCardAsync(
                    credentials, new List<CompanyWorkCardSubmission> { submission }, ct);

                var first = result.Data?.FirstOrDefault();
                item.RetryCount++;
                item.LastRetryAt = DateTime.UtcNow;

                if (result.Success && first != null && !first.IsBusinessError)
                {
                    item.Resolved = true;
                    item.ResolvedAt = DateTime.UtcNow;
                    item.ResolvedProtocol = first.Protocol;
                    item.LastRetryError = null;

                    var wc = await db.WorkCards
                        .FirstOrDefaultAsync(w => w.EmployeeId == item.EmployeeId
                            && w.MovementDateTime == item.OriginalScannedAt, ct);
                    if (wc != null)
                    {
                        wc.SubmittedToErgani = true;
                        wc.Protocol = first.Protocol;
                        wc.SubmissionId = first.SubmissionId;
                    }

                    db.ApiSubmissionLogs.Add(new ApiSubmissionLog
                    {
                        CompanyId = company.Id, EmployeeId = employee.Id,
                        SubmissionType = "WorkCard-Retry",
                        RequestPayloadJson = result.RequestPayloadJson,
                        ResponseRawJson = result.ResponseRawJson,
                        SubmissionId = first.SubmissionId,
                        Protocol = first.Protocol,
                        SubmissionDate = DateTime.UtcNow,
                        HttpStatusCode = result.HttpStatusCode,
                        Success = true, DurationMs = result.DurationMs
                    });

                    await db.SaveChangesAsync(ct);
                    _logger.LogInformation("Retry succeeded for employee {EId}, Protocol: {P}",
                        item.EmployeeId, first.Protocol);
                }
                else
                {
                    item.LastRetryError = result.IsBusinessError
                        ? result.BusinessErrorDescription : result.ErrorMessage;
                    if (item.RetryCount >= MaxRetryAttempts)
                        _logger.LogWarning("Max retries reached for FailedSubmission {Id}.", item.Id);
                }

                await cache.SaveChangesAsync(ct);
                await Task.Delay(TimeSpan.FromSeconds(2), ct);
            }
            catch (Exception ex)
            {
                item.RetryCount++;
                item.LastRetryAt = DateTime.UtcNow;
                item.LastRetryError = ex.Message;
                await cache.SaveChangesAsync(ct);
                _logger.LogError(ex, "Exception retrying FailedSubmission {Id}.", item.Id);
            }
        }
    }

    private async Task FlushPendingSubmissionsAsync(
        Company company, LocalCache.LocalCacheDbContext cache, CancellationToken ct)
    {
        var pending = await cache.PendingSubmissions
            .Where(p => p.CompanyId == company.Id && !p.Synced && p.SyncAttempts < MaxRetryAttempts)
            .OrderBy(p => p.ScannedAt)
            .ToListAsync(ct);

        if (pending.Count == 0) return;
        _logger.LogInformation("Flushing {Count} pending submission(s) for company {Id}.",
            pending.Count, company.Id);

        foreach (var item in pending)
        {
            if (ct.IsCancellationRequested) break;

            var outcome = await _workCardSubmitter.SubmitAsync(new WorkCardSubmissionRequest
            {
                EmployeeId = item.EmployeeId, CompanyId = item.CompanyId,
                BranchId = item.BranchId, MovementType = item.MovementType,
                MovementDateTime = item.ScannedAt
            });

            item.SyncAttempts++;
            if (outcome.Success) { item.Synced = true; item.SyncedAt = DateTime.UtcNow; }
            else item.LastSyncError = outcome.ErrorMessage;

            await cache.SaveChangesAsync(ct);
            await Task.Delay(TimeSpan.FromSeconds(2), ct);
        }
    }
}
