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
            .Take(1000) // cap to prevent accidental full-table pulls
            .ToListAsync();

        return results.Select(w => new WorkCardHistoryDto
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
            WasEarlyDeparture = w.WasEarlyDeparture,
            EarlyDepartureMinutes = w.EarlyDepartureMinutes,
            EmailAlertSent = w.EmailAlertSent,
            CreatedAt = w.CreatedAt
        }).ToList();
    }
}
