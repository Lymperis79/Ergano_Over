using System.Text.Json;
using ErganiManager.Core.Interfaces;
using ErganiManager.Core.Models;
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
                throw new InvalidOperationException(ErganiManager.Data.UiText.T("Start and end time are required for Office/Home work days."));

            if (dto.EndTime <= dto.StartTime)
                throw new InvalidOperationException(ErganiManager.Data.UiText.T("End time must be after start time."));
        }

        await using var db = OpenDb();

        // A day imported from the Ergani export is already declared there: it is stored as
        // submitted so it is never sent again. Ordinary edits still reset the flag (below).
        var alreadyAtErgani = dto.Source == ErganiManager.Core.Interfaces.ScheduleSource.ErganiImported && dto.SubmittedToErgani;

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
                Source = ToEntityScheduleSource(dto.Source),
                StartTime = dto.StartTime,
                EndTime = dto.EndTime,
                Comments = dto.Comments,
                SubmittedToErgani = alreadyAtErgani,
                CreatedAt = DateTime.UtcNow
            };
            db.Schedules.Add(entity);
            await db.SaveChangesAsync();
            return entity.Id;
        }

        existing.BranchId = dto.BranchId;
        existing.WorkType = ToEntityWorkType(dto.WorkType);
        existing.Source = ToEntityScheduleSource(dto.Source);
        existing.StartTime = dto.StartTime;
        existing.EndTime = dto.EndTime;
        existing.Comments = dto.Comments;
        existing.UpdatedAt = DateTime.UtcNow;

        if (alreadyAtErgani)
        {
            existing.SubmittedToErgani = true; // keep any protocol we already know
        }
        else if (existing.SubmittedToErgani)
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

    public async Task<int> DeleteDaysAsync(int employeeId, IReadOnlyCollection<DateOnly> dates)
    {
        if (dates.Count == 0) return 0;

        await using var db = OpenDb();

        var wanted = dates.ToHashSet();
        var from = wanted.Min();
        var to   = wanted.Max();

        var inRange = await db.Schedules
            .Where(s => s.EmployeeId == employeeId && s.ScheduleDate >= from && s.ScheduleDate <= to)
            .ToListAsync();

        var toRemove = inRange.Where(s => wanted.Contains(s.ScheduleDate)).ToList();
        if (toRemove.Count == 0) return 0;

        db.Schedules.RemoveRange(toRemove);
        await db.SaveChangesAsync();
        return toRemove.Count;
    }

    public async Task<List<ScheduleSubmissionLogDto>> GetSubmissionLogAsync(
        int companyId, int employeeId, int take = 200)
    {
        await using var db = OpenDb();

        var rows = await db.ApiSubmissionLogs
            .Where(l => l.CompanyId == companyId
                     && l.EmployeeId == employeeId
                     && l.SubmissionType == "DailySchedule")
            .OrderByDescending(l => l.SubmissionDate)
            .Take(take)
            .ToListAsync();

        return rows.Select(l => new ScheduleSubmissionLogDto
        {
            Id              = l.Id,
            ScheduleDate    = l.ScheduleDate,
            SubmissionDate  = l.SubmissionDate,
            Success         = l.Success,
            Protocol        = l.Protocol,
            ErrorMessage    = l.ErrorMessage,
            HttpStatusCode  = l.HttpStatusCode,
            ResponseRawJson = l.ResponseRawJson
        }).ToList();
    }

    public async Task<int> BulkSetAsync(
        int employeeId, int branchId, DateOnly fromDate, DateOnly toDate,
        AppWorkType workType, TimeOnly? startTime, TimeOnly? endTime, IReadOnlySet<DayOfWeek>? onlyDaysOfWeek = null)
    {
        if (toDate < fromDate)
            throw new InvalidOperationException(ErganiManager.Data.UiText.T("End date must be on or after start date."));

        if (workType is AppWorkType.Office or AppWorkType.Home && (startTime == null || endTime == null))
            throw new InvalidOperationException(ErganiManager.Data.UiText.T("Start and end time are required for Office/Home work days."));

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
                existing.Source = Data.Entities.ScheduleSource.Manual;
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
                    Source = Data.Entities.ScheduleSource.Manual,
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

    private static Data.Entities.ScheduleSource ToEntityScheduleSource(Core.Interfaces.ScheduleSource source) => source switch
    {
        Core.Interfaces.ScheduleSource.ErganiImported => Data.Entities.ScheduleSource.ErganiImported,
        _ => Data.Entities.ScheduleSource.Manual
    };

    private static Core.Interfaces.ScheduleSource ToAppScheduleSource(Data.Entities.ScheduleSource source) => source switch
    {
        Data.Entities.ScheduleSource.ErganiImported => Core.Interfaces.ScheduleSource.ErganiImported,
        _ => Core.Interfaces.ScheduleSource.Manual
    };

    private static ScheduleDayDto ToDto(EmployeeSchedule s, List<WorkCard>? dayCards) => new()
    {
        Id = s.Id,
        EmployeeId = s.EmployeeId,
        BranchId = s.BranchId,
        ScheduleDate = s.ScheduleDate,
        WorkType = ToAppWorkType(s.WorkType),
        Source = ToAppScheduleSource(s.Source),
        StartTime = s.StartTime,
        EndTime = s.EndTime,
        SubmittedToErgani = s.SubmittedToErgani,
        Protocol = s.Protocol,
        Comments = s.Comments,
        ActualArrival = dayCards?.FirstOrDefault(w => w.MovementType == MovementType.Arrival)?.MovementDateTime,
        ActualDeparture = dayCards?.FirstOrDefault(w => w.MovementType == MovementType.Departure)?.MovementDateTime
    };

    // ── Clone to other employees (with history and undo) ─────────────────────

    /// <summary>How many clones are kept in the history of a company; older ones are dropped.</summary>
    private const int MaxCloneHistory = 20;

    /// <summary>A destination row exactly as it was before a clone replaced it.</summary>
    private sealed class SnapshotRow
    {
        public int EmployeeId { get; set; }
        public int BranchId { get; set; }
        public DateOnly ScheduleDate { get; set; }
        public WorkType WorkType { get; set; }
        public ScheduleType ScheduleType { get; set; }
        public Data.Entities.ScheduleSource Source { get; set; }
        public TimeOnly? StartTime { get; set; }
        public TimeOnly? EndTime { get; set; }
        public bool SubmittedToErgani { get; set; }
        public string? SubmissionId { get; set; }
        public string? Protocol { get; set; }
        public string? Comments { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
    }

    /// <summary>An unsent overtime record as it was before a clone removed it.</summary>
    private sealed class OvertimeSnapshotRow
    {
        public int EmployeeId { get; set; }
        public int BranchId { get; set; }
        public DateOnly OvertimeDate { get; set; }
        public TimeOnly StartTime { get; set; }
        public TimeOnly EndTime { get; set; }
        public OvertimeJustification Justification { get; set; }
        public int WeeklyWorkdaysNumber { get; set; }
        public string? AseeApproval { get; set; }
        public bool IsCancelled { get; set; }
        public string? ResponseRawJson { get; set; }
        public string? RequestPayloadJson { get; set; }
        public int? HttpStatusCode { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    /// <summary>An unsent holiday day as it was before a clone replaced it.</summary>
    private sealed class LeaveSnapshotRow
    {
        public int EmployeeId { get; set; }
        public int BranchId { get; set; }
        public DateOnly LeaveDate { get; set; }
        public string LeaveTypeCode { get; set; } = string.Empty;
        public TimeOnly? StartTime { get; set; }
        public TimeOnly? EndTime { get; set; }
        public int? ReferenceYear { get; set; }
        public int? EntitledDays { get; set; }
        public string? Comments { get; set; }
        public string? ResponseRawJson { get; set; }
        public string? RequestPayloadJson { get; set; }
        public int? HttpStatusCode { get; set; }
        public string? LastError { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    private static bool IsSentToErgani(bool submittedToErgani, string? protocol)
        => submittedToErgani || !string.IsNullOrWhiteSpace(protocol);

    public Task<int> CopyToEmployeesAsync(
        int sourceEmployeeId,
        IReadOnlyList<DateOnly> dates,
        IReadOnlyList<int> targetEmployeeIds,
        string? performedBy = null,
        bool includeHolidays = false)
        => CloneCoreAsync(sourceEmployeeId, dates.ToHashSet(), false, targetEmployeeIds, performedBy, includeHolidays);

    public Task<int> CopyToEmployeesAsync(
        int sourceEmployeeId,
        DateOnly fromDate,
        DateOnly toDate,
        IReadOnlyList<int> targetEmployeeIds,
        string? performedBy = null,
        bool includeHolidays = false)
    {
        var dates = new HashSet<DateOnly>();
        for (var d = fromDate; d <= toDate; d = d.AddDays(1)) dates.Add(d);
        return CloneCoreAsync(sourceEmployeeId, dates, true, targetEmployeeIds, performedBy, includeHolidays);
    }

    private async Task<int> CloneCoreAsync(
        int sourceEmployeeId,
        HashSet<DateOnly> dateSet,
        bool wholePeriod,
        IReadOnlyList<int> targetEmployeeIds,
        string? performedBy,
        bool includeHolidays)
    {
        var targetIds = targetEmployeeIds.Where(id => id != sourceEmployeeId).Distinct().ToList();
        if (dateSet.Count == 0 || targetIds.Count == 0) return 0;

        // One timestamp for the whole clone: the history entry and every row created by it carry it, so
        // Undo can tell what was created by (or after) this clone.
        var cloneTime = DateTime.UtcNow;

        await using var db = OpenDb();

        var sourceDays = await db.Schedules.AsNoTracking()
            .Where(s => s.EmployeeId == sourceEmployeeId && dateSet.Contains(s.ScheduleDate))
            .ToListAsync();
        var sourceLeaves = includeHolidays
            ? await db.Leaves.AsNoTracking()
                .Where(l => l.EmployeeId == sourceEmployeeId && dateSet.Contains(l.LeaveDate))
                .ToListAsync()
            : new List<Leave>();
        if (sourceDays.Count == 0 && sourceLeaves.Count == 0) return 0;

        var source = await db.Employees.AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == sourceEmployeeId)
            ?? throw new InvalidOperationException(UiText.T("The source employee was not found."));
        var targets = await db.Employees.AsNoTracking()
            .Where(e => targetIds.Contains(e.Id))
            .ToListAsync();

        // Everything the clone is about to replace. It is saved in the history (together with the
        // Ergani status / protocol / submission id / import source) so Undo can put it back as it was.
        var replaced = await db.Schedules
            .Where(s => targetIds.Contains(s.EmployeeId) && dateSet.Contains(s.ScheduleDate))
            .ToListAsync();
        var snapshot = replaced.Select(r => new SnapshotRow
        {
            EmployeeId = r.EmployeeId, BranchId = r.BranchId, ScheduleDate = r.ScheduleDate,
            WorkType = r.WorkType, ScheduleType = r.ScheduleType, Source = r.Source,
            StartTime = r.StartTime, EndTime = r.EndTime,
            SubmittedToErgani = r.SubmittedToErgani, SubmissionId = r.SubmissionId, Protocol = r.Protocol,
            Comments = r.Comments, CreatedAt = r.CreatedAt, UpdatedAt = r.UpdatedAt
        }).ToList();

        db.Schedules.RemoveRange(replaced);

        // Overtime (hours over 8) is derived from the end time. Unsent overtime records of the destination
        // days would become stale after the copy, so they are removed (and saved for Undo); the overtime for
        // the new end times is created again when the days are sent. Records already sent to Ergani stay.
        var replacedOvertime = await db.Overtimes
            .Where(o => targetIds.Contains(o.EmployeeId) && dateSet.Contains(o.OvertimeDate)
                        && !o.IsCancelled && !o.SubmittedToErgani)
            .ToListAsync();
        var overtimeSnapshot = replacedOvertime.Select(o => new OvertimeSnapshotRow
        {
            EmployeeId = o.EmployeeId, BranchId = o.BranchId, OvertimeDate = o.OvertimeDate,
            StartTime = o.StartTime, EndTime = o.EndTime, Justification = o.Justification,
            WeeklyWorkdaysNumber = o.WeeklyWorkdaysNumber, AseeApproval = o.AseeApproval,
            IsCancelled = o.IsCancelled, ResponseRawJson = o.ResponseRawJson,
            RequestPayloadJson = o.RequestPayloadJson, HttpStatusCode = o.HttpStatusCode,
            CreatedAt = o.CreatedAt
        }).ToList();
        db.Overtimes.RemoveRange(replacedOvertime);

        // Remove the pending-log rows belonging to overtime records replaced by this clone.
        if (replacedOvertime.Count > 0)
        {
            var replacedOvertimeIds = replacedOvertime.Select(o => o.Id).ToHashSet();
            var oldPendingLogs = await db.ApiSubmissionLogs
                .Where(l => l.CompanyId == source.CompanyId &&
                            l.SubmissionType == "Overtime" &&
                            l.ErrorMessage == "PENDING_OVERTIME")
                .ToListAsync();
            foreach (var log in oldPendingLogs)
            {
                if (TryGetPendingOvertimeId(log.RequestPayloadJson, out var overtimeId) &&
                    replacedOvertimeIds.Contains(overtimeId))
                    db.ApiSubmissionLogs.Remove(log);
            }
        }

        // Holidays: the destination's unsent holiday days on these dates are replaced by the source's.
        // Days already sent to Ergani stay (they cannot be withdrawn here) and those dates are skipped.
        var leaveSnapshot = new List<LeaveSnapshotRow>();
        var copiedLeaves = 0;
        if (includeHolidays)
        {
            var destLeaves = await db.Leaves
                .Where(l => targetIds.Contains(l.EmployeeId) && dateSet.Contains(l.LeaveDate))
                .ToListAsync();
            var replacedLeaves = destLeaves.Where(l => !l.SubmittedToErgani).ToList();
            var sentLeaveKeys = destLeaves.Where(l => l.SubmittedToErgani)
                .Select(l => (l.EmployeeId, l.LeaveDate)).ToHashSet();

            leaveSnapshot = replacedLeaves.Select(l => new LeaveSnapshotRow
            {
                EmployeeId = l.EmployeeId, BranchId = l.BranchId, LeaveDate = l.LeaveDate,
                LeaveTypeCode = l.LeaveTypeCode, StartTime = l.StartTime, EndTime = l.EndTime,
                ReferenceYear = l.ReferenceYear, EntitledDays = l.EntitledDays, Comments = l.Comments,
                ResponseRawJson = l.ResponseRawJson, RequestPayloadJson = l.RequestPayloadJson,
                HttpStatusCode = l.HttpStatusCode, LastError = l.LastError, CreatedAt = l.CreatedAt
            }).ToList();
            db.Leaves.RemoveRange(replacedLeaves);

            foreach (var targetId in targetIds)
            {
                foreach (var srcLeave in sourceLeaves)
                {
                    if (sentLeaveKeys.Contains((targetId, srcLeave.LeaveDate))) continue;
                    db.Leaves.Add(new Leave
                    {
                        EmployeeId = targetId, BranchId = srcLeave.BranchId, LeaveDate = srcLeave.LeaveDate,
                        LeaveTypeCode = srcLeave.LeaveTypeCode, StartTime = srcLeave.StartTime,
                        EndTime = srcLeave.EndTime, ReferenceYear = srcLeave.ReferenceYear,
                        EntitledDays = srcLeave.EntitledDays, Comments = srcLeave.Comments,
                        SubmittedToErgani = false, CreatedAt = cloneTime
                    });
                    copiedLeaves++;
                }
            }
        }

        int written = 0;
        var pendingOvertimeLogs = new List<ApiSubmissionLog>();
        foreach (var targetId in targetIds)
        {
            var targetEmployee = targets.FirstOrDefault(e => e.Id == targetId);
            foreach (var src in sourceDays)
            {
                var copiedSchedule = new EmployeeSchedule
                {
                    EmployeeId = targetId, BranchId = src.BranchId, ScheduleDate = src.ScheduleDate,
                    WorkType = src.WorkType, Source = Data.Entities.ScheduleSource.Manual,
                    StartTime = src.StartTime, EndTime = src.EndTime, Comments = src.Comments,
                    SubmittedToErgani = false, CreatedAt = cloneTime
                };
                db.Schedules.Add(copiedSchedule);
                written++;

                if (targetEmployee == null ||
                    src.WorkType is not (WorkType.Office or WorkType.Home))
                    continue;

                var overtimeRange = ScheduleHours.GetOvertimeRange(src.StartTime, src.EndTime);
                if (overtimeRange is not { } range)
                    continue;

                // Carry over the source day's reason when a matching source overtime exists;
                // otherwise use the normal default. Weekly workdays belong to the destination employee.
                var sourceOvertime = await db.Overtimes.AsNoTracking().FirstOrDefaultAsync(o =>
                    o.EmployeeId == sourceEmployeeId && o.OvertimeDate == src.ScheduleDate &&
                    o.StartTime == range.From && o.EndTime == range.To && !o.IsCancelled);
                var overtime = new Overtime
                {
                    EmployeeId = targetId,
                    BranchId = src.BranchId,
                    OvertimeDate = src.ScheduleDate,
                    StartTime = range.From,
                    EndTime = range.To,
                    Justification = sourceOvertime?.Justification ?? OvertimeJustification.ExceptionalWorkload,
                    WeeklyWorkdaysNumber = targetEmployee.WeeklyWorkdays,
                    RequestPayloadJson = JsonSerializer.Serialize(new { source = "schedule", scheduleId = copiedSchedule.Id }),
                    CreatedAt = cloneTime
                };
                db.Overtimes.Add(overtime);

                // Add the pending log after SaveChanges assigns the overtime's primary key.
                pendingOvertimeLogs.Add(new ApiSubmissionLog
                {
                    CompanyId = source.CompanyId,
                    EmployeeId = targetId,
                    EmployeeIdsJson = JsonSerializer.Serialize(new[] { targetId }),
                    SubmissionType = "Overtime",
                    ScheduleDate = src.ScheduleDate,
                    SubmissionDate = cloneTime,
                    Success = false,
                    ErrorMessage = "PENDING_OVERTIME"
                });
            }
        }

        // Keep the history short: drop the oldest entries so that, with this one, MaxCloneHistory remain.
        var obsolete = await db.ScheduleCloneBatches
            .Where(b => b.CompanyId == source.CompanyId)
            .OrderByDescending(b => b.Id)
            .Skip(MaxCloneHistory - 1)
            .ToListAsync();
        db.ScheduleCloneBatches.RemoveRange(obsolete);

        var orderedDates = dateSet.OrderBy(d => d).ToList();
        db.ScheduleCloneBatches.Add(new ScheduleCloneBatch
        {
            CompanyId = source.CompanyId,
            SourceEmployeeId = sourceEmployeeId,
            SourceEmployeeName = $"{source.FirstName} {source.LastName}",
            PerformedBy = performedBy,
            CreatedAt = cloneTime,
            FirstDate = orderedDates[0],
            LastDate = orderedDates[^1],
            DayCount = orderedDates.Count,
            IsWholePeriod = wholePeriod,
            DatesJson = JsonSerializer.Serialize(orderedDates),
            TargetEmployeeIdsJson = JsonSerializer.Serialize(targetIds),
            TargetNames = string.Join("; ", targets
                .OrderBy(t => t.LastName).ThenBy(t => t.FirstName)
                .Select(t => $"{t.FirstName} {t.LastName}")),
            EntriesWritten = written,
            ReplacedEntries = snapshot.Count,
            ReplacedSubmittedEntries = snapshot.Count(r => IsSentToErgani(r.SubmittedToErgani, r.Protocol)),
            SnapshotJson = JsonSerializer.Serialize(snapshot),
            ReplacedOvertimeEntries = overtimeSnapshot.Count,
            OvertimeSnapshotJson = JsonSerializer.Serialize(overtimeSnapshot),
            IncludesHolidays = includeHolidays,
            CopiedLeaveEntries = copiedLeaves,
            ReplacedLeaveEntries = leaveSnapshot.Count,
            LeaveSnapshotJson = JsonSerializer.Serialize(leaveSnapshot)
        });

        // One SaveChanges = the schedules, overtime records, pending logs and history entry are
        // written together or not at all. Save once to assign overtime IDs, then attach their log rows.
        await db.SaveChangesAsync();

        if (pendingOvertimeLogs.Count > 0)
        {
            var logIndex = 0;
            foreach (var targetId in targetIds)
            {
                foreach (var src in sourceDays)
                {
                    if (src.WorkType is not (WorkType.Office or WorkType.Home)) continue;
                    var range = ScheduleHours.GetOvertimeRange(src.StartTime, src.EndTime);
                    if (range is not { } overtimeRange) continue;

                    var overtime = await db.Overtimes.FirstOrDefaultAsync(o =>
                        o.EmployeeId == targetId && o.OvertimeDate == src.ScheduleDate &&
                        o.StartTime == overtimeRange.From && o.EndTime == overtimeRange.To &&
                        !o.IsCancelled && o.CreatedAt == cloneTime &&
                        o.RequestPayloadJson != null && o.RequestPayloadJson.Contains("\"source\":\"schedule\""));
                    if (overtime == null) continue;

                    var log = pendingOvertimeLogs[logIndex++];
                    log.RequestPayloadJson = JsonSerializer.Serialize(new { overtimeId = overtime.Id, source = "schedule" });
                    db.ApiSubmissionLogs.Add(log);
                }
            }
            await db.SaveChangesAsync();
        }

        return written + copiedLeaves;
    }

    private static bool TryGetPendingOvertimeId(string? payload, out int id)
    {
        id = 0;
        if (string.IsNullOrWhiteSpace(payload)) return false;
        try
        {
            using var doc = JsonDocument.Parse(payload);
            return doc.RootElement.TryGetProperty("overtimeId", out var value) && value.TryGetInt32(out id);
        }
        catch (JsonException) { return false; }
    }

    public async Task<List<CloneImpactDto>> GetCloneImpactAsync(
        IReadOnlyList<int> targetEmployeeIds,
        IReadOnlyCollection<DateOnly> dates)
    {
        var ids = targetEmployeeIds.Distinct().ToList();
        var dateSet = dates.ToHashSet();
        if (ids.Count == 0 || dateSet.Count == 0) return new List<CloneImpactDto>();

        await using var db = OpenDb();
        var rows = await db.Schedules.AsNoTracking()
            .Where(s => ids.Contains(s.EmployeeId) && dateSet.Contains(s.ScheduleDate))
            .Select(s => new { s.EmployeeId, s.SubmittedToErgani, s.Protocol })
            .ToListAsync();

        var overtime = await db.Overtimes.AsNoTracking()
            .Where(o => ids.Contains(o.EmployeeId) && dateSet.Contains(o.OvertimeDate) && !o.IsCancelled)
            .Select(o => new { o.EmployeeId, o.SubmittedToErgani })
            .ToListAsync();

        var leaves = await db.Leaves.AsNoTracking()
            .Where(l => ids.Contains(l.EmployeeId) && dateSet.Contains(l.LeaveDate))
            .Select(l => new { l.EmployeeId, l.SubmittedToErgani })
            .ToListAsync();

        return ids
            .Select(id => new CloneImpactDto
            {
                EmployeeId = id,
                ExistingEntries = rows.Count(r => r.EmployeeId == id),
                SubmittedEntries = rows.Count(r => r.EmployeeId == id && IsSentToErgani(r.SubmittedToErgani, r.Protocol)),
                UnsentOvertimeEntries = overtime.Count(o => o.EmployeeId == id && !o.SubmittedToErgani),
                SubmittedOvertimeEntries = overtime.Count(o => o.EmployeeId == id && o.SubmittedToErgani),
                UnsentLeaveEntries = leaves.Count(l => l.EmployeeId == id && !l.SubmittedToErgani),
                SubmittedLeaveEntries = leaves.Count(l => l.EmployeeId == id && l.SubmittedToErgani)
            })
            .Where(i => i.ExistingEntries > 0 || i.UnsentOvertimeEntries > 0 || i.SubmittedOvertimeEntries > 0
                        || i.UnsentLeaveEntries > 0 || i.SubmittedLeaveEntries > 0)
            .ToList();
    }

    public async Task<CloneBatchInfoDto?> GetLastCloneAsync(int companyId)
    {
        await using var db = OpenDb();
        var batch = await db.ScheduleCloneBatches.AsNoTracking()
            .Where(b => b.CompanyId == companyId && b.UndoneAt == null)
            .OrderByDescending(b => b.Id)
            .FirstOrDefaultAsync();
        if (batch == null) return null;

        var info = ToInfo(batch);
        info.SubmittedEntries = await CountSentInScopeAsync(db, batch);
        return info;
    }

    public async Task<List<CloneBatchInfoDto>> GetCloneHistoryAsync(int companyId, int take = 10)
    {
        await using var db = OpenDb();
        var batches = await db.ScheduleCloneBatches.AsNoTracking()
            .Where(b => b.CompanyId == companyId)
            .OrderByDescending(b => b.Id)
            .Take(take)
            .ToListAsync();
        return batches.Select(ToInfo).ToList();
    }

    public async Task<CloneUndoResult> UndoCloneAsync(int batchId, string? performedBy = null)
    {
        await using var db = OpenDb();

        var batch = await db.ScheduleCloneBatches.FirstOrDefaultAsync(b => b.Id == batchId);
        if (batch == null || batch.UndoneAt != null)
            return new CloneUndoResult { Status = CloneUndoStatus.NothingToUndo };

        // Only the latest clone can be undone: an older snapshot could otherwise bring back rows
        // that a newer clone has replaced since.
        var newerExists = await db.ScheduleCloneBatches
            .AnyAsync(b => b.CompanyId == batch.CompanyId && b.Id > batch.Id && b.UndoneAt == null);
        if (newerExists)
            return new CloneUndoResult { Status = CloneUndoStatus.NewerCloneExists };

        var dateSet = (JsonSerializer.Deserialize<List<DateOnly>>(batch.DatesJson) ?? new()).ToHashSet();
        var targetIds = JsonSerializer.Deserialize<List<int>>(batch.TargetEmployeeIdsJson) ?? new();
        var snapshot = JsonSerializer.Deserialize<List<SnapshotRow>>(batch.SnapshotJson) ?? new();

        var current = await db.Schedules
            .Where(s => targetIds.Contains(s.EmployeeId) && dateSet.Contains(s.ScheduleDate))
            .ToListAsync();

        // Something on these dates has been submitted to Ergani (or imported from it) after the clone.
        // A local undo cannot withdraw that from Ergani, so refuse instead of showing a false picture.
        // Overtime created since the clone belongs to the cloned days; if it was sent, undo is refused too.
        var overtimeSnapshot = JsonSerializer.Deserialize<List<OvertimeSnapshotRow>>(batch.OvertimeSnapshotJson) ?? new();
        var newOvertime = (await db.Overtimes
                .Where(o => targetIds.Contains(o.EmployeeId) && dateSet.Contains(o.OvertimeDate) && !o.IsCancelled)
                .ToListAsync())
            .Where(o => o.CreatedAt >= batch.CreatedAt)
            .ToList();

        // Holidays created since the clone (only when the clone copied holidays) are covered the same way.
        var leaveSnapshot = JsonSerializer.Deserialize<List<LeaveSnapshotRow>>(batch.LeaveSnapshotJson) ?? new();
        var newLeaves = batch.IncludesHolidays
            ? (await db.Leaves
                    .Where(l => targetIds.Contains(l.EmployeeId) && dateSet.Contains(l.LeaveDate))
                    .ToListAsync())
                .Where(l => l.CreatedAt >= batch.CreatedAt)
                .ToList()
            : new List<Leave>();

        var sent = current.Count(c => IsSentToErgani(c.SubmittedToErgani, c.Protocol))
                   + newOvertime.Count(o => o.SubmittedToErgani)
                   + newLeaves.Count(l => l.SubmittedToErgani);
        if (sent > 0)
            return new CloneUndoResult { Status = CloneUndoStatus.BlockedSubmitted, SubmittedEntries = sent };

        db.Schedules.RemoveRange(current);
        db.Overtimes.RemoveRange(newOvertime);
        db.Leaves.RemoveRange(newLeaves);
        foreach (var l in leaveSnapshot)
        {
            db.Leaves.Add(new Leave
            {
                EmployeeId = l.EmployeeId, BranchId = l.BranchId, LeaveDate = l.LeaveDate,
                LeaveTypeCode = l.LeaveTypeCode, StartTime = l.StartTime, EndTime = l.EndTime,
                ReferenceYear = l.ReferenceYear, EntitledDays = l.EntitledDays, Comments = l.Comments,
                ResponseRawJson = l.ResponseRawJson, RequestPayloadJson = l.RequestPayloadJson,
                HttpStatusCode = l.HttpStatusCode, LastError = l.LastError, CreatedAt = l.CreatedAt
            });
        }
        foreach (var o in overtimeSnapshot)
        {
            db.Overtimes.Add(new Overtime
            {
                EmployeeId = o.EmployeeId, BranchId = o.BranchId, OvertimeDate = o.OvertimeDate,
                StartTime = o.StartTime, EndTime = o.EndTime, Justification = o.Justification,
                WeeklyWorkdaysNumber = o.WeeklyWorkdaysNumber, AseeApproval = o.AseeApproval,
                IsCancelled = o.IsCancelled, ResponseRawJson = o.ResponseRawJson,
                RequestPayloadJson = o.RequestPayloadJson, HttpStatusCode = o.HttpStatusCode,
                CreatedAt = o.CreatedAt
            });
        }
        foreach (var r in snapshot)
        {
            db.Schedules.Add(new EmployeeSchedule
            {
                EmployeeId = r.EmployeeId, BranchId = r.BranchId, ScheduleDate = r.ScheduleDate,
                WorkType = r.WorkType, ScheduleType = r.ScheduleType, Source = r.Source,
                StartTime = r.StartTime, EndTime = r.EndTime,
                SubmittedToErgani = r.SubmittedToErgani, SubmissionId = r.SubmissionId, Protocol = r.Protocol,
                Comments = r.Comments, CreatedAt = r.CreatedAt, UpdatedAt = r.UpdatedAt
            });
        }

        batch.UndoneAt = DateTime.UtcNow;
        batch.UndoneBy = performedBy;
        await db.SaveChangesAsync();

        return new CloneUndoResult
        {
            Status = CloneUndoStatus.Done,
            RemovedEntries = current.Count,
            RestoredEntries = snapshot.Count,
            RestoredOvertimeEntries = overtimeSnapshot.Count,
            RestoredLeaveEntries = leaveSnapshot.Count
        };
    }

    private static async Task<int> CountSentInScopeAsync(AppDbContext db, ScheduleCloneBatch batch)
    {
        var dateSet = (JsonSerializer.Deserialize<List<DateOnly>>(batch.DatesJson) ?? new()).ToHashSet();
        var targetIds = JsonSerializer.Deserialize<List<int>>(batch.TargetEmployeeIdsJson) ?? new();
        if (dateSet.Count == 0 || targetIds.Count == 0) return 0;

        var rows = await db.Schedules.AsNoTracking()
            .Where(s => targetIds.Contains(s.EmployeeId) && dateSet.Contains(s.ScheduleDate))
            .Select(s => new { s.SubmittedToErgani, s.Protocol })
            .ToListAsync();
        var sentOvertime = await db.Overtimes.AsNoTracking()
            .Where(o => targetIds.Contains(o.EmployeeId) && dateSet.Contains(o.OvertimeDate)
                        && !o.IsCancelled && o.SubmittedToErgani && o.CreatedAt >= batch.CreatedAt)
            .CountAsync();

        var sentLeaves = batch.IncludesHolidays
            ? await db.Leaves.AsNoTracking()
                .Where(l => targetIds.Contains(l.EmployeeId) && dateSet.Contains(l.LeaveDate)
                            && l.SubmittedToErgani && l.CreatedAt >= batch.CreatedAt)
                .CountAsync()
            : 0;

        return rows.Count(r => IsSentToErgani(r.SubmittedToErgani, r.Protocol)) + sentOvertime + sentLeaves;
    }

    private static CloneBatchInfoDto ToInfo(ScheduleCloneBatch b)
    {
        var targetIds = JsonSerializer.Deserialize<List<int>>(b.TargetEmployeeIdsJson) ?? new();
        return new CloneBatchInfoDto
        {
            Id = b.Id,
            SourceEmployeeId = b.SourceEmployeeId,
            SourceEmployeeName = b.SourceEmployeeName,
            PerformedBy = b.PerformedBy,
            CreatedAtUtc = DateTime.SpecifyKind(b.CreatedAt, DateTimeKind.Utc),
            FirstDate = b.FirstDate,
            LastDate = b.LastDate,
            DayCount = b.DayCount,
            IsWholePeriod = b.IsWholePeriod,
            TargetCount = targetIds.Count,
            TargetNames = b.TargetNames
                .Split("; ", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToList(),
            EntriesWritten = b.EntriesWritten,
            ReplacedEntries = b.ReplacedEntries,
            ReplacedSubmittedEntries = b.ReplacedSubmittedEntries,
            ReplacedOvertimeEntries = b.ReplacedOvertimeEntries,
            IncludesHolidays = b.IncludesHolidays,
            CopiedLeaveEntries = b.CopiedLeaveEntries,
            ReplacedLeaveEntries = b.ReplacedLeaveEntries,
            UndoneAtUtc = b.UndoneAt.HasValue ? DateTime.SpecifyKind(b.UndoneAt.Value, DateTimeKind.Utc) : null,
            UndoneBy = b.UndoneBy
        };
    }
}
