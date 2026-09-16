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
        var config = _connectionState.LoadConfig();
        if (config == null)
            return new SyncResult { Success = false, ErrorMessage = "Database not configured." };

        var (canConnect, error) = await DbProviderFactory.TestConnectionAsync(config);
        if (!canConnect)
            return new SyncResult { Success = false, ErrorMessage = error ?? "Database unreachable." };

        var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();
        DbProviderFactory.Configure(optionsBuilder, config);

        await using var db = new AppDbContext(optionsBuilder.Options);
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
            cachedEmp.FullName = emp.FullName;
            cachedEmp.IsActive = emp.IsActive;
            cachedEmp.LastSyncAt = DateTime.UtcNow;
        }
        result.EmployeesSynced = employees.Count;

        // ── Schedules: today and the next 14 days, so offline checks keep working ──
        var today = DateOnly.FromDateTime(DateTime.Today);
        var horizon = today.AddDays(14);

        var employeeIds = employees.Select(e => e.Id).ToList();
        var schedules = await db.Schedules
            .Where(s => employeeIds.Contains(s.EmployeeId)
                        && s.ScheduleDate >= today
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
            cachedSched.LastSyncAt = DateTime.UtcNow;
        }
        result.SchedulesSynced = schedules.Count;

        await cache.SaveChangesAsync();

        _logger.LogInformation(
            "Cache refreshed for company {CompanyId}: {EmployeeCount} employees, {ScheduleCount} schedules.",
            companyId, result.EmployeesSynced, result.SchedulesSynced);

        return result;
    }

    public async Task<SyncResult> FlushPendingSubmissionsAsync()
    {
        using var cache = LocalCacheDbContextFactory.Create();

        var pending = await cache.PendingSubmissions
            .Where(p => !p.Synced)
            .OrderBy(p => p.ScannedAt)
            .ToListAsync();

        var result = new SyncResult { Success = true };

        foreach (var item in pending)
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

                if (outcome.Success)
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
