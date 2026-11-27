using System.Text.Json;
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

        // Show the most recently created records first, regardless of their overtime date.
        var list = await query
            .OrderByDescending(o => o.CreatedAt)
            .ThenByDescending(o => o.Id)
            .ToListAsync();
        return list.Select(ToDto).ToList();
    }

    public async Task<int> CreateAsync(OvertimeDto dto, int companyId)
    {
        if (dto.EndTime <= dto.StartTime)
            throw new InvalidOperationException(ErganiManager.Data.UiText.T("End time must be after start time."));

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
            SubmittedToErgani    = dto.SubmittedToErgani, // true for records imported from Ergani
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
            ?? throw new InvalidOperationException(ErganiManager.Data.UiText.F("Overtime record {0} not found.", id));
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
        if (entity == null) return;

        if (entity.SubmittedToErgani || !string.IsNullOrWhiteSpace(entity.Protocol))
            throw new InvalidOperationException(ErganiManager.Data.UiText.T(
                "This overtime has already been submitted to Ergani and cannot be deleted locally."));

        // Schedule-generated overtime is derived from the employee's working periods.
        // Remove the derived record and roll the schedule back in the same save operation,
        // otherwise the calendar will generate the same overtime again on its next refresh.
        if (IsScheduleGenerated(entity.RequestPayloadJson))
        {
            var scheduleId = ReadScheduleId(entity.RequestPayloadJson);
            var scheduleQuery = db.Schedules.Include(s => s.WorkingPeriods)
                .Where(s => s.EmployeeId == entity.EmployeeId && s.ScheduleDate == entity.OvertimeDate);
            var schedule = scheduleId.HasValue
                ? await scheduleQuery.FirstOrDefaultAsync(s => s.Id == scheduleId.Value)
                : await scheduleQuery.FirstOrDefaultAsync();

            if (schedule != null)
            {
                var beforeSnapshot = CreateScheduleSnapshot(schedule);
                var restored = TryApplyScheduleSnapshot(schedule, schedule.PendingPreviousSnapshotJson);

                if (!restored)
                {
                    var historySnapshot = await db.ScheduleChangeHistory
                        .Where(h => h.EmployeeId == schedule.EmployeeId && h.ScheduleDate == schedule.ScheduleDate
                                    && h.PreviousSnapshotJson != null)
                        .OrderByDescending(h => h.ChangedAtUtc)
                        .ThenByDescending(h => h.Id)
                        .Select(h => h.PreviousSnapshotJson)
                        .FirstOrDefaultAsync();
                    restored = TryApplyScheduleSnapshot(schedule, historySnapshot);
                }

                // For a schedule created without an earlier saved version, trim the generated
                // overtime from the final working period. The generated interval is the tail
                // of that period, so its start is the previous working-time end.
                if (!restored)
                    restored = TrimGeneratedOvertime(schedule, entity);

                if (!restored)
                    throw new InvalidOperationException(ErganiManager.Data.UiText.T(
                        "The previous working time could not be determined. The overtime was not deleted, so the schedule remains unchanged."));

                // Keep a full audit trail and require the restored schedule to be reviewed/
                // submitted again rather than pretending that Ergani has been updated.
                if (schedule.SubmittedToErgani || !schedule.HasPendingChange)
                {
                    schedule.PendingPreviousSnapshotJson = beforeSnapshot;
                    schedule.HasPendingChange = true;
                }
                schedule.SubmittedToErgani = false;
                schedule.SubmissionId = null;
                schedule.Protocol = null;
                schedule.UpdatedAt = DateTime.UtcNow;

                db.ScheduleChangeHistory.Add(new ScheduleChangeHistory
                {
                    EmployeeId = schedule.EmployeeId,
                    ScheduleDate = schedule.ScheduleDate,
                    ChangeSource = "OvertimeDeletion",
                    PreviousSnapshotJson = beforeSnapshot,
                    NewWorkType = schedule.WorkType.ToString(),
                    NewPeriodsJson = JsonSerializer.Serialize(schedule.WorkingPeriods
                        .OrderBy(p => p.PeriodNumber)
                        .Select(p => new
                        {
                            p.PeriodNumber,
                            StartTime = p.StartTime.ToString("HH:mm"),
                            EndTime = p.EndTime.ToString("HH:mm")
                        }).ToList()),
                    ChangedAtUtc = DateTime.UtcNow
                });
            }
        }

        // Remove the pending Overtime log row tied to this generated record as well.
        var marker = "\\"overtimeId\\":" + entity.Id;
        var pendingLogs = await db.ApiSubmissionLogs
            .Where(l => l.SubmissionType == "Overtime" &&
                        l.ErrorMessage == "PENDING_OVERTIME" &&
                        l.RequestPayloadJson != null &&
                        l.RequestPayloadJson.Contains(marker))
            .ToListAsync();
        db.ApiSubmissionLogs.RemoveRange(pendingLogs);

        db.Overtimes.Remove(entity);
        await db.SaveChangesAsync();
    }

    private static bool IsScheduleGenerated(string? payload)
    {
        if (string.IsNullOrWhiteSpace(payload)) return false;
        try
        {
            using var document = JsonDocument.Parse(payload);
            return document.RootElement.TryGetProperty("source", out var source) &&
                   string.Equals(source.GetString(), "schedule", StringComparison.OrdinalIgnoreCase);
        }
        catch (JsonException) { return false; }
    }

    private static int? ReadScheduleId(string? payload)
    {
        if (string.IsNullOrWhiteSpace(payload)) return null;
        try
        {
            using var document = JsonDocument.Parse(payload);
            return document.RootElement.TryGetProperty("scheduleId", out var value) &&
                   value.TryGetInt32(out var id) ? id : null;
        }
        catch (JsonException) { return null; }
    }

    private static string CreateScheduleSnapshot(EmployeeSchedule schedule)
        => JsonSerializer.Serialize(new
        {
            WorkType = schedule.WorkType.ToString(),
            Periods = schedule.WorkingPeriods.OrderBy(p => p.PeriodNumber).Select(p => new
            {
                p.PeriodNumber,
                StartTime = p.StartTime.ToString("HH:mm"),
                EndTime = p.EndTime.ToString("HH:mm")
            }).ToList()
        });

    private static bool TryApplyScheduleSnapshot(EmployeeSchedule schedule, string? snapshotJson)
    {
        if (string.IsNullOrWhiteSpace(snapshotJson)) return false;
        try
        {
            using var document = JsonDocument.Parse(snapshotJson);
            var root = document.RootElement;
            if (!root.TryGetProperty("WorkType", out var workTypeElement) ||
                !Enum.TryParse<WorkType>(workTypeElement.GetString(), out var workType) ||
                !root.TryGetProperty("Periods", out var periodsElement) ||
                periodsElement.ValueKind != JsonValueKind.Array)
                return false;

            var periods = new List<ScheduleWorkingPeriod>();
            foreach (var item in periodsElement.EnumerateArray())
            {
                if (!item.TryGetProperty("PeriodNumber", out var numberElement) ||
                    !numberElement.TryGetInt32(out var number) ||
                    !item.TryGetProperty("StartTime", out var startElement) ||
                    !TimeOnly.TryParse(startElement.GetString(), out var start) ||
                    !item.TryGetProperty("EndTime", out var endElement) ||
                    !TimeOnly.TryParse(endElement.GetString(), out var end))
                    return false;

                periods.Add(new ScheduleWorkingPeriod
                {
                    PeriodNumber = number,
                    StartTime = start,
                    EndTime = end
                });
            }

            schedule.WorkType = workType;
            schedule.WorkingPeriods = periods.OrderBy(p => p.PeriodNumber).ToList();
            schedule.StartTime = schedule.WorkingPeriods.FirstOrDefault()?.StartTime;
            schedule.EndTime = schedule.WorkingPeriods.LastOrDefault()?.EndTime;
            return true;
        }
        catch (JsonException) { return false; }
    }

    private static bool TrimGeneratedOvertime(EmployeeSchedule schedule, Overtime overtime)
    {
        var periods = schedule.WorkingPeriods.OrderBy(p => p.PeriodNumber).ToList();
        if (periods.Count == 0) return false;

        var last = periods[^1];
        // Only trim when this overtime exactly reaches the end of the last working period.
        if (last.EndTime != overtime.EndTime || overtime.StartTime <= last.StartTime)
            return false;

        last.EndTime = overtime.StartTime;
        schedule.EndTime = overtime.StartTime;
        return true;
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
        SubmissionId         = o.SubmissionId,
        ResponseRawJson      = o.ResponseRawJson,
        SubmittedDate        = o.SubmittedDate,
        CreatedAt            = o.CreatedAt
    };
}
