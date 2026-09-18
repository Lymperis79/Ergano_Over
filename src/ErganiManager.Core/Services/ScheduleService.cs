using ErganiManager.Core.Interfaces;
using ErganiManager.Data;
using ErganiManager.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace ErganiManager.Core.Services;

public class ScheduleService : IScheduleService
{
    private readonly IConnectionStateService _connectionState;

    public ScheduleService(IConnectionStateService connectionState)
    {
        _connectionState = connectionState;
    }

    private AppDbContext OpenDb()
        => new AppDbContext(_connectionState.GetDbOptions());

    public async Task<List<ScheduleDayDto>> GetMonthAsync(int employeeId, int year, int month)
    {
        await using var db = OpenDb();

        var firstDay = new DateOnly(year, month, 1);
        var lastDay = firstDay.AddMonths(1).AddDays(-1);

        var schedules = await db.Schedules
            .Where(s => s.EmployeeId == employeeId && s.ScheduleDate >= firstDay && s.ScheduleDate <= lastDay)
            .ToListAsync();

        var workCards = await db.WorkCards
            .Where(w => w.EmployeeId == employeeId
                        && w.MovementDateTime >= firstDay.ToDateTime(TimeOnly.MinValue)
                        && w.MovementDateTime < lastDay.AddDays(1).ToDateTime(TimeOnly.MinValue))
            .ToListAsync();

        var workCardsByDate = workCards
            .GroupBy(w => DateOnly.FromDateTime(w.MovementDateTime))
            .ToDictionary(g => g.Key, g => g.ToList());

        return schedules.Select(s => ToDto(s, workCardsByDate.GetValueOrDefault(s.ScheduleDate))).ToList();
    }

    public async Task<ScheduleDayDto?> GetByDateAsync(int employeeId, DateOnly date)
    {
        await using var db = OpenDb();

        var schedule = await db.Schedules
            .FirstOrDefaultAsync(s => s.EmployeeId == employeeId && s.ScheduleDate == date);

        var dayCards = await db.WorkCards
            .Where(w => w.EmployeeId == employeeId
                        && w.MovementDateTime >= date.ToDateTime(TimeOnly.MinValue)
                        && w.MovementDateTime < date.AddDays(1).ToDateTime(TimeOnly.MinValue))
            .ToListAsync();

        if (schedule == null)
        {
            if (dayCards.Count == 0)
                return null;

            return new ScheduleDayDto
            {
                EmployeeId = employeeId,
                ScheduleDate = date,
                ActualArrival = dayCards.FirstOrDefault(w => w.MovementType == MovementType.Arrival)?.MovementDateTime,
                ActualDeparture = dayCards.FirstOrDefault(w => w.MovementType == MovementType.Departure)?.MovementDateTime
            };
        }

        return ToDto(schedule, dayCards);
    }

    public async Task<int> UpsertDayAsync(ScheduleDayDto dto)
    {
        if (dto.WorkType is AppWorkType.Office or AppWorkType.Home)
        {
            if (dto.StartTime == null || dto.EndTime == null)
                throw new InvalidOperationException("Start and end time are required for Office/Home work days.");

            if (dto.EndTime <= dto.StartTime)
                throw new InvalidOperationException("End time must be after start time.");
        }

        await using var db = OpenDb();

        var existing = await db.Schedules
            .FirstOrDefaultAsync(s => s.EmployeeId == dto.EmployeeId && s.ScheduleDate == dto.ScheduleDate);

        if (existing == null)
        {
            var entity = new EmployeeSchedule
            {
                EmployeeId = dto.EmployeeId,
                BranchId = dto.BranchId,
                ScheduleDate = dto.ScheduleDate,
                WorkType = ToEntityWorkType(dto.WorkType),
                StartTime = dto.StartTime,
                EndTime = dto.EndTime,
                Comments = dto.Comments,
                CreatedAt = DateTime.UtcNow
            };
            db.Schedules.Add(entity);
            await db.SaveChangesAsync();
            return entity.Id;
        }

        existing.BranchId = dto.BranchId;
        existing.WorkType = ToEntityWorkType(dto.WorkType);
        existing.StartTime = dto.StartTime;
        existing.EndTime = dto.EndTime;
        existing.Comments = dto.Comments;
        existing.UpdatedAt = DateTime.UtcNow;

        if (existing.SubmittedToErgani)
        {
            existing.SubmittedToErgani = false;
            existing.SubmissionId = null;
            existing.Protocol = null;
        }

        await db.SaveChangesAsync();
        return existing.Id;
    }

    public async Task DeleteDayAsync(int scheduleId)
    {
        await using var db = OpenDb();
        var entity = await db.Schedules.FindAsync(scheduleId);
        if (entity != null)
        {
            db.Schedules.Remove(entity);
            await db.SaveChangesAsync();
        }
    }

    public async Task<int> BulkSetAsync(
        int employeeId, int branchId, DateOnly fromDate, DateOnly toDate,
        AppWorkType workType, TimeOnly? startTime, TimeOnly? endTime, IReadOnlySet<DayOfWeek>? onlyDaysOfWeek = null)
    {
        if (toDate < fromDate)
            throw new InvalidOperationException("End date must be on or after start date.");

        if (workType is AppWorkType.Office or AppWorkType.Home && (startTime == null || endTime == null))
            throw new InvalidOperationException("Start and end time are required for Office/Home work days.");

        await using var db = OpenDb();

        var existingInRange = await db.Schedules
            .Where(s => s.EmployeeId == employeeId && s.ScheduleDate >= fromDate && s.ScheduleDate <= toDate)
            .ToListAsync();
        var existingByDate = existingInRange.ToDictionary(s => s.ScheduleDate);

        int count = 0;
        for (var date = fromDate; date <= toDate; date = date.AddDays(1))
        {
            if (onlyDaysOfWeek != null && !onlyDaysOfWeek.Contains(date.DayOfWeek))
                continue;

            if (existingByDate.TryGetValue(date, out var existing))
            {
                existing.BranchId = branchId;
                existing.WorkType = ToEntityWorkType(workType);
                existing.StartTime = startTime;
                existing.EndTime = endTime;
                existing.UpdatedAt = DateTime.UtcNow;
                if (existing.SubmittedToErgani)
                {
                    existing.SubmittedToErgani = false;
                    existing.SubmissionId = null;
                    existing.Protocol = null;
                }
            }
            else
            {
                db.Schedules.Add(new EmployeeSchedule
                {
                    EmployeeId = employeeId,
                    BranchId = branchId,
                    ScheduleDate = date,
                    WorkType = ToEntityWorkType(workType),
                    StartTime = startTime,
                    EndTime = endTime,
                    CreatedAt = DateTime.UtcNow
                });
            }
            count++;
        }

        await db.SaveChangesAsync();
        return count;
    }

    private static WorkType ToEntityWorkType(AppWorkType t) => t switch
    {
        AppWorkType.Office => Data.Entities.WorkType.Office,
        AppWorkType.Home => Data.Entities.WorkType.Home,
        AppWorkType.Rest => Data.Entities.WorkType.Rest,
        AppWorkType.Absent => Data.Entities.WorkType.Absent,
        _ => throw new ArgumentOutOfRangeException(nameof(t))
    };

    private static AppWorkType ToAppWorkType(WorkType t) => t switch
    {
        Data.Entities.WorkType.Office => AppWorkType.Office,
        Data.Entities.WorkType.Home => AppWorkType.Home,
        Data.Entities.WorkType.Rest => AppWorkType.Rest,
        Data.Entities.WorkType.Absent => AppWorkType.Absent,
        _ => throw new ArgumentOutOfRangeException(nameof(t))
    };

    private static ScheduleDayDto ToDto(EmployeeSchedule s, List<WorkCard>? dayCards) => new()
    {
        Id = s.Id,
        EmployeeId = s.EmployeeId,
        BranchId = s.BranchId,
        ScheduleDate = s.ScheduleDate,
        WorkType = ToAppWorkType(s.WorkType),
        StartTime = s.StartTime,
        EndTime = s.EndTime,
        SubmittedToErgani = s.SubmittedToErgani,
        Protocol = s.Protocol,
        Comments = s.Comments,
        ActualArrival = dayCards?.FirstOrDefault(w => w.MovementType == MovementType.Arrival)?.MovementDateTime,
        ActualDeparture = dayCards?.FirstOrDefault(w => w.MovementType == MovementType.Departure)?.MovementDateTime
    };

    public async Task<int> CopyToEmployeesAsync(
        int sourceEmployeeId,
        DateOnly fromDate,
        DateOnly toDate,
        IReadOnlyList<int> targetEmployeeIds)
    {
        await using var db = OpenDb();

        // Load source schedule for the period
        var sourceDays = await db.Schedules
            .Where(s => s.EmployeeId == sourceEmployeeId
                     && s.ScheduleDate >= fromDate
                     && s.ScheduleDate <= toDate)
            .ToListAsync();

        if (sourceDays.Count == 0 || targetEmployeeIds.Count == 0)
            return 0;

        int written = 0;

        foreach (var targetId in targetEmployeeIds)
        {
            // Remove existing entries for this employee in the range
            var existing = await db.Schedules
                .Where(s => s.EmployeeId == targetId
                         && s.ScheduleDate >= fromDate
                         && s.ScheduleDate <= toDate)
                .ToListAsync();
            db.Schedules.RemoveRange(existing);

            // Copy source days to target employee
            foreach (var src in sourceDays)
            {
                db.Schedules.Add(new EmployeeSchedule
                {
                    EmployeeId       = targetId,
                    BranchId         = src.BranchId,
                    ScheduleDate     = src.ScheduleDate,
                    WorkType         = src.WorkType,
                    StartTime        = src.StartTime,
                    EndTime          = src.EndTime,
                    Comments         = src.Comments,
                    SubmittedToErgani = false,
                    CreatedAt        = DateTime.UtcNow
                });
                written++;
            }
        }

        await db.SaveChangesAsync();
        return written;
    }
}
