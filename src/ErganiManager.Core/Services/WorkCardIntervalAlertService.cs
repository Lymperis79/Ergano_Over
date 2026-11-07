using ErganiManager.Core.Interfaces;
using ErganiManager.Data;
using ErganiManager.Data.Entities;
using ErganiManager.LocalCache;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ErganiManager.Core.Services;

/// <summary>
/// Evaluates the two configurable short-interval attendance warnings for every client.
/// Warnings are informational: they never prevent a scan from being queued or submitted.
/// </summary>
public sealed class WorkCardIntervalAlertService
{
    private readonly IConnectionStateService _connection;
    private readonly IEmailAlertService _alerts;
    private readonly ILogger<WorkCardIntervalAlertService> _logger;

    public WorkCardIntervalAlertService(
        IConnectionStateService connection,
        IEmailAlertService alerts,
        ILogger<WorkCardIntervalAlertService> logger)
    {
        _connection = connection;
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

            var history = new List<(DateTime At, string Movement)>();
            if (_connection.ConfigExists())
            {
                try
                {
                    await using var db = new AppDbContext(_connection.GetDbOptions());
                    var rows = await db.WorkCards.AsNoTracking()
                        .Where(w => w.EmployeeId == employeeId && w.Employee != null &&
                                    w.Employee.CompanyId == companyId && w.MovementDateTime < scannedAt)
                        .OrderByDescending(w => w.MovementDateTime)
                        .Take(20)
                        .Select(w => new { w.MovementDateTime, w.MovementType })
                        .ToListAsync();
                    history.AddRange(rows.Select(w => (w.MovementDateTime,
                        w.MovementType == MovementType.Arrival ? "Arrival" : "Departure")));
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Main work-card history unavailable for interval alert; local history will be used.");
                }
            }

            var localRows = await cache.PendingSubmissions.AsNoTracking()
                .Where(p => p.CompanyId == companyId && p.EmployeeId == employeeId && p.ScannedAt < scannedAt)
                .OrderByDescending(p => p.ScannedAt)
                .Take(20)
                .Select(p => new { p.ScannedAt, p.MovementType })
                .ToListAsync();
            history.AddRange(localRows.Select(p => (p.ScannedAt, p.MovementType)));

            var previous = history
                .Where(x => x.At < scannedAt)
                .OrderByDescending(x => x.At)
                .FirstOrDefault();
            if (previous == default) return null;

            var isShortClockInOut = movement == "Departure" && previous.Movement == "Arrival";
            var isRepeatedArrival = movement == "Arrival" && previous.Movement == "Arrival";
            if (!isShortClockInOut && !isRepeatedArrival) return null;

            var elapsed = scannedAt - previous.At;
            // The clients already reject accidental duplicate scans inside 20 seconds.
            // Do not emit an interval warning for a scan that will be rejected by that guard.
            if (elapsed.TotalSeconds < 20 || elapsed.TotalMinutes >= threshold) return null;

            var eventType = isShortClockInOut
                ? AlertEvent.ShortClockInOutInterval
                : AlertEvent.RepeatedClockIn;
            var ruleName = isShortClockInOut
                ? "Arrival followed by departure too quickly"
                : "Two consecutive arrivals too close together";
            var interval = Math.Max(0, (int)Math.Floor(elapsed.TotalMinutes));
            var warning = $"{ruleName}: {employeeName}, {previous.At:HH:mm:ss} → {scannedAt:HH:mm:ss} " +
                          $"({elapsed.TotalMinutes:0.0} minutes; configured minimum {threshold} minutes). " +
                          "The scan will still be submitted to Ergani.";

            string branchName = string.Empty;
            string companyName = $"Company #{companyId}";
            if (_connection.ConfigExists())
            {
                try
                {
                    await using var db = new AppDbContext(_connection.GetDbOptions());
                    companyName = await db.Companies.AsNoTracking()
                        .Where(c => c.Id == companyId).Select(c => c.Name).FirstOrDefaultAsync() ?? companyName;
                    var branchId = await db.Employees.AsNoTracking()
                        .Where(e => e.Id == employeeId && e.CompanyId == companyId)
                        .Select(e => (int?)e.BranchId).FirstOrDefaultAsync();
                    if (branchId.HasValue)
                        branchName = await db.Branches.AsNoTracking()
                            .Where(b => b.Id == branchId.Value && b.CompanyId == companyId)
                            .Select(b => b.Name).FirstOrDefaultAsync() ?? string.Empty;
                }
                catch { /* Branch label is optional; do not affect the scan. */ }
            }

            var alert = new AlertMessage
            {
                Event = eventType,
                Title = $"{ruleName} — {employeeName}",
                OccurredAt = scannedAt
            }
            .Add("Company", companyName)
            .Add("Branch", branchName)
            .Add("Employee", $"{employeeName} (AFM: {taxId})")
            .Add("Previous movement", $"{previous.Movement} at {previous.At:yyyy-MM-dd HH:mm:ss}")
            .Add("Current movement", $"{movement} at {scannedAt:yyyy-MM-dd HH:mm:ss}")
            .Add("Elapsed time", $"{elapsed.TotalMinutes:0.0} minutes")
            .Add("Configured threshold", $"{threshold} minutes")
            .Add("Action", "Warning only — attendance submission continues.");

            // SendAlertAsync is designed to persist in-app notification independently and queue
            // retryable email failures. It never throws into the attendance path.
            await _alerts.SendAlertAsync(companyId, alert);
            return warning;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Attendance interval alert check failed for company {CompanyId}, employee {EmployeeId}.", companyId, employeeId);
            return null;
        }
    }
}
