using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using ErganiManager.Core.Interfaces;
using ErganiManager.LocalCache;
using Microsoft.EntityFrameworkCore;

namespace ErganiManager.Core.Services;

/// <summary>One unresolved attendance event that has been paused for chronology review.</summary>
public sealed record ChronologyReviewAlertItem(
    int CompanyId, int EmployeeId, DateTime ScannedAt, string MovementType);

/// <summary>
/// Sends one consolidated, durable alert for each employee/workday whose unresolved scans
/// were newly paused because another attendance event already has an Ergani protocol.
/// </summary>
public static class ChronologyReviewAlert
{
    public static async Task SendAsync(
        IEmailAlertService alerts,
        LocalCacheDbContext cache,
        IReadOnlyDictionary<int, string> companyNames,
        IEnumerable<ChronologyReviewAlertItem> items)
    {
        try
        {
            var grouped = items
                .GroupBy(x => new { x.CompanyId, x.EmployeeId, Day = x.ScannedAt.Date })
                .ToList();
            if (grouped.Count == 0) return;

            var employeeIds = grouped.Select(x => x.Key.EmployeeId).Distinct().ToList();
            var employees = await cache.CachedEmployees.AsNoTracking()
                .Where(x => employeeIds.Contains(x.Id))
                .ToDictionaryAsync(x => x.Id);

            foreach (var group in grouped)
            {
            var employee = employees.GetValueOrDefault(group.Key.EmployeeId);
            var employeeName = employee?.FullName ?? $"#{group.Key.EmployeeId}";
            var employeeIdentity = employee == null || string.IsNullOrWhiteSpace(employee.TaxId)
                ? employeeName
                : $"{employeeName} (AFM: {employee.TaxId})";
            var pausedScans = string.Join(Environment.NewLine,
                group.OrderBy(x => x.ScannedAt)
                    .Select(x => $"{x.ScannedAt:HH:mm:ss} — {ErganiManager.Data.UiText.T(x.MovementType)}"));
            var companyName = companyNames.TryGetValue(group.Key.CompanyId, out var name)
                ? name
                : $"#{group.Key.CompanyId}";

            var alert = new AlertMessage
            {
                Event = AlertEvent.ChronologyReview,
                Title = ErganiManager.Data.UiText.T("Attendance chronology requires administrator review"),
                OccurredAt = DateTime.Now
            }
            .Add("Company", companyName)
            .Add("Employee", employeeIdentity)
            .Add("Workday", group.Key.Day.ToString("dd/MM/yyyy"))
            .Add("Paused scans", pausedScans)
            .Add("Reason", ErganiManager.Data.UiText.T(
                "Another attendance event for this employee has an Ergani protocol on the same workday."))
            .Add("Action required", ErganiManager.Data.UiText.T(
                "Check the Ergani portal and API response. Decide manually whether each paused scan should be retried or marked as not submitted. Protocol-bearing records are protected and must not be deleted or resubmitted."));

                await alerts.SendAlertAsync(group.Key.CompanyId, alert);
            }
        }
        catch (Exception ex)
        {
            // An alert must never turn a safely queued/review-paused scan into an HTTP or sync failure.
            Trace.TraceError("Could not create chronology review alert: " + ex);
        }
    }
}
