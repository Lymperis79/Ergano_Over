using ErganiManager.Core.Interfaces;
using ErganiManager.Data.Entities;
using ErganiManager.LocalCache;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ErganiManager.Core.Services;

/// <summary>
/// Evaluates the two configurable short-interval attendance warnings for every client.
/// Warnings are informational: they never prevent a scan from being queued or submitted.
/// All lookups use the durable local outbox so terminal scans never wait on the main database.
/// </summary>
public sealed class WorkCardIntervalAlertService
{
    private readonly IEmailAlertService _alerts;
    private readonly ILogger<WorkCardIntervalAlertService> _logger;

    public WorkCardIntervalAlertService(
        IEmailAlertService alerts,
        ILogger<WorkCardIntervalAlertService> logger)
    {
        _alerts = alerts;
        _logger = logger;
    }

    public async Task<string?> EvaluateAndAlertAsync(
        int companyId, int employeeId, string employeeName, string taxId,
        string movement, DateTime scannedAt)
    {
        try
        {
            using var cache = LocalCacheDbContextFactory.Create();
            var settings = await cache.CachedCompanySettings.FindAsync(companyId);
            if (settings == null) return null;

            var enabled = movement == "Departure"
                ? settings.ShortClockInOutAlertEnabled
                : movement == "Arrival" && settings.RepeatedClockInAlertEnabled;
            if (!enabled) return null;

            var threshold = movement == "Departure"
                ? settings.ShortClockInOutAlertMinutes
                : settings.RepeatedClockInAlertMinutes;
            if (threshold <= 0) return null;

            // The local outbox retains acknowledged rows as an audit trail, so this works
            // both online and offline and does not need a potentially slow main-DB query.
            var history = await cache.PendingSubmissions.AsNoTracking()
                .Where(p => p.CompanyId == companyId && p.EmployeeId == employeeId && p.ScannedAt < scannedAt)
                .OrderByDescending(p => p.ScannedAt)
                .Take(20)
                .Select(p => new { p.ScannedAt, p.MovementType })
                .ToListAsync();

            var previous = history.FirstOrDefault();
            if (previous == null) return null;

            var previousMovement = previous.MovementType;
            var isShortClockInOut = movement == "Departure" && previousMovement == "Arrival";
            var isRepeatedArrival = movement == "Arrival" && previousMovement == "Arrival";
            if (!isShortClockInOut && !isRepeatedArrival) return null;

            var elapsed = scannedAt - previous.ScannedAt;
            // The clients already reject accidental duplicate scans inside 20 seconds.
            // Do not emit an interval warning for a scan that will be rejected by that guard.
            if (elapsed.TotalSeconds < 20 || elapsed.TotalMinutes >= threshold) return null;

            var eventType = isShortClockInOut
                ? AlertEvent.ShortClockInOutInterval
                : AlertEvent.RepeatedClockIn;
            var ruleName = isShortClockInOut
                ? "Arrival followed by departure too quickly"
                : "Two consecutive arrivals too close together";
            var companyName = string.IsNullOrWhiteSpace(settings.CompanyName)
                ? $"Company #{companyId}" : settings.CompanyName;
            var warning = $"{ruleName}: {employeeName}, {previous.ScannedAt:HH:mm:ss} → {scannedAt:HH:mm:ss} " +
                          $"({elapsed.TotalMinutes:0.0} minutes; configured minimum {threshold} minutes). " +
                          "The scan will still be submitted to Ergani.";

            var alert = new AlertMessage
            {
                Event = eventType,
                Title = $"{ruleName} — {employeeName}",
                OccurredAt = scannedAt
            }
            .Add("Company", companyName)
            .Add("Employee", $"{employeeName} (AFM: {taxId})")
            .Add("Previous movement", $"{previousMovement} at {previous.ScannedAt:yyyy-MM-dd HH:mm:ss}")
            .Add("Current movement", $"{movement} at {scannedAt:yyyy-MM-dd HH:mm:ss}")
            .Add("Elapsed time", $"{elapsed.TotalMinutes:0.0} minutes")
            .Add("Configured threshold", $"{threshold} minutes")
            .Add("Action", "Warning only — attendance submission continues.");

            // Alert delivery is asynchronous and independent of the attendance submission.
            // The alert service persists in-app notifications and queues retryable emails.
            _ = _alerts.SendAlertAsync(companyId, alert);
            return warning;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Attendance interval alert check failed for company {CompanyId}, employee {EmployeeId}.", companyId, employeeId);
            return null;
        }
    }
}
