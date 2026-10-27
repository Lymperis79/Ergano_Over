using ErganiManager.Core.Interfaces;
using ErganiManager.Core.Models;
using ErganiManager.Data;
using ErganiManager.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace ErganiManager.Core.Services;

public partial class ReportService : IReportService
{
    private readonly IConnectionStateService _connectionState;
    private readonly IOvertimeService _overtime;
    private readonly IRoundingSettingsService _rounding;

    public ReportService(IConnectionStateService connectionState, IOvertimeService overtime,
                         IRoundingSettingsService rounding)
    {
        _connectionState = connectionState;
        _overtime = overtime;
        _rounding = rounding;
    }

    public async Task<AttendanceReport> GetAttendanceReportAsync(
        int companyId, DateOnly from, DateOnly to, int? employeeId)
    {
        if (to < from) throw new ArgumentException("The end date must not be before the start date.", nameof(to));

        // Reuse the existing scan-pairing logic so overnight shifts, unmatched scans,
        // and blocked early-attempt records are handled consistently with the other reports.
        var summary = await GetTimeSummaryReportAsync(companyId, from, to, employeeId);
        var rows = new List<AttendanceReportRow>();

        foreach (var month in summary.Months)
        foreach (var day in month.Days.Where(d => d.Date >= from && d.Date <= to))
        {
            // Overtime-only dates are not attendance rows unless there is also a schedule or a scan.
            if (string.IsNullOrEmpty(day.DeclaredKind) && day.Pairs.Count == 0 && !day.IsDeclaredHoliday) continue;

            var firstArrival = day.ActualArrival;
            var lastDeparture = day.ActualDeparture;
            var scheduledStart = day.DeclaredFrom;
            var scheduledEnd = day.DeclaredTo;
            var lateMinutes = 0;
            var earlyMinutes = 0;

            if (scheduledStart is { } start && firstArrival is { } arrival &&
                (day.DeclaredKind is nameof(WorkType.Office) or nameof(WorkType.Home)))
            {
                var expected = day.Date.ToDateTime(start);
                var late = (arrival - expected).TotalMinutes;
                if (late > 5) lateMinutes = (int)Math.Round(late, MidpointRounding.AwayFromZero);
            }

            if (scheduledEnd is { } end && scheduledStart is { } startForEnd && lastDeparture is { } departure &&
                (day.DeclaredKind is nameof(WorkType.Office) or nameof(WorkType.Home)))
            {
                var expectedEnd = day.Date.ToDateTime(end);
                if (end <= startForEnd) expectedEnd = expectedEnd.AddDays(1);
                var early = (expectedEnd - departure).TotalMinutes;
                if (early > 5) earlyMinutes = (int)Math.Round(early, MidpointRounding.AwayFromZero);
            }

            rows.Add(new AttendanceReportRow
            {
                EmployeeId = month.EmployeeId,
                EmployeeName = month.EmployeeName,
                Date = day.Date,
                ScheduleKind = day.DeclaredKind,
                HolidayTypeCode = day.HolidayTypeCode,
                HolidayType = day.HolidayType,
                ScheduledStart = scheduledStart,
                ScheduledEnd = scheduledEnd,
                ScheduledHours = day.DeclaredHours,
                FirstArrival = firstArrival,
                LastDeparture = lastDeparture,
                ActualHours = day.ActualHours,
                LateMinutes = lateMinutes,
                EarlyDepartureMinutes = earlyMinutes,
                MissingClockIn = day.Pairs.Any(p => p.In is null && p.Out is not null),
                MissingClockOut = day.Pairs.Any(p => p.In is not null && p.Out is null),
                ScanPairs = string.Join(" | ", day.Pairs.Select(p => p.Text))
            });
        }

        rows = rows.OrderBy(r => r.EmployeeName, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(r => r.Date).ToList();

        var scheduledRows = rows.Where(r => r.ScheduleKind is nameof(WorkType.Office) or nameof(WorkType.Home)).ToList();
        var summaries = rows.GroupBy(r => (r.EmployeeId, r.EmployeeName))
            .Select(g => new AttendanceEmployeeSummary
            {
                EmployeeId = g.Key.EmployeeId,
                EmployeeName = g.Key.EmployeeName,
                ScheduledDays = g.Count(r => r.ScheduleKind is nameof(WorkType.Office) or nameof(WorkType.Home)),
                PresentDays = g.Count(r => (r.ScheduleKind is nameof(WorkType.Office) or nameof(WorkType.Home))
                    && r.FirstArrival.HasValue && r.LastDeparture.HasValue),
                NoShowDays = g.Count(r => (r.ScheduleKind is nameof(WorkType.Office) or nameof(WorkType.Home))
                    && r.Date <= DateOnly.FromDateTime(DateTime.Today)
                    && !r.FirstArrival.HasValue && !r.LastDeparture.HasValue),
                LateDays = g.Count(r => r.LateMinutes > 0),
                MissingClockInDays = g.Count(r => r.MissingClockIn),
                MissingClockOutDays = g.Count(r => r.MissingClockOut),
                ActualHours = g.Sum(r => r.ActualHours ?? 0)
            }).OrderBy(r => r.EmployeeName, StringComparer.CurrentCultureIgnoreCase).ToList();

        return new AttendanceReport
        {
            Rows = rows,
            EmployeeSummaries = summaries,
            ScheduledDays = scheduledRows.Count,
            PresentDays = scheduledRows.Count(r => r.FirstArrival.HasValue && r.LastDeparture.HasValue),
            NoShowDays = scheduledRows.Count(r => r.Date <= DateOnly.FromDateTime(DateTime.Today)
                && !r.FirstArrival.HasValue && !r.LastDeparture.HasValue),
            LateDays = scheduledRows.Count(r => r.LateMinutes > 0),
            MissingClockInDays = rows.Count(r => r.MissingClockIn),
            MissingClockOutDays = rows.Count(r => r.MissingClockOut),
            ActualHours = rows.Sum(r => r.ActualHours ?? 0)
        };
    }

    public async Task<OvertimeReport> GetOvertimeReportAsync(
        int companyId, DateOnly from, DateOnly to, int? employeeId)
    {
        var all = await _overtime.GetByCompanyAsync(companyId, from, to);
        var records = all
            .Where(o => !o.IsCancelled && (employeeId == null || o.EmployeeId == employeeId))
            .ToList();

        var months = records
            .GroupBy(o => (o.EmployeeId, o.EmployeeFullName, o.OvertimeDate.Year, o.OvertimeDate.Month))
            .OrderBy(g => g.Key.EmployeeFullName, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(g => g.Key.Year).ThenBy(g => g.Key.Month)
            .Select(g => new OvertimeReportMonth
            {
                EmployeeId   = g.Key.EmployeeId,
                EmployeeName = g.Key.EmployeeFullName,
                Year         = g.Key.Year,
                Month        = g.Key.Month,
                Records      = g.OrderBy(o => o.OvertimeDate).ThenBy(o => o.StartTime).ToList(),
                TotalHours   = g.Sum(o => o.HoursWorked)
            })
            .ToList();

        return new OvertimeReport
        {
            Months = months,
            EmployeeTotals = months
                .GroupBy(m => (m.EmployeeId, m.EmployeeName))
                .Select(g => new ReportEmployeeTotal
                    { EmployeeId = g.Key.EmployeeId, EmployeeName = g.Key.EmployeeName, Hours = g.Sum(m => m.TotalHours) })
                .ToList(),
            GrandTotalHours = months.Sum(m => m.TotalHours)
        };
    }

    public async Task<WorkTimeReport> GetWorkingTimeReportAsync(
        int companyId, DateOnly from, DateOnly to, int? employeeId)
    {
        await using var db = new AppDbContext(_connectionState.GetDbOptions());

        var query = db.Schedules
            .Include(s => s.Employee)
            .Where(s => s.Employee != null && s.Employee.CompanyId == companyId
                        && s.ScheduleDate >= from && s.ScheduleDate <= to
                        && (s.WorkType == WorkType.Office || s.WorkType == WorkType.Home)
                        && s.StartTime != null && s.EndTime != null);
        if (employeeId != null) query = query.Where(s => s.EmployeeId == employeeId);

        var days = await query.ToListAsync();

        // What the employee really worked comes from the clock-in/out scans: the same figure the
        // "Declared vs actual" report shows. The schedule only says what he was supposed to work.
        var summary = await GetTimeSummaryReportAsync(companyId, from, to, employeeId);
        var actualByMonth = summary.Months.ToDictionary(m => (m.EmployeeId, m.Year, m.Month));

        const double normalPerDay = 8.0;
        var scheduledRows = days
            .Select(s =>
            {
                var span = s.EndTime!.Value.ToTimeSpan() - s.StartTime!.Value.ToTimeSpan();
                if (span <= TimeSpan.Zero) span += TimeSpan.FromHours(24);   // shift past midnight
                return (Day: s, Hours: span.TotalHours);
            })
            .GroupBy(x => (x.Day.EmployeeId, Name: x.Day.Employee!.FullName, x.Day.ScheduleDate.Year, x.Day.ScheduleDate.Month))
            .Select(g => new WorkTimeReportRow
            {
                EmployeeId   = g.Key.EmployeeId,
                EmployeeName = g.Key.Name,
                Year         = g.Key.Year,
                Month        = g.Key.Month,
                DaysWorked   = g.Count(),
                TotalHours   = g.Sum(x => x.Hours),
                ActualHours  = actualByMonth.TryGetValue((g.Key.EmployeeId, g.Key.Year, g.Key.Month), out var am) ? am.ActualHours : 0,
                NormalHours  = g.Sum(x => Math.Min(x.Hours, normalPerDay)),
                OverHours    = g.Sum(x => Math.Max(0, x.Hours - normalPerDay))
            })
            .ToList();

        // A month with real scans but no Office/Home schedule still gets a row (scheduled = 0).
        var scheduledKeys = scheduledRows.Select(r => (r.EmployeeId, r.Year, r.Month)).ToHashSet();
        var scanOnlyRows = actualByMonth
            .Where(kv => !scheduledKeys.Contains(kv.Key) && kv.Value.ActualHours > 0)
            .Select(kv => new WorkTimeReportRow
            {
                EmployeeId   = kv.Value.EmployeeId,
                EmployeeName = kv.Value.EmployeeName,
                Year         = kv.Value.Year,
                Month        = kv.Value.Month,
                DaysWorked   = kv.Value.Days.Count(d => d.ActualHours != null),
                ActualHours  = kv.Value.ActualHours
            });

        var rows = scheduledRows.Concat(scanOnlyRows)
            .OrderBy(r => r.EmployeeName, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(r => r.Year).ThenBy(r => r.Month)
            .ToList();

        return new WorkTimeReport
        {
            Rows = rows,
            EmployeeTotals = rows
                .GroupBy(r => (r.EmployeeId, r.EmployeeName))
                .Select(g => new ReportEmployeeTotal
                {
                    EmployeeId = g.Key.EmployeeId, EmployeeName = g.Key.EmployeeName,
                    Hours = g.Sum(r => r.TotalHours), ActualHours = g.Sum(r => r.ActualHours)
                })
                .ToList(),
            GrandTotalHours  = rows.Sum(r => r.TotalHours),
            GrandActualHours = rows.Sum(r => r.ActualHours)
        };
    }

    public async Task<TimeSummaryReport> GetTimeSummaryReportAsync(
        int companyId, DateOnly from, DateOnly to, int? employeeId)
    {
        await using var db = new AppDbContext(_connectionState.GetDbOptions());

        var scheduleQuery = db.Schedules.Include(s => s.Employee)
            .Where(s => s.Employee != null && s.Employee.CompanyId == companyId
                        && s.ScheduleDate >= from && s.ScheduleDate <= to);
        if (employeeId != null) scheduleQuery = scheduleQuery.Where(s => s.EmployeeId == employeeId);
        var schedules = await scheduleQuery.ToListAsync();

        // Load one extra day on both sides so a real shift crossing midnight can be paired correctly.
        var rangeStart = from.AddDays(-1).ToDateTime(TimeOnly.MinValue);
        var rangeEnd   = to.AddDays(2).ToDateTime(TimeOnly.MinValue);
        var cardQuery = db.WorkCards.Include(w => w.Employee)
            .Where(w => w.Employee != null && w.Employee.CompanyId == companyId
                        && w.MovementDateTime >= rangeStart && w.MovementDateTime < rangeEnd
                        && !w.WasBlockedEarlyAttempt);
        if (employeeId != null) cardQuery = cardQuery.Where(w => w.EmployeeId == employeeId);
        var cards = await cardQuery.ToListAsync();

        var overtime = (await _overtime.GetByCompanyAsync(companyId, from, to))
            .Where(o => !o.IsCancelled && (employeeId == null || o.EmployeeId == employeeId))
            .ToList();

        var leaveQuery = db.Leaves.Include(l => l.Employee)
            .Where(l => l.Employee != null && l.Employee.CompanyId == companyId
                        && l.LeaveDate >= from && l.LeaveDate <= to);
        if (employeeId != null) leaveQuery = leaveQuery.Where(l => l.EmployeeId == employeeId);
        var leaves = await leaveQuery.AsNoTracking().ToListAsync();
        var leaveTypes = LeaveTypes.All.ToDictionary(t => t.Code, StringComparer.OrdinalIgnoreCase);
        var leavesByKey = leaves.ToLookup(l => (EmployeeId: l.EmployeeId, Date: l.LeaveDate));

        var rules = _rounding.Current;

        // Build clock-in/out pairs by the date of the clock-in. This is important for
        // night shifts such as 22:00 → 06:00, where the departure is on the next date.
        var cardPairsByKey = new Dictionary<(int EmployeeId, DateOnly Date), List<ScanPair>>();
        foreach (var employeeCards in cards.GroupBy(c => c.EmployeeId))
        {
            DateTime? openIn = null;
            foreach (var card in employeeCards.OrderBy(c => c.MovementDateTime))
            {
                if (card.MovementType == MovementType.Arrival)
                {
                    if (openIn is { } previousIn)
                    {
                        var previousDate = DateOnly.FromDateTime(previousIn);
                        AddPair(cardPairsByKey, employeeCards.Key, previousDate, new ScanPair { In = previousIn });
                    }
                    openIn = card.MovementDateTime;
                }
                else
                {
                    if (openIn is { } arrival)
                    {
                        AddPair(cardPairsByKey, employeeCards.Key, DateOnly.FromDateTime(arrival),
                            new ScanPair { In = arrival, Out = card.MovementDateTime });
                        openIn = null;
                    }
                    else
                    {
                        AddPair(cardPairsByKey, employeeCards.Key, DateOnly.FromDateTime(card.MovementDateTime),
                            new ScanPair { Out = card.MovementDateTime });
                    }
                }
            }

            if (openIn is { } unmatchedIn)
                AddPair(cardPairsByKey, employeeCards.Key, DateOnly.FromDateTime(unmatchedIn), new ScanPair { In = unmatchedIn });
        }

        // One row per employee + workday that has a schedule, scan pair or overtime.
        var keys = schedules.Select(s => (EmployeeId: s.EmployeeId, Date: s.ScheduleDate))
            .Concat(cardPairsByKey.Keys)
            .Concat(overtime.Select(o => (EmployeeId: o.EmployeeId, Date: o.OvertimeDate)))
            .Concat(leaves.Select(l => (EmployeeId: l.EmployeeId, Date: l.LeaveDate)))
            .Where(k => k.Date >= from && k.Date <= to)
            .Distinct().ToList();

        var names = new Dictionary<int, string>();
        foreach (var s in schedules) names[s.EmployeeId] = s.Employee!.FullName;
        foreach (var c in cards) names.TryAdd(c.EmployeeId, c.Employee!.FullName);
        foreach (var o in overtime) names.TryAdd(o.EmployeeId, o.EmployeeFullName);
        foreach (var l in leaves)
            if (l.Employee != null) names.TryAdd(l.EmployeeId, l.Employee.FullName);

        var scheduleByKey = schedules.ToLookup(s => (EmployeeId: s.EmployeeId, Date: s.ScheduleDate));
        var overtimeByKey = overtime.ToLookup(o => (EmployeeId: o.EmployeeId, Date: o.OvertimeDate));

        var rows = new List<(int EmployeeId, DayTimeRow Row)>();
        foreach (var key in keys)
        {
            var sch = scheduleByKey[key].FirstOrDefault();
            var dayCards = cardPairsByKey.TryGetValue(key, out var pairsForDay)
                ? pairsForDay.OrderBy(p => p.In ?? p.Out ?? DateTime.MinValue).ToList()
                : new List<ScanPair>();
            var dayOt = overtimeByKey[key].OrderBy(o => o.StartTime).ToList();
            var dayLeaves = leavesByKey[key].ToList();
            var holidayLabels = dayLeaves.Select(l =>
            {
                var code = l.LeaveTypeCode ?? string.Empty;
                return leaveTypes.TryGetValue(code, out var type) ? $"{type.Code} — {type.LocalizedDescription}" : code;
            }).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.CurrentCultureIgnoreCase).ToList();

            double? declared = null;
            if (sch is { StartTime: { } st, EndTime: { } et } && sch.WorkType is WorkType.Office or WorkType.Home)
            {
                var span = et.ToTimeSpan() - st.ToTimeSpan();
                if (span <= TimeSpan.Zero) span += TimeSpan.FromHours(24);
                declared = span.TotalHours;
            }

            double? actual = dayCards.Any(p => p.Hours != null) ? dayCards.Sum(p => p.Hours ?? 0) : null;
            var nightHours = dayCards.Sum(p => CalculateNightHours(p, rules));

            rows.Add((key.EmployeeId, new DayTimeRow
            {
                Date            = key.Item2,
                DeclaredKind    = sch == null ? string.Empty : sch.WorkType.ToString(),
                HolidayTypeCode = string.Join(", ", dayLeaves.Select(l => l.LeaveTypeCode).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase)),
                HolidayType     = string.Join("; ", holidayLabels),
                DeclaredFrom    = declared != null ? sch!.StartTime : null,
                DeclaredTo      = declared != null ? sch!.EndTime : null,
                DeclaredHours   = declared,
                Pairs           = dayCards,
                ActualHours     = actual,
                NightHours      = nightHours,
                RoundedHours    = actual != null ? rules.Round(actual.Value) : null,
                DiffHours       = declared != null && actual != null ? actual - declared : null,
                OvertimeRanges  = string.Join(", ", dayOt.Select(o => $"{o.StartTime:HH:mm}–{o.EndTime:HH:mm}")),
                OvertimeHours   = dayOt.Sum(o => o.HoursWorked)
            }));
        }

        var months = rows
            .GroupBy(r => (r.EmployeeId, r.Row.Date.Year, r.Row.Date.Month))
            .OrderBy(g => names[g.Key.EmployeeId], StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(g => g.Key.Year).ThenBy(g => g.Key.Month)
            .Select(g =>
            {
                var days = g.Select(x => x.Row).OrderBy(r => r.Date).ToList();
                return new TimeSummaryMonth
                {
                    EmployeeId    = g.Key.EmployeeId,
                    EmployeeName  = names[g.Key.EmployeeId],
                    Year          = g.Key.Year,
                    Month         = g.Key.Month,
                    Days          = days,
                    DeclaredHours = days.Sum(d => d.DeclaredHours ?? 0),
                    ActualHours   = days.Sum(d => d.ActualHours ?? 0),
                    DiffHours     = days.Sum(d => d.DiffHours ?? 0),
                    OvertimeHours = days.Sum(d => d.OvertimeHours),
                    RoundedHours  = days.Sum(d => d.RoundedHours ?? 0),
                    NightHours    = days.Sum(d => d.NightHours)
                };
            })
            .ToList();

        return new TimeSummaryReport
        {
            Months        = months,
            DeclaredHours = months.Sum(m => m.DeclaredHours),
            ActualHours   = months.Sum(m => m.ActualHours),
            OvertimeHours = months.Sum(m => m.OvertimeHours),
            RoundedHours  = months.Sum(m => m.RoundedHours),
            NightHours    = months.Sum(m => m.NightHours)
        };
    }

    private static void AddPair(Dictionary<(int EmployeeId, DateOnly Date), List<ScanPair>> target,
        int employeeId, DateOnly date, ScanPair pair)
    {
        var key = (employeeId, date);
        if (!target.TryGetValue(key, out var list))
            target[key] = list = new List<ScanPair>();
        list.Add(pair);
    }

    /// <summary>Returns the overlap of one real clock-in/out pair with every configured night window.</summary>
    private static double CalculateNightHours(ScanPair pair, RoundingRules rules)
    {
        if (pair.In is not { } start || pair.Out is not { } end || end <= start)
            return 0;

        var nightStart = rules.NightStart;
        var nightEnd = rules.NightEnd;
        var total = TimeSpan.Zero;

        // Check the night window on the start date, the end date, and the day before.
        // The loop also handles a configurable non-cross-midnight window.
        var firstDate = DateOnly.FromDateTime(start).AddDays(-1);
        var lastDate = DateOnly.FromDateTime(end);
        for (var date = firstDate; date <= lastDate; date = date.AddDays(1))
        {
            var windowStart = date.ToDateTime(nightStart);
            var windowEnd = nightEnd > nightStart
                ? date.ToDateTime(nightEnd)
                : date.AddDays(1).ToDateTime(nightEnd);

            var overlapStart = start > windowStart ? start : windowStart;
            var overlapEnd = end < windowEnd ? end : windowEnd;
            if (overlapEnd > overlapStart)
                total += overlapEnd - overlapStart;
        }

        return total.TotalHours;
    }
}
