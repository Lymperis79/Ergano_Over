using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ErganiManager.Core.Interfaces;
using ErganiManager.Core.Models;
using ErganiManager.Data;
using ErganiManager.Data.Entities;
using ErganiManager.ErganiApi.Models;
using ErganiManager.LocalCache;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ErganiManager.ErganiApi.Services;

public enum QueuedScanStatus
{
    /// <summary>Ergani accepted the card.</summary>
    Sent,
    /// <summary>A queued work card is currently being sent to Ergani.</summary>
    Retrying,
    /// <summary>Not delivered yet (no connection / Ergani down) — will be retried automatically.</summary>
    Waiting,
    /// <summary>Ergani rejected the card; it needs attention (see the API log).</summary>
    Rejected
}

public record QueuedScanUpdate(int PendingId, QueuedScanStatus Status, string? Protocol, string? Error, int Attempts = 0);

public class ErganiRetryService
{
    /// <summary>
    /// Raised from the background thread whenever a queued scan changes state. Subscribers that
    /// touch the UI must marshal to the UI thread themselves.
    /// </summary>
    public event EventHandler<QueuedScanUpdate>? QueuedScanUpdated;

    private void Notify(int pendingId, QueuedScanStatus status, string? protocol, string? error, int attempts = 0)
    {
        try { QueuedScanUpdated?.Invoke(this, new QueuedScanUpdate(pendingId, status, protocol, error, attempts)); }
        catch (Exception ex) { _logger.LogWarning(ex, "QueuedScanUpdated handler failed."); }
    }

    private readonly IErganiClient _erganiClient;
    private readonly IErganiHealthCheckService _healthCheck;
    private readonly IConnectionStateService _connectionState;
    private readonly ICredentialProtector _credentialProtector;
    private readonly IWorkCardSubmitter _workCardSubmitter;
    private readonly ILogger<ErganiRetryService> _logger;

    private CancellationTokenSource? _cts;
    private Task? _runnerTask;
    private bool _started;

    private static readonly TimeSpan HealthCheckInterval = TimeSpan.FromMinutes(1);

    // Lets the scan screens wake the loop immediately after queuing a scan, instead of
    // waiting for the next interval.
    private readonly SemaphoreSlim _wake = new(0, 1);

    // Prevent a manual selected retry from racing the automatic queue worker.
    private readonly SemaphoreSlim _pendingGate = new(1, 1);

    /// <summary>
    /// Ask the background loop to run right now (e.g. a scan was just saved, or the user
    /// pressed Retry Now). Returns immediately; never blocks the caller.
    /// </summary>
    public void TriggerNow()
    {
        try { _wake.Release(); }
        catch (SemaphoreFullException) { /* a run is already requested */ }
    }
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
        await _wake.WaitAsync(TimeSpan.FromSeconds(30), ct).ConfigureAwait(false);

        while (!ct.IsCancellationRequested)
        {
            try { await ProcessAllCompaniesAsync(ct); }
            catch (OperationCanceledException) { break; }
            catch (Exception ex) { _logger.LogError(ex, "Error in Ergani retry loop."); }

            // Sleeps until the next interval, or until TriggerNow() wakes it.
            await _wake.WaitAsync(HealthCheckInterval, ct).ConfigureAwait(false);
        }
    }

    private async Task ProcessAllCompaniesAsync(CancellationToken ct)
    {
        // Refresh the cached connection state here, off the UI thread, so the scan screens can
        // read it instantly instead of probing the database during a scan.
        var state = await _connectionState.EvaluateAsync();
        if (state != AppConnectionState.Normal)
        {
            _logger.LogDebug("Main database not available ({State}) — queued scans stay queued.", state);
            return;
        }

        await using var db = new AppDbContext(_connectionState.GetDbOptions());

        var companies = await db.Companies.Where(c => c.IsActive).ToListAsync(ct);
        using var cache = LocalCacheDbContextFactory.Create();

        foreach (var company in companies)
        {
            if (ct.IsCancellationRequested) break;
            try { await ProcessCompanyAsync(company, db, cache, ct); }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                // One company's problem (e.g. missing credentials) must not block the others.
                _logger.LogError(ex, "Retry processing failed for company {Id}.", company.Id);
            }
        }
    }

    private async Task ProcessCompanyAsync(
        Company company, AppDbContext db,
        LocalCache.LocalCacheDbContext cache, CancellationToken ct)
    {
        var credentials = new ErganiCredentials
        {
            Username = company.ErganiUsername,
            Password = _credentialProtector.Unprotect(company.ErganiPasswordEncrypted),
            Usertype = company.ErganiUsertype,
            BaseUrl  = company.ErganiBaseUrl
        };

        var status = await _healthCheck.CheckAsync(credentials, ct);
        if (status == ErganiServiceStatus.Offline)
        {
            _logger.LogDebug("Ergani offline for company {Id} — retry deferred.", company.Id);
            return;
        }

        // Scans saved by the scan screens are the first delivery, so they are always sent.
        await FlushPendingSubmissionsAsync(company, cache, ct);

        // The older failed-submission table is only retried when auto-retry is enabled.
        if (company.AutoRetryFailedSubmissions)
            await FlushFailedSubmissionsAsync(company, credentials, db, cache, ct);
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

                var movementTypeCode = WorkCardMovementTypeCodes.FromString(item.MovementType);

                var submission = new CompanyWorkCardSubmission
                {
                    EmployerTaxIdentificationNumber = company.TaxId,
                    BusinessBranchNumber            = branch.BranchNumber,
                    Comments                        = $"Retry — original error: {item.FailureReason}",
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
                                SubmissionDate                  = DateOnly.FromDateTime(item.OriginalScannedAt.Date),
                                MovementDateTime                = new DateTimeOffset(item.OriginalScannedAt,
                                                                    TimeZoneInfo.Local.GetUtcOffset(item.OriginalScannedAt)),
                                LateDeclarationJustification    = "EMPLOYER_SYSTEMS_UNAVAILABLE"
                            }
                        }
                    }
                };

                var envelope = new WorkCardSubmissionEnvelope
                {
                    Cards = new WorkCardCardArray { Card = new List<CompanyWorkCardSubmission> { submission } }
                };

                var result = await _erganiClient.SubmitWorkCardAsync(credentials, envelope, ct);

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

    /// <summary>
    /// Sends scans that the scan screens saved locally. Runs in the background, so a slow or
    /// unreachable Ergani never holds up the next person scanning.
    ///
    /// * Success                        → marked synced.
    /// * Ergani rejected the card       → marked handled; the failed API-log row (updated in place
    ///                                    by the submitter) can be retried from the API Log page.
    /// * Network / Ergani outage        → stays queued and is retried on the next cycle, with no
    ///                                    attempt limit, so a long outage can never lose a scan.
    /// </summary>
    private async Task FlushPendingSubmissionsAsync(
        Company company, LocalCache.LocalCacheDbContext cache, CancellationToken ct)
    {
        await _pendingGate.WaitAsync(ct);
        try
        {
            await FlushPendingSubmissionsCoreAsync(company, cache, ct);
        }
        finally
        {
            _pendingGate.Release();
        }
    }

    private async Task FlushPendingSubmissionsCoreAsync(
        Company company, LocalCache.LocalCacheDbContext cache, CancellationToken ct)
    {
        var pending = await cache.PendingSubmissions
            .Where(p => p.CompanyId == company.Id && !p.Synced)
            .OrderBy(p => p.ScannedAt)
            .ToListAsync(ct);

        if (pending.Count == 0) return;
        _logger.LogInformation("Sending {Count} queued scan(s) for company {Id}.",
            pending.Count, company.Id);

        foreach (var item in pending)
        {
            if (ct.IsCancellationRequested) break;

            try
            {
                // The age of the scan decides f_aitiologia inside the submitter
                // (empty within 10 minutes, a justification code after that).
                Notify(item.Id, QueuedScanStatus.Retrying, null, item.LastSyncError, item.SyncAttempts);

                var outcome = await _workCardSubmitter.SubmitAsync(new WorkCardSubmissionRequest
                {
                    EmployeeId = item.EmployeeId, CompanyId = item.CompanyId,
                    BranchId = item.BranchId, MovementType = item.MovementType,
                    MovementDateTime = item.ScannedAt
                });

                item.SyncAttempts++;

                if (outcome.Success)
                {
                    item.Synced = true;
                    item.SyncedAt = DateTime.UtcNow;
                    item.LastSyncError = null;
                    await cache.SaveChangesAsync(ct);
                    Notify(item.Id, QueuedScanStatus.Sent, outcome.Protocol, null, item.SyncAttempts);
                }
                else if (outcome.IsBusinessError)
                {
                    // Resending an unchanged card will not help; keep it visible in the API log.
                    item.Synced = true;
                    item.SyncedAt = DateTime.UtcNow;
                    item.LastSyncError = outcome.ErrorMessage;
                    await cache.SaveChangesAsync(ct);
                    _logger.LogWarning("Ergani rejected queued scan {Id}: {Error}", item.Id, outcome.ErrorMessage);
                    Notify(item.Id, QueuedScanStatus.Rejected, null, outcome.ErrorMessage, item.SyncAttempts);
                }
                else
                {
                    // Connection / service problem: keep it queued, keep the order, try next cycle.
                    item.LastSyncError = outcome.ErrorMessage;
                    await cache.SaveChangesAsync(ct);
                    _logger.LogInformation("Queued scan {Id} not delivered yet: {Error}", item.Id, outcome.ErrorMessage);
                    Notify(item.Id, QueuedScanStatus.Waiting, null, outcome.ErrorMessage, item.SyncAttempts);
                    break;
                }

                await Task.Delay(TimeSpan.FromSeconds(1), ct);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                item.SyncAttempts++;
                item.LastSyncError = ex.Message;
                await cache.SaveChangesAsync(ct);
                _logger.LogError(ex, "Exception sending queued scan {Id}; will retry.", item.Id);
                Notify(item.Id, QueuedScanStatus.Waiting, null, ex.Message, item.SyncAttempts);
                break;
            }
        }


    }

    /// <summary>
    /// Immediately retries only the selected locally queued scans. The normal background
    /// worker remains responsible for automatic retries; the gate prevents the two paths
    /// from sending the same PendingSubmission concurrently.
    /// </summary>
    public async Task<IReadOnlyList<(int PendingId, bool Success, string? Protocol, string? Error)>>
        RetryPendingAsync(IEnumerable<int> pendingIds, CancellationToken ct = default)
    {
        var ids = pendingIds.Distinct().ToList();
        var results = new List<(int PendingId, bool Success, string? Protocol, string? Error)>();
        if (ids.Count == 0)
            return results;

        await _pendingGate.WaitAsync(ct);
        try
        {
            using var cache = LocalCacheDbContextFactory.Create();
            var items = await cache.PendingSubmissions
                .Where(p => ids.Contains(p.Id) && !p.Synced)
                .OrderBy(p => p.ScannedAt)
                .ToListAsync(ct);

            foreach (var item in items)
            {
                ct.ThrowIfCancellationRequested();
                item.SyncAttempts++;

                try
                {
                    Notify(item.Id, QueuedScanStatus.Retrying, null, item.LastSyncError, item.SyncAttempts);

                    var outcome = await _workCardSubmitter.SubmitAsync(new WorkCardSubmissionRequest
                    {
                        EmployeeId = item.EmployeeId,
                        CompanyId = item.CompanyId,
                        BranchId = item.BranchId,
                        MovementType = item.MovementType,
                        MovementDateTime = item.ScannedAt
                    });

                    if (outcome.Success)
                    {
                        item.Synced = true;
                        item.SyncedAt = DateTime.UtcNow;
                        item.LastSyncError = null;
                        results.Add((item.Id, true, outcome.Protocol, null));
                        Notify(item.Id, QueuedScanStatus.Sent, outcome.Protocol, null, item.SyncAttempts);
                    }
                    else if (outcome.IsBusinessError)
                    {
                        // Ergani rejected the card; do not keep hammering the same invalid request.
                        item.Synced = true;
                        item.SyncedAt = DateTime.UtcNow;
                        item.LastSyncError = outcome.ErrorMessage;
                        results.Add((item.Id, false, null, outcome.ErrorMessage));
                        Notify(item.Id, QueuedScanStatus.Rejected, null, outcome.ErrorMessage, item.SyncAttempts);
                    }
                    else
                    {
                        item.LastSyncError = outcome.ErrorMessage;
                        results.Add((item.Id, false, null, outcome.ErrorMessage));
                        Notify(item.Id, QueuedScanStatus.Waiting, null, outcome.ErrorMessage, item.SyncAttempts);
                    }
                }
                catch (Exception ex)
                {
                    item.LastSyncError = ex.Message;
                    results.Add((item.Id, false, null, ex.Message));
                    Notify(item.Id, QueuedScanStatus.Waiting, null, ex.Message, item.SyncAttempts);
                }

                await cache.SaveChangesAsync(ct);
            }

            // IDs that disappeared because the background worker already sent them are treated
            // as successfully handled; the history view will reload their WorkCard row.
            foreach (var missing in ids.Except(items.Select(i => i.Id)))
                results.Add((missing, true, null, "Already sent or no longer queued."));

            return results;
        }
        finally
        {
            _pendingGate.Release();
        }
    }
}
