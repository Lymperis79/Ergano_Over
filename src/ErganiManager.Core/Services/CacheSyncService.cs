using ErganiManager.Core.Interfaces;
using ErganiManager.Data;
using ErganiManager.LocalCache;
using ErganiManager.LocalCache.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ErganiManager.Core.Services;

public class CacheSyncService : ICacheSyncService
{
    private readonly IConnectionStateService _connectionState;
    private readonly IWorkCardSubmitter _workCardSubmitter;
    private readonly IEmailAlertService _emailAlerts;
    private readonly ILogger<CacheSyncService> _logger;

    public CacheSyncService(
        IConnectionStateService connectionState,
        IWorkCardSubmitter workCardSubmitter,
        IEmailAlertService emailAlerts,
        ILogger<CacheSyncService> logger)
    {
        _connectionState = connectionState;
        _workCardSubmitter = workCardSubmitter;
        _emailAlerts = emailAlerts;
        _logger = logger;
    }

    public async Task<SyncResult> RefreshCacheFromMainDatabaseAsync(int companyId)
    {
        if (!_connectionState.ConfigExists())
            return new SyncResult { Success = false, ErrorMessage = ErganiManager.Data.UiText.T("Database not configured.") };

        await using var db = new AppDbContext(_connectionState.GetDbOptions());
        using var cache = LocalCacheDbContextFactory.Create();

        var result = new SyncResult { Success = true };

        // ── Company time-rule settings ──────────────────────────
        var company = await db.Companies.FindAsync(companyId);
        if (company != null)
        {
            var cachedSettings = await cache.CachedCompanySettings.FindAsync(companyId);
            if (cachedSettings == null)
            {
                cachedSettings = new CachedCompanySettings { CompanyId = companyId };
                cache.CachedCompanySettings.Add(cachedSettings);
            }

            cachedSettings.CompanyName = company.Name;
            cachedSettings.EarlyClockInBlockMinutes = company.EarlyClockInBlockMinutes;
            cachedSettings.EarlyDepartureAlertMinutes = company.EarlyDepartureAlertMinutes;
            cachedSettings.ShortClockInOutAlertEnabled = company.ShortClockInOutAlertEnabled;
            cachedSettings.ShortClockInOutAlertMinutes = company.ShortClockInOutAlertMinutes;
            cachedSettings.RepeatedClockInAlertEnabled = company.RepeatedClockInAlertEnabled;
            cachedSettings.RepeatedClockInAlertMinutes = company.RepeatedClockInAlertMinutes;
            cachedSettings.BlockClockInWithoutSchedule = company.BlockClockInWithoutSchedule;
            cachedSettings.BlockClockInOnRestOrAbsentDay = company.BlockClockInOnRestOrAbsentDay;
            cachedSettings.OfficialHolidayDatesJson = string.IsNullOrWhiteSpace(company.OfficialHolidayDatesJson) ? "[]" : company.OfficialHolidayDatesJson;
            cachedSettings.AlertEvents = company.AlertEvents;
            cachedSettings.ClockInAlertMinutes = company.ClockInAlertMinutes;
            cachedSettings.LastSyncAt = DateTime.UtcNow;
        }

        // ── Employees (active only — terminal only needs to find active staff) ──
        var employees = await db.Employees
            .Where(e => e.CompanyId == companyId && e.IsActive)
            .ToListAsync();

        foreach (var emp in employees)
        {
            var cachedEmp = await cache.CachedEmployees.FindAsync(emp.Id);
            if (cachedEmp == null)
            {
                cachedEmp = new CachedEmployee { Id = emp.Id };
                cache.CachedEmployees.Add(cachedEmp);
            }

            cachedEmp.CompanyId = emp.CompanyId;
            cachedEmp.BranchId = emp.BranchId;
            cachedEmp.BarcodeId = emp.BarcodeId;
            cachedEmp.TaxId = emp.TaxId;
            cachedEmp.FullName = emp.FullName;
            cachedEmp.IsActive = emp.IsActive;
            cachedEmp.LastSyncAt = DateTime.UtcNow;
        }
        result.EmployeesSynced = employees.Count;

        // ── Schedules: yesterday (overnight shifts), today and the next 14 days, so offline checks keep working ──
        var today = DateOnly.FromDateTime(DateTime.Today);
        var horizon = today.AddDays(14);

        var employeeIds = employees.Select(e => e.Id).ToList();
        var schedules = await db.Schedules
            .Where(s => employeeIds.Contains(s.EmployeeId)
                        && s.ScheduleDate >= today.AddDays(-1)
                        && s.ScheduleDate <= horizon)
            .ToListAsync();

        foreach (var sched in schedules)
        {
            var cachedSched = await cache.CachedSchedules
                .FirstOrDefaultAsync(s => s.EmployeeId == sched.EmployeeId && s.ScheduleDate == sched.ScheduleDate);

            if (cachedSched == null)
            {
                cachedSched = new CachedSchedule
                {
                    EmployeeId = sched.EmployeeId,
                    ScheduleDate = sched.ScheduleDate
                };
                cache.CachedSchedules.Add(cachedSched);
            }

            cachedSched.StartTime = sched.StartTime;
            cachedSched.EndTime = sched.EndTime;
            cachedSched.WorkType = (int)sched.WorkType;
            cachedSched.LastSyncAt = DateTime.UtcNow;
        }
        result.SchedulesSynced = schedules.Count;

        await cache.SaveChangesAsync();

        _logger.LogInformation(
            "Cache refreshed for company {CompanyId}: {EmployeeCount} employees, {ScheduleCount} schedules.",
            companyId, result.EmployeesSynced, result.SchedulesSynced);

        return result;
    }

    public async Task<DateTime?> GetLastSuccessfulRefreshAsync(int companyId)
    {
        using var cache = LocalCacheDbContextFactory.Create();
        return await cache.CachedCompanySettings
            .AsNoTracking()
            .Where(settings => settings.CompanyId == companyId)
            .Select(settings => (DateTime?)settings.LastSyncAt)
            .FirstOrDefaultAsync();
    }

    public async Task<SyncResult> FlushPendingSubmissionsAsync()
    {
        await using var db = new AppDbContext(_connectionState.GetDbOptions());
        using var cache = LocalCacheDbContextFactory.Create();

        var pending = await cache.PendingSubmissions
            .Where(p => !p.Synced)
            .OrderBy(p => p.ScannedAt)
            .ToListAsync();

        // A saved protocol is an acknowledgement even if a previous process stopped
        // before setting Synced. Never submit that event again.
        foreach (var acknowledgedPending in pending.Where(p => !string.IsNullOrWhiteSpace(p.ErganiProtocol)))
        {
            acknowledgedPending.Synced = true;
            acknowledgedPending.SyncedAt ??= DateTime.UtcNow;
            acknowledgedPending.ProcessingUntilUtc = null;
        }
        var unresolvedPending = pending
            .Where(p => string.IsNullOrWhiteSpace(p.ErganiProtocol))
            .ToList();

        // Keep this secondary flush path subject to the same chronology safeguard as
        // the retry worker: a protocol on the same employee/workday pauses unresolved scans.
        if (unresolvedPending.Count > 0)
        {
            var employeeIdsForReview = unresolvedPending.Select(p => p.EmployeeId).Distinct().ToList();
            var minReviewDate = unresolvedPending.Min(p => p.ScannedAt.Date);
            var maxReviewDate = unresolvedPending.Max(p => p.ScannedAt.Date).AddDays(1);
            var acknowledged = await db.WorkCards
                .Where(w => employeeIdsForReview.Contains(w.EmployeeId)
                            && w.Protocol != null && w.Protocol != ""
                            && w.MovementDateTime >= minReviewDate && w.MovementDateTime < maxReviewDate)
                .Select(w => new { w.EmployeeId, w.MovementDateTime })
                .ToListAsync();
            var localAcknowledged = await cache.PendingSubmissions
                .Where(p => p.ErganiProtocol != null && p.ErganiProtocol != ""
                            && employeeIdsForReview.Contains(p.EmployeeId)
                            && p.ScannedAt >= minReviewDate && p.ScannedAt < maxReviewDate)
                .Select(p => new { p.EmployeeId, ScannedAt = p.ScannedAt })
                .ToListAsync();
            // Employee IDs are globally unique in the main database; use the
            // unresolved employee set here because this cache-sync method processes
            // pending records from all companies and has no single companyId.
            var legacyAcknowledged = await cache.FailedSubmissions
                .Where(f => f.ResolvedProtocol != null && f.ResolvedProtocol != ""
                            && employeeIdsForReview.Contains(f.EmployeeId)
                            && f.OriginalScannedAt >= minReviewDate && f.OriginalScannedAt < maxReviewDate)
                .Select(f => new { f.EmployeeId, ScannedAt = f.OriginalScannedAt })
                .ToListAsync();
            // Only a later or same-time acknowledged event can make an unresolved scan
            // unsafe to send. An earlier accepted movement must not block a later scan.
            var acknowledgedEvents = acknowledged
                .Select(w => (w.EmployeeId, At: w.MovementDateTime))
                .Concat(localAcknowledged.Select(w => (w.EmployeeId, At: w.ScannedAt)))
                .Concat(legacyAcknowledged.Select(w => (w.EmployeeId, At: w.ScannedAt)))
                .ToList();
            var newlyPaused = new List<ChronologyReviewAlertItem>();
            foreach (var item in unresolvedPending)
            {
                var hasLaterOrSameProtocol = acknowledgedEvents.Any(w =>
                    w.EmployeeId == item.EmployeeId && w.At >= item.ScannedAt);
                if (hasLaterOrSameProtocol)
                {
                    if (!item.ReviewRequired)
                        newlyPaused.Add(new ChronologyReviewAlertItem(
                            item.CompanyId, item.EmployeeId, item.ScannedAt, item.MovementType));
                    item.ReviewRequired = true;
                    item.ReviewReason = "A later or same-time attendance event for this employee already has an Ergani protocol. Check the Ergani portal and API response before deciding whether to retry or discard this earlier unsubmitted record.";
                    item.ProcessingUntilUtc = null;
                }
            }
            if (acknowledgedEvents.Count > 0)
                await cache.SaveChangesAsync();

            // This flush path can be the first one to detect the conflict (including Android
            // outbox scans), so it must raise the same admin alert as the retry worker.
            if (newlyPaused.Count > 0)
            {
                var companyIds = newlyPaused.Select(x => x.CompanyId).Distinct().ToList();
                var companyNames = await db.Companies.AsNoTracking()
                    .Where(x => companyIds.Contains(x.Id))
                    .ToDictionaryAsync(x => x.Id, x => x.Name);
                await ChronologyReviewAlert.SendAsync(_emailAlerts, cache, companyNames, newlyPaused);
            }
        }

        var result = new SyncResult { Success = true };
        result.PendingSubmissionsRemaining += unresolvedPending.Count(p => p.ReviewRequired);

        foreach (var item in unresolvedPending.Where(p => !p.ReviewRequired))
        {
            var request = new WorkCardSubmissionRequest
            {
                EmployeeId = item.EmployeeId,
                CompanyId = item.CompanyId,
                BranchId = item.BranchId,
                MovementType = item.MovementType,
                MovementDateTime = item.ScannedAt
            };

            try
            {
                var outcome = await _workCardSubmitter.SubmitAsync(request);

                item.ErganiProtocol = outcome.Protocol ?? item.ErganiProtocol;
                item.ErganiSubmissionId = outcome.SubmissionId ?? item.ErganiSubmissionId;
                item.ErganiResponseRawJson = outcome.ResponseRawJson ?? item.ErganiResponseRawJson;

                if (outcome.Success || !string.IsNullOrWhiteSpace(outcome.Protocol))
                {
                    item.Synced = true;
                    item.SyncedAt = DateTime.UtcNow;
                    result.PendingSubmissionsFlushed++;
                }
                else
                {
                    item.SyncAttempts++;
                    item.LastSyncError = outcome.ErrorMessage;
                    result.PendingSubmissionsRemaining++;
                    _logger.LogWarning(
                        "Failed to flush pending submission {Id} for employee {EmployeeId}: {Error}",
                        item.Id, item.EmployeeId, outcome.ErrorMessage);
                }
            }
            catch (Exception ex)
            {
                item.SyncAttempts++;
                item.LastSyncError = ex.Message;
                result.PendingSubmissionsRemaining++;
                _logger.LogError(ex, "Exception flushing pending submission {Id}.", item.Id);
            }
        }

        await cache.SaveChangesAsync();

        _logger.LogInformation(
            "Pending submission flush complete: {Flushed} synced, {Remaining} still pending.",
            result.PendingSubmissionsFlushed, result.PendingSubmissionsRemaining);

        return result;
    }
}
