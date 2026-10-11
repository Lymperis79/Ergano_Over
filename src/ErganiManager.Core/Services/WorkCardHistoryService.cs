using ErganiManager.Core.Interfaces;
using ErganiManager.Data;
using Microsoft.EntityFrameworkCore;

namespace ErganiManager.Core.Services;

public class WorkCardHistoryService : IWorkCardHistoryService
{
    private readonly IConnectionStateService _connectionState;

    public WorkCardHistoryService(IConnectionStateService connectionState)
    {
        _connectionState = connectionState;
    }

    private AppDbContext OpenDb()
        => new AppDbContext(_connectionState.GetDbOptions());

    public async Task<List<WorkCardHistoryDto>> GetAsync(int companyId, WorkCardHistoryFilter filter)
    {
        await using var db = OpenDb();

        var fromDt = filter.FromDate?.ToDateTime(TimeOnly.MinValue) ?? DateTime.MinValue;
        var toDt = filter.ToDate?.ToDateTime(TimeOnly.MaxValue) ?? DateTime.MaxValue;

        var query = db.WorkCards
            .Include(w => w.Employee)
            .Include(w => w.Branch)
            .Where(w => w.Employee != null && w.Employee.CompanyId == companyId)
            .Where(w => w.MovementDateTime >= fromDt && w.MovementDateTime <= toDt);

        if (filter.EmployeeId.HasValue)
            query = query.Where(w => w.EmployeeId == filter.EmployeeId.Value);

        if (filter.BranchId.HasValue)
            query = query.Where(w => w.BranchId == filter.BranchId.Value);

        if (!string.IsNullOrEmpty(filter.MovementType))
            query = query.Where(w => w.MovementType.ToString() == filter.MovementType);

        if (filter.EarlyDepartureOnly == true)
            query = query.Where(w => w.WasEarlyDeparture);

        var results = await query
            .OrderByDescending(w => w.MovementDateTime)
            .Take(1000)
            .ToListAsync();

        var records = results.Select(w => new WorkCardHistoryDto
        {
            Id = w.Id,
            EmployeeId = w.EmployeeId,
            EmployeeFullName = w.Employee?.FullName ?? "Unknown",
            EmployeeTaxId = w.Employee?.TaxId ?? "",
            BranchName = w.Branch?.Name ?? w.Branch?.Address ?? "",
            MovementType = w.MovementType.ToString(),
            MovementDateTime = w.MovementDateTime,
            SubmittedToErgani = w.SubmittedToErgani,
            Protocol = w.Protocol,
            SubmissionId = w.SubmissionId,
            ResponseRawJson = w.ResponseRawJson,
            SubmissionDate = w.SubmissionDate,
            WasEarlyDeparture = w.WasEarlyDeparture,
            EarlyDepartureMinutes = w.EarlyDepartureMinutes,
            EmailAlertSent = w.EmailAlertSent,
            CreatedAt = w.CreatedAt
        }).ToList();

        // The scanner intentionally saves first to the local cache. Until the
        // background sender succeeds there is no WorkCard row to show above.
        // Include those records here so History is also the operator's view of
        // scans waiting for Ergani.
        using var cache = ErganiManager.LocalCache.LocalCacheDbContextFactory.Create();

        var pending = await cache.PendingSubmissions
            .Where(p => p.CompanyId == companyId && !p.Synced
                        && p.ScannedAt >= fromDt && p.ScannedAt <= toDt)
            .OrderByDescending(p => p.ScannedAt)
            .ToListAsync();

        var failed = await cache.FailedSubmissions
            .Where(f => f.CompanyId == companyId && !f.Resolved
                        && f.OriginalScannedAt >= fromDt && f.OriginalScannedAt <= toDt)
            .OrderByDescending(f => f.OriginalScannedAt)
            .ToListAsync();

        var employeeIds = pending.Select(p => p.EmployeeId)
            .Concat(failed.Select(f => f.EmployeeId))
            .Distinct()
            .ToList();

        if (employeeIds.Count > 0)
        {
            var employees = await db.Employees
                .Where(e => employeeIds.Contains(e.Id) && e.CompanyId == companyId)
                .ToDictionaryAsync(e => e.Id);

            var branchIds = pending.Select(p => p.BranchId)
                .Concat(failed.Select(f => f.BranchId))
                .Distinct()
                .ToList();

            var branches = await db.Branches
                .Where(b => branchIds.Contains(b.Id))
                .ToDictionaryAsync(b => b.Id);

            bool Matches(int employeeId, int branchId, string movement)
            {
                if (filter.EmployeeId.HasValue && employeeId != filter.EmployeeId.Value)
                    return false;
                if (filter.BranchId.HasValue && branchId != filter.BranchId.Value)
                    return false;
                if (!string.IsNullOrEmpty(filter.MovementType) && movement != filter.MovementType)
                    return false;
                return true;
            }

            foreach (var p in pending)
            {
                if (!employees.TryGetValue(p.EmployeeId, out var employee))
                    continue;
                if (!Matches(p.EmployeeId, p.BranchId, p.MovementType))
                    continue;

                // Avoid a duplicate if a WorkCard was created between the cache
                // query and this query. The main WorkCard is the authoritative row.
                if (records.Any(r => r.EmployeeId == p.EmployeeId &&
                                     r.MovementType == p.MovementType &&
                                     Math.Abs((r.MovementDateTime - p.ScannedAt).TotalSeconds) <= 1))
                    continue;

                records.Add(new WorkCardHistoryDto
                {
                    Id = 0,
                    EmployeeId = p.EmployeeId,
                    EmployeeFullName = employee.FullName,
                    EmployeeTaxId = employee.TaxId,
                    BranchName = branches.TryGetValue(p.BranchId, out var branch) ? (branch.Name ?? branch.Address ?? "") : "",
                    MovementType = p.MovementType,
                    MovementDateTime = p.ScannedAt,
                    SubmittedToErgani = false,
                    SubmissionDate = DateOnly.FromDateTime(p.ScannedAt.Date),
                    CreatedAt = p.ScannedAt,
                    LocalPendingId = p.Id,
                    IsLocalPending = true,
                    RetryAttempts = p.SyncAttempts,
                    RetryError = p.LastSyncError
                });
            }

            foreach (var f in failed)
            {
                if (!employees.TryGetValue(f.EmployeeId, out var employee))
                    continue;
                if (!Matches(f.EmployeeId, f.BranchId, f.MovementType))
                    continue;

                if (records.Any(r => r.EmployeeId == f.EmployeeId &&
                                     r.MovementType == f.MovementType &&
                                     Math.Abs((r.MovementDateTime - f.OriginalScannedAt).TotalSeconds) <= 1))
                    continue;

                records.Add(new WorkCardHistoryDto
                {
                    Id = 0,
                    EmployeeId = f.EmployeeId,
                    EmployeeFullName = employee.FullName,
                    EmployeeTaxId = employee.TaxId,
                    BranchName = branches.TryGetValue(f.BranchId, out var branch) ? (branch.Name ?? branch.Address ?? "") : "",
                    MovementType = f.MovementType,
                    MovementDateTime = f.OriginalScannedAt,
                    SubmittedToErgani = false,
                    SubmissionDate = DateOnly.FromDateTime(f.OriginalScannedAt.Date),
                    CreatedAt = f.CreatedAt,
                    LocalFailedId = f.Id,
                    IsLocalFailed = true,
                    RetryAttempts = f.RetryCount,
                    RetryError = f.LastRetryError ?? f.ErrorDescription
                });
            }
        }

        return records
            .OrderByDescending(r => r.MovementDateTime)
            .Take(1000)
            .ToList();
    }
}
