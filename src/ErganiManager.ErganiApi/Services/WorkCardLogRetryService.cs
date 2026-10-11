using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ErganiManager.Core.Interfaces;
using ErganiManager.Data;
using ErganiManager.ErganiApi.Models;
using ErganiManager.LocalCache;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ErganiManager.ErganiApi.Services;

public enum LogRetryStatus { Succeeded, Failed, Skipped }

public record LogRetryResult(int LogId, LogRetryStatus Status, string? Message);

public record LogRetryUpdate(int LogId, LogRetryStatus Status, string? Message, int RetryCount = 0, string? Protocol = null);

/// <summary>
/// Manual retry of work cards that failed to upload, driven from the API Log page.
///
/// Rules:
///  * The failed ApiSubmissionLog row is UPDATED IN PLACE — no new log line is created,
///    whether the retry succeeds or fails again. RetryCount / LastRetryAt track the attempts.
///  * The original request payload stored on the row is re-sent. f_aitiologia depends on the
///    time since the FIRST attempt (ApiSubmissionLog.SubmissionDate, which is never changed
///    by a retry):
///      - within 10 minutes of the first attempt → f_aitiologia is sent EMPTY
///        (Ergani rejects a justification on an on-time submission)
///      - more than 10 minutes after it           → the chosen justification code is sent
///        (a late submission without one is rejected)
///  * When a retry succeeds the matching WorkCard record is marked as submitted.
/// </summary>
public class WorkCardLogRetryService
{
    /// <summary>Raised whenever a manual API-log retry changes state.</summary>
    public event EventHandler<LogRetryUpdate>? RetryUpdated;

    private void Notify(int logId, LogRetryStatus status, string? message, int retryCount = 0, string? protocol = null)
    {
        try { RetryUpdated?.Invoke(this, new LogRetryUpdate(logId, status, message, retryCount, protocol)); }
        catch (Exception ex) { _logger.LogWarning(ex, "API-log retry notification failed."); }
    }

    // One retry batch at a time, so a double-click can never send the same card twice.
    private static readonly SemaphoreSlim Gate = new(1, 1);

    private static readonly JsonSerializerOptions ReadOptions = new() { PropertyNameCaseInsensitive = true };

    private static readonly TimeSpan DelayBetweenCalls = TimeSpan.FromSeconds(1);

    /// <summary>Ergani's on-time window, measured from the first attempt.</summary>
    public static readonly TimeSpan OnTimeWindow = TimeSpan.FromMinutes(10);

    /// <summary>Used when the caller does not pick a code. 002 = problem with employer systems.</summary>
    public const string DefaultLateJustification = AitiologiaCodes.EmployerSystemUnavailable;

    private readonly IErganiClient _erganiClient;
    private readonly ICredentialProtector _credentialProtector;
    private readonly IConnectionStateService _connectionState;
    private readonly ILogger<WorkCardLogRetryService> _logger;

    public WorkCardLogRetryService(
        IErganiClient erganiClient,
        ICredentialProtector credentialProtector,
        IConnectionStateService connectionState,
        ILogger<WorkCardLogRetryService> logger)
    {
        _erganiClient = erganiClient;
        _credentialProtector = credentialProtector;
        _connectionState = connectionState;
        _logger = logger;
    }

    /// <param name="lateJustificationCode">
    /// f_aitiologia code (001/002/003) sent for entries whose first attempt was more than
    /// 10 minutes ago. Ignored for entries still inside the 10-minute window.
    /// </param>
    public async Task<IReadOnlyList<LogRetryResult>> RetryAsync(
        IEnumerable<int> logIds, string? lateJustificationCode = null, CancellationToken ct = default)
    {
        var lateCode = string.IsNullOrWhiteSpace(lateJustificationCode)
            ? DefaultLateJustification
            : lateJustificationCode.Trim();

        var ids = logIds.Distinct().ToList();
        var results = new List<LogRetryResult>();
        if (ids.Count == 0) return results;

        await Gate.WaitAsync(ct);
        try
        {
            await using var db = new AppDbContext(_connectionState.GetDbOptions());

            for (var i = 0; i < ids.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                results.Add(await RetryOneAsync(db, ids[i], lateCode, ct));

                if (i < ids.Count - 1)
                    await Task.Delay(DelayBetweenCalls, ct);
            }
        }
        finally
        {
            Gate.Release();
        }

        return results;
    }

    private async Task<LogRetryResult> RetryOneAsync(AppDbContext db, int logId, string lateCode, CancellationToken ct)
    {
        var log = await db.ApiSubmissionLogs.FirstOrDefaultAsync(l => l.Id == logId, ct);

        if (log == null)
            return new(logId, LogRetryStatus.Skipped, "Log entry not found.");
        if (log.Success)
            return new(logId, LogRetryStatus.Skipped, "Already submitted successfully.");
        if (log.SubmissionType != "WorkCard")
            return new(logId, LogRetryStatus.Skipped, "Only work card submissions can be retried.");
        if (string.IsNullOrWhiteSpace(log.RequestPayloadJson))
            return new(logId, LogRetryStatus.Skipped, "No stored request payload to resend.");

        WorkCardSubmissionEnvelope? envelope;
        try
        {
            envelope = JsonSerializer.Deserialize<WorkCardSubmissionEnvelope>(log.RequestPayloadJson, ReadOptions);
        }
        catch (JsonException ex)
        {
            return new(logId, LogRetryStatus.Skipped, $"Stored payload could not be read: {ex.Message}");
        }

        var entries = envelope?.Cards?.Card?
            .Where(c => c.Details?.CardDetails != null)
            .SelectMany(c => c.Details.CardDetails)
            .ToList();

        if (envelope == null || entries == null || entries.Count == 0)
            return new(logId, LogRetryStatus.Skipped, "Stored payload contains no work card entries.");

        // A scan that is still queued locally is sent by the background service on its own.
        // Retrying it here as well could deliver the same card to Ergani twice.
        if (log.EmployeeId is int queuedEmployeeId)
        {
            var scannedAt = entries[0].MovementDateTime.DateTime;
            var from = scannedAt.AddSeconds(-1);
            var to   = scannedAt.AddSeconds(1);

            using var cache = LocalCacheDbContextFactory.Create();
            var stillQueued = await cache.PendingSubmissions.AnyAsync(p =>
                p.EmployeeId == queuedEmployeeId && !p.Synced &&
                p.ScannedAt >= from && p.ScannedAt <= to, ct);

            if (stillQueued)
                return new(logId, LogRetryStatus.Skipped,
                    "Still queued — the background sender will retry it automatically.");
        }

        // 10-minute rule, measured from the FIRST attempt (SubmissionDate is never modified
        // by retries). Stored as UTC; providers return it with Kind=Unspecified.
        var firstAttemptUtc = DateTime.SpecifyKind(log.SubmissionDate, DateTimeKind.Utc);
        var sinceFirstAttempt = DateTime.UtcNow - firstAttemptUtc;
        var isLate = sinceFirstAttempt > OnTimeWindow;

        var justification = isLate ? lateCode : string.Empty;
        foreach (var entry in entries)
            entry.LateDeclarationJustification = justification;

        _logger.LogInformation(
            "Retry of log entry {LogId}: first attempt {Minutes:F0} min ago → f_aitiologia '{Code}'.",
            logId, sinceFirstAttempt.TotalMinutes, justification);

        var company = await db.Companies.FindAsync(new object[] { log.CompanyId }, ct);
        if (company == null)
            return new(logId, LogRetryStatus.Skipped, "Company no longer exists.");

        var credentials = new ErganiCredentials
        {
            Username = company.ErganiUsername,
            Password = _credentialProtector.Unprotect(company.ErganiPasswordEncrypted),
            Usertype = company.ErganiUsertype,
            BaseUrl  = company.ErganiBaseUrl
        };

        Notify(logId, LogRetryStatus.Failed, "Retrying...", log.RetryCount, log.Protocol);

        ErganiCallResult<List<ErganiSubmissionResponse>> call;
        try
        {
            call = await _erganiClient.SubmitWorkCardAsync(credentials, envelope, ct);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // e.g. authentication failure — still counts as an attempt on this row.
            _logger.LogWarning(ex, "Retry of log entry {LogId} threw.", logId);
            log.RetryCount++;
            log.LastRetryAt = DateTime.UtcNow;
            log.ErrorMessage = ex.Message;
            await db.SaveChangesAsync(ct);
            Notify(logId, LogRetryStatus.Failed, ex.Message, log.RetryCount, log.Protocol);
            return new(logId, LogRetryStatus.Failed, ex.Message);
        }

        var first = call.Data?.FirstOrDefault();
        var ok = call.Success && first is not { IsBusinessError: true };

        // ── Update the SAME row ───────────────────────────────────────────
        log.RetryCount++;
        log.LastRetryAt = DateTime.UtcNow;
        log.RequestPayloadJson = call.RequestPayloadJson;   // what was actually sent this time
        log.ResponseRawJson = call.ResponseRawJson;
        log.HttpStatusCode = call.HttpStatusCode;
        log.DurationMs = call.DurationMs;
        log.Success = ok;
        log.ErrorMessage = ok ? null : (call.ErrorMessage ?? first?.Description ?? "Retry failed.");

        if (ok)
        {
            log.SubmissionId = first?.SubmissionId;
            log.Protocol = first?.Protocol;
            await MarkWorkCardsSubmittedAsync(db, log, entries, first, call, ct);
        }

        await db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Retry #{Retry} of log entry {LogId}: {Outcome}.",
            log.RetryCount, logId, ok ? "succeeded" : "failed");

        Notify(logId, ok ? LogRetryStatus.Succeeded : LogRetryStatus.Failed,
            log.ErrorMessage, log.RetryCount, log.Protocol);

        return new(logId, ok ? LogRetryStatus.Succeeded : LogRetryStatus.Failed, log.ErrorMessage);
    }

    private static async Task MarkWorkCardsSubmittedAsync(
        AppDbContext db,
        ErganiManager.Data.Entities.ApiSubmissionLog log,
        List<WorkCardEntry> entries,
        ErganiSubmissionResponse? response,
        ErganiCallResult<List<ErganiSubmissionResponse>> call,
        CancellationToken ct)
    {
        if (log.EmployeeId is not int employeeId) return;

        foreach (var entry in entries)
        {
            var movementType = entry.MovementType == WorkCardMovementTypeCodes.Departure
                ? ErganiManager.Data.Entities.MovementType.Departure
                : ErganiManager.Data.Entities.MovementType.Arrival;

            // Clock time as originally scanned (local); ±1s tolerance for timestamp rounding.
            var scanned = entry.MovementDateTime.DateTime;
            var from = scanned.AddSeconds(-1);
            var to   = scanned.AddSeconds(1);

            var workCard = await db.WorkCards.FirstOrDefaultAsync(w =>
                w.EmployeeId == employeeId &&
                w.MovementType == movementType &&
                w.MovementDateTime >= from &&
                w.MovementDateTime <= to &&
                !w.SubmittedToErgani, ct);

            if (workCard == null) continue;

            workCard.SubmittedToErgani = true;
            workCard.Protocol = response?.Protocol;
            workCard.SubmissionId = response?.SubmissionId;
            workCard.ResponseRawJson = call.ResponseRawJson;
        }
    }
}
