using ErganiManager.Core.Interfaces;
using ErganiManager.Data;
using ErganiManager.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace ErganiManager.Core.Services;

public class OvertimeService : IOvertimeService
{
    private readonly IConnectionStateService _connectionState;

    public OvertimeService(IConnectionStateService connectionState)
    {
        _connectionState = connectionState;
    }

    private AppDbContext OpenDb()
        => new AppDbContext(_connectionState.GetDbOptions());

    public async Task<List<OvertimeDto>> GetByCompanyAsync(
        int companyId, DateOnly? fromDate = null, DateOnly? toDate = null)
    {
        await using var db = OpenDb();

        var query = db.Overtimes
            .Include(o => o.Employee)
            .Include(o => o.Branch)
            .Where(o => o.Employee != null && o.Employee.CompanyId == companyId);

        if (fromDate.HasValue) query = query.Where(o => o.OvertimeDate >= fromDate.Value);
        if (toDate.HasValue)   query = query.Where(o => o.OvertimeDate <= toDate.Value);

        var list = await query.OrderByDescending(o => o.OvertimeDate).ToListAsync();
        return list.Select(ToDto).ToList();
    }

    public async Task<int> CreateAsync(OvertimeDto dto, int companyId)
    {
        if (dto.EndTime <= dto.StartTime)
            throw new InvalidOperationException("End time must be after start time.");

        await using var db = OpenDb();

        var entity = new Overtime
        {
            EmployeeId           = dto.EmployeeId,
            BranchId             = dto.BranchId,
            OvertimeDate         = dto.OvertimeDate,
            StartTime            = dto.StartTime,
            EndTime              = dto.EndTime,
            Justification        = ToEntityJustification(dto.Justification),
            WeeklyWorkdaysNumber = dto.WeeklyWorkdaysNumber,
            AseeApproval         = dto.AseeApproval,
            IsCancelled          = false,
            SubmittedToErgani    = false,
            CreatedAt            = DateTime.UtcNow
        };

        db.Overtimes.Add(entity);
        await db.SaveChangesAsync();
        return entity.Id;
    }

    public async Task CancelAsync(int id)
    {
        await using var db = OpenDb();
        var entity = await db.Overtimes.FindAsync(id)
            ?? throw new InvalidOperationException($"Overtime record {id} not found.");
        entity.IsCancelled       = true;
        entity.SubmittedToErgani = false;
        entity.SubmissionId      = null;
        entity.Protocol          = null;
        await db.SaveChangesAsync();
    }

    public async Task DeleteAsync(int id)
    {
        await using var db = OpenDb();
        var entity = await db.Overtimes.FindAsync(id);
        if (entity != null) { db.Overtimes.Remove(entity); await db.SaveChangesAsync(); }
    }

    private static OvertimeJustification ToEntityJustification(AppOvertimeJustification j) => j switch
    {
        AppOvertimeJustification.AccidentPreventionOrDamageRestoration => OvertimeJustification.AccidentPreventionOrDamageRestoration,
        AppOvertimeJustification.UrgentSeasonalTasks                   => OvertimeJustification.UrgentSeasonalTasks,
        AppOvertimeJustification.ExceptionalWorkload                   => OvertimeJustification.ExceptionalWorkload,
        AppOvertimeJustification.SupplementaryTasks                    => OvertimeJustification.SupplementaryTasks,
        AppOvertimeJustification.LostHoursSuddenCauses                 => OvertimeJustification.LostHoursSuddenCauses,
        AppOvertimeJustification.LostHoursOfficialHolidays             => OvertimeJustification.LostHoursOfficialHolidays,
        AppOvertimeJustification.LostHoursWeatherConditions            => OvertimeJustification.LostHoursWeatherConditions,
        AppOvertimeJustification.EmergencyClosureDay                   => OvertimeJustification.EmergencyClosureDay,
        AppOvertimeJustification.NonWorkdayTasks                       => OvertimeJustification.NonWorkdayTasks,
        _ => throw new ArgumentOutOfRangeException(nameof(j))
    };

    private static AppOvertimeJustification ToAppJustification(OvertimeJustification j) => j switch
    {
        OvertimeJustification.AccidentPreventionOrDamageRestoration => AppOvertimeJustification.AccidentPreventionOrDamageRestoration,
        OvertimeJustification.UrgentSeasonalTasks                   => AppOvertimeJustification.UrgentSeasonalTasks,
        OvertimeJustification.ExceptionalWorkload                   => AppOvertimeJustification.ExceptionalWorkload,
        OvertimeJustification.SupplementaryTasks                    => AppOvertimeJustification.SupplementaryTasks,
        OvertimeJustification.LostHoursSuddenCauses                 => AppOvertimeJustification.LostHoursSuddenCauses,
        OvertimeJustification.LostHoursOfficialHolidays             => AppOvertimeJustification.LostHoursOfficialHolidays,
        OvertimeJustification.LostHoursWeatherConditions            => AppOvertimeJustification.LostHoursWeatherConditions,
        OvertimeJustification.EmergencyClosureDay                   => AppOvertimeJustification.EmergencyClosureDay,
        OvertimeJustification.NonWorkdayTasks                       => AppOvertimeJustification.NonWorkdayTasks,
        _ => throw new ArgumentOutOfRangeException(nameof(j))
    };

    private static OvertimeDto ToDto(Overtime o) => new()
    {
        Id                   = o.Id,
        EmployeeId           = o.EmployeeId,
        EmployeeFullName     = o.Employee?.FullName ?? "Unknown",
        BranchId             = o.BranchId,
        BranchName           = o.Branch?.Name ?? o.Branch?.Address ?? "",
        OvertimeDate         = o.OvertimeDate,
        StartTime            = o.StartTime,
        EndTime              = o.EndTime,
        Justification        = ToAppJustification(o.Justification),
        WeeklyWorkdaysNumber = o.WeeklyWorkdaysNumber,
        AseeApproval         = o.AseeApproval,
        IsCancelled          = o.IsCancelled,
        SubmittedToErgani    = o.SubmittedToErgani,
        Protocol             = o.Protocol,
        CreatedAt            = o.CreatedAt
    };
}
