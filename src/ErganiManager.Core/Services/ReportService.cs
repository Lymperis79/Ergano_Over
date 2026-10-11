using ErganiManager.Core.Interfaces;
using ErganiManager.Data;
using ErganiManager.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace ErganiManager.Core.Services;

public class ReportService : IReportService
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

        const double normalPerDay = 8.0;
        var rows = days
            .Select(s =>
            {
                var span = s.EndTime!.Value.ToTimeSpan() - s.StartTime!.Value.ToTimeSpan();
                if (span <= TimeSpan.Zero) span += TimeSpan.FromHours(24);   // shift past midnight
                return (Day: s, Hours: span.TotalHours);
            })
            .GroupBy(x => (x.Day.EmployeeId, Name: x.Day.Employee!.FullName, x.Day.ScheduleDate.Year, x.Day.ScheduleDate.Month))
            .OrderBy(g => g.Key.Name, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(g => g.Key.Year).ThenBy(g => g.Key.Month)
            .Select(g => new WorkTimeReportRow
            {
                EmployeeId   = g.Key.EmployeeId,
                EmployeeName = g.Key.Name,
                Year         = g.Key.Year,
                Month        = g.Key.Month,
                DaysWorked   = g.Count(),
                TotalHours   = g.Sum(x => x.Hours),
                NormalHours  = g.Sum(x => Math.Min(x.Hours, normalPerDay)),
                OverHours    = g.Sum(x => Math.Max(0, x.Hours - normalPerDay))
            })
            .ToList();

        return new WorkTimeReport
        {
            Rows = rows,
            EmployeeTotals = rows
                .GroupBy(r => (r.EmployeeId, r.EmployeeName))
                .Select(g => new ReportEmployeeTotal
                    { EmployeeId = g.Key.EmployeeId, EmployeeName = g.Key.EmployeeName, Hours = g.Sum(r => r.TotalHours) })
                .ToList(),
            GrandTotalHours = rows.Sum(r => r.TotalHours)
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
            .Where(k => k.Date >= from && k.Date <= to)
            .Distinct().ToList();

        var names = new Dictionary<int, string>();
        foreach (var s in schedules) names[s.EmployeeId] = s.Employee!.FullName;
        foreach (var c in cards) names.TryAdd(c.EmployeeId, c.Employee!.FullName);
        foreach (var o in overtime) names.TryAdd(o.EmployeeId, o.EmployeeFullName);

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
