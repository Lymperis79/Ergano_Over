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
    private readonly ILogger<CacheSyncService> _logger;

    public CacheSyncService(
        IConnectionStateService connectionState,
        IWorkCardSubmitter workCardSubmitter,
        ILogger<CacheSyncService> logger)
    {
        _connectionState = connectionState;
        _workCardSubmitter = workCardSubmitter;
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

            cachedSettings.EarlyClockInBlockMinutes = company.EarlyClockInBlockMinutes;
            cachedSettings.EarlyDepartureAlertMinutes = company.EarlyDepartureAlertMinutes;
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

        // Keep this secondary flush path subject to the same chronology safeguard as
        // the retry worker: a protocol on the same employee/workday pauses unresolved scans.
        if (pending.Count > 0)
        {
            var employeeIdsForReview = pending.Select(p => p.EmployeeId).Distinct().ToList();
            var minReviewDate = pending.Min(p => p.ScannedAt.Date);
            var maxReviewDate = pending.Max(p => p.ScannedAt.Date).AddDays(1);
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
                .Select(p => new { p.EmployeeId, p.ScannedAt })
                .ToListAsync();
            var protocolDays = acknowledged
                .Select(w => (w.EmployeeId, Day: w.MovementDateTime.Date))
                .Concat(localAcknowledged.Select(w => (w.EmployeeId, Day: w.ScannedAt.Date)))
                .ToHashSet();
            foreach (var item in pending)
            {
                if (protocolDays.Contains((item.EmployeeId, item.ScannedAt.Date)))
                {
                    item.ReviewRequired = true;
                    item.ReviewReason = "Another attendance event for this employee has an Ergani protocol on the same workday. Check the Ergani portal and API response before deciding whether to retry or discard this unsubmitted record.";
                }
            }
            if (protocolDays.Count > 0)
                await cache.SaveChangesAsync();
        }

        var result = new SyncResult { Success = true };
        result.PendingSubmissionsRemaining += pending.Count(p => p.ReviewRequired);

        foreach (var item in pending.Where(p => !p.ReviewRequired))
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
