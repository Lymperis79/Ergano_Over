using ErganiManager.Core.Interfaces;
using ErganiManager.Core.Models;
using ErganiManager.Data;
using ErganiManager.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace ErganiManager.Core.Services;

public class LeaveService : ILeaveService
{
    private readonly IConnectionStateService _connectionState;

    public LeaveService(IConnectionStateService connectionState)
    {
        _connectionState = connectionState;
    }

    private AppDbContext OpenDb() => new AppDbContext(_connectionState.GetDbOptions());

    public async Task<List<LeaveDto>> GetByEmployeeAsync(int companyId, int employeeId, int take = 300)
    {
        await using var db = OpenDb();

        var rows = await db.Leaves
            .Where(l => l.EmployeeId == employeeId
                     && l.Employee != null && l.Employee.CompanyId == companyId)
            .OrderByDescending(l => l.LeaveDate)
            .ThenByDescending(l => l.Id)
            .Take(take)
            .ToListAsync();

        return rows.Select(ToDto).ToList();
    }

    public async Task<List<LeaveDto>> GetByEmployeeDateRangeAsync(
        int companyId, int employeeId, DateOnly from, DateOnly to)
    {
        await using var db = OpenDb();

        var rows = await db.Leaves
            .Where(l => l.EmployeeId == employeeId
                     && l.LeaveDate >= from && l.LeaveDate <= to
                     && l.Employee != null && l.Employee.CompanyId == companyId)
            .OrderBy(l => l.LeaveDate)
            .ThenBy(l => l.Id)
            .ToListAsync();

        return rows.Select(ToDto).ToList();
    }

    public async Task<LeaveCreateResult> CreateRangeAsync(
        LeaveDto template, DateOnly from, DateOnly to, bool skipWeekends)
    {
        if (to < from)
            throw new InvalidOperationException("The end date must not be before the start date.");
        if ((to.DayNumber - from.DayNumber) > 366)
            throw new InvalidOperationException("A holiday can cover at most one year at a time.");

        var type = LeaveTypes.Find(template.LeaveTypeCode)
            ?? throw new InvalidOperationException("Select a holiday type.");

        if (type.IsHourly)
        {
            if (!template.StartTime.HasValue || !template.EndTime.HasValue)
                throw new InvalidOperationException("This holiday type is hourly — enter the start and end time.");
            if (template.EndTime <= template.StartTime)
                throw new InvalidOperationException("The end time must be after the start time.");
        }

        await using var db = OpenDb();

        var existing = await db.Leaves
            .Where(l => l.EmployeeId == template.EmployeeId && l.LeaveDate >= from && l.LeaveDate <= to)
            .ToListAsync();

        var result = new LeaveCreateResult();
        var created = new List<Leave>();

        for (var day = from; day <= to; day = day.AddDays(1))
        {
            if (skipWeekends && day.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) continue;

            var start = type.IsHourly ? template.StartTime : null;
            var end   = type.IsHourly ? template.EndTime   : null;

            var duplicate = existing.Any(l =>
                l.LeaveDate == day &&
                string.Equals(l.LeaveTypeCode, type.Code, StringComparison.OrdinalIgnoreCase) &&
                l.StartTime == start && l.EndTime == end);
            if (duplicate) { result.Duplicates++; continue; }

            created.Add(new Leave
            {
                EmployeeId    = template.EmployeeId,
                BranchId      = template.BranchId,
                LeaveDate     = day,
                LeaveTypeCode = type.Code,
                StartTime     = start,
                EndTime       = end,
                ReferenceYear = template.ReferenceYear,
                EntitledDays  = template.EntitledDays,
                Comments      = string.IsNullOrWhiteSpace(template.Comments) ? null : template.Comments.Trim(),
                SubmittedToErgani = template.SubmittedToErgani, // true for leave imported from Ergani
                CreatedAt     = DateTime.UtcNow
            });
        }

        if (created.Count > 0)
        {
            db.Leaves.AddRange(created);
            await db.SaveChangesAsync();
            result.Created = created.Count;
            result.CreatedIds.AddRange(created.Select(c => c.Id));
        }

        return result;
    }

    public async Task DeleteAsync(int id)
    {
        await using var db = OpenDb();
        var entity = await db.Leaves.FindAsync(id);
        if (entity == null) return;
        db.Leaves.Remove(entity);
        await db.SaveChangesAsync();
    }

    public async Task<bool> DeleteUnsentAsync(int id)
    {
        await using var db = OpenDb();
        var entity = await db.Leaves.FindAsync(id);
        if (entity == null) return true;
        if (entity.SubmittedToErgani) return false;

        db.Leaves.Remove(entity);
        await db.SaveChangesAsync();
        return true;
    }

    private static LeaveDto ToDto(Leave l) => new()
    {
        Id                = l.Id,
        EmployeeId        = l.EmployeeId,
        BranchId          = l.BranchId,
        LeaveDate         = l.LeaveDate,
        LeaveTypeCode     = l.LeaveTypeCode,
        StartTime         = l.StartTime,
        EndTime           = l.EndTime,
        ReferenceYear     = l.ReferenceYear,
        EntitledDays      = l.EntitledDays,
        Comments          = l.Comments,
        SubmittedToErgani = l.SubmittedToErgani,
        Protocol          = l.Protocol,
        SubmissionId      = l.SubmissionId,
        ResponseRawJson   = l.ResponseRawJson,
        SubmittedDate     = l.SubmittedDate,
        LastError         = l.LastError,
        CreatedAt         = l.CreatedAt
    };
}
