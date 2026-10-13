using System.Globalization;
using ErganiManager.Core.Interfaces;
using ErganiManager.Data;
using ErganiManager.Data.Entities;
using ErganiManager.LocalCache;
using Microsoft.EntityFrameworkCore;

using UiText = ErganiManager.Data.UiText;
namespace ErganiManager.Core.Services;

/// <summary>The analysis reports shown as generic tables (see <see cref="TableReport"/>).</summary>
public partial class ReportService
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    private const string Minus = "\u2212";

    public Task<TableReport> GetTableReportAsync(
        TableReportKind kind, int companyId, DateOnly from, DateOnly to, int? employeeId) => kind switch
    {
        TableReportKind.RoundingImpact   => BuildRoundingImpactAsync(companyId, from, to, employeeId),
        TableReportKind.SubmissionStatus => BuildSubmissionStatusAsync(companyId, from, to, employeeId),
        TableReportKind.FailureLog       => BuildFailureLogAsync(companyId, from, to, employeeId),
        TableReportKind.Consistency      => BuildConsistencyAsync(companyId, from, to, employeeId),
        TableReportKind.Roster           => BuildRosterAsync(companyId, from, to, employeeId),
        TableReportKind.OvertimeTrend    => BuildOvertimeTrendAsync(companyId, from, to, employeeId),
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

    // ── small helpers ───────────────────────────────────────────────────────

    private static string SignedMinutes(int minutes) =>
        minutes == 0 ? "0" : (minutes < 0 ? Minus : "+") + Math.Abs(minutes) + " " + UiText.T("min");

    private static string SignedHours(double hours) =>
        Math.Abs(hours) < 1.0 / 120 ? "0:00" : (hours < 0 ? Minus : "+") + ReportFormat.Hours(Math.Abs(hours));

    private static string Trim(string? text, int max = 220)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;
        var t = text.Replace("\r", " ").Replace("\n", " ").Trim();
        return t.Length <= max ? t : t[..max] + "…";
    }

    private static string Clock(TimeOnly t) => t.ToString("HH:mm", Inv);

    private static double SpanHours(TimeOnly start, TimeOnly end)
    {
        var span = end.ToTimeSpan() - start.ToTimeSpan();
        if (span <= TimeSpan.Zero) span += TimeSpan.FromHours(24); // ends after midnight
        return span.TotalHours;
    }

    private static DateTime LocalStart(DateOnly d) => d.ToDateTime(TimeOnly.MinValue);

    // ── 1. Rounding impact ─────────────────────────────────────────────────

    private async Task<TableReport> BuildRoundingImpactAsync(int companyId, DateOnly from, DateOnly to, int? employeeId)
    {
        var summary = await GetTimeSummaryReportAsync(companyId, from, to, employeeId);
        var report = new TableReport { Title = UiText.T("Rounding impact") };

        var perEmployee = report.AddSection(UiText.T("Per employee"),
            (UiText.T("Employee"), 230), (UiText.T("Days with scans"), 120), (UiText.T("Actual"), 90), (UiText.T("Rounded"), 90), (UiText.T("Change"), 90),
            (UiText.T("Days up"), 80), (UiText.T("Days down"), 90), (UiText.T("Avg change / day"), 130));
        var detail = report.AddSection(UiText.T("Days where the rounding changed the time"),
            (UiText.T("Date"), 130), (UiText.T("Actual in → out"), 300), (UiText.T("Actual"), 80), (UiText.T("Rounded"), 80),
            (UiText.T("Rounded in → out"), 300), (UiText.T("Change"), 90));

        double allActual = 0, allRounded = 0;
        int allDays = 0, allUp = 0, allDown = 0, addedMin = 0, removedMin = 0, changedDays = 0;

        var employees = summary.Months
            .GroupBy(m => (m.EmployeeId, m.EmployeeName))
            .OrderBy(g => g.Key.EmployeeName, StringComparer.CurrentCultureIgnoreCase);

        foreach (var emp in employees)
        {
            int days = 0, up = 0, down = 0;
            double actual = 0, rounded = 0;

            foreach (var month in emp)
            {
                var changed = new List<(DayTimeRow Day, int Min)>();
                var monthDays = 0;

                foreach (var d in month.Days.Where(d => d.ActualHours != null && d.RoundedHours != null))
                {
                    monthDays++;
                    actual += d.ActualHours!.Value;
                    rounded += d.RoundedHours!.Value;
                    var min = (int)Math.Round((d.RoundedHours.Value - d.ActualHours.Value) * 60);
                    if (min > 0) { up++; addedMin += min; }
                    else if (min < 0) { down++; removedMin += -min; }
                    if (min != 0) changed.Add((d, min));
                }
                days += monthDays;

                if (changed.Count == 0) continue;
                changedDays += changed.Count;

                detail.Add(RowStyle.Group, $"{emp.Key.EmployeeName} — {month.MonthLabel}");
                foreach (var (d, min) in changed)
                    detail.Add(d.DateText, string.Join(" | ", d.Pairs.Select(p => p.Text)), d.ActualHoursText,
                        d.RoundedHoursText, d.RoundedInOutCsvText, SignedMinutes(min));
                detail.Add(RowStyle.Total, UiText.T("Month total"), UiText.F("{0} of {1} day(s) changed", changed.Count, monthDays),
                    ReportFormat.Hours(month.ActualHours), ReportFormat.Hours(month.RoundedHours), "",
                    SignedHours(month.RoundedHours - month.ActualHours));
            }

            if (days == 0) continue;
            allDays += days; allActual += actual; allRounded += rounded; allUp += up; allDown += down;

            var avg = (rounded - actual) * 60 / days;
            perEmployee.Add(emp.Key.EmployeeName, days.ToString(Inv), ReportFormat.Hours(actual), ReportFormat.Hours(rounded),
                SignedHours(rounded - actual), up.ToString(Inv), down.ToString(Inv),
                (avg < 0 ? Minus : avg > 0 ? "+" : "") + Math.Abs(avg).ToString("0.0", Inv) + " " + UiText.T("min"));
        }

        if (allDays > 0)
        {
            var avgAll = (allRounded - allActual) * 60 / allDays;
            perEmployee.Add(RowStyle.Total, UiText.T("All employees"), allDays.ToString(Inv), ReportFormat.Hours(allActual),
                ReportFormat.Hours(allRounded), SignedHours(allRounded - allActual), allUp.ToString(Inv), allDown.ToString(Inv),
                (avgAll < 0 ? Minus : avgAll > 0 ? "+" : "") + Math.Abs(avgAll).ToString("0.0", Inv) + " " + UiText.T("min"));
        }

        report.Summary = allDays == 0
            ? UiText.T("No clock-in/out pairs in this period.")
            : UiText.F("Rounding changed {0} of {1} day(s): {2} min added, {3} min removed, net {4}. Uses the rounding bands from Administration.", changedDays, allDays, addedMin, removedMin, SignedHours(allRounded - allActual));
        return report;
    }

    // ── 2. Submission status ───────────────────────────────────────────────

    private async Task<TableReport> BuildSubmissionStatusAsync(int companyId, DateOnly from, DateOnly to, int? employeeId)
    {
        await using var db = new AppDbContext(_connectionState.GetDbOptions());
        var dayFrom = LocalStart(from);
        var dayTo = LocalStart(to.AddDays(1));
        string Name(string? f, string? l) => $"{f} {l}".Trim();

        var cards = await db.WorkCards
            .Where(w => w.Employee != null && w.Employee.CompanyId == companyId
                        && w.MovementDateTime >= dayFrom && w.MovementDateTime < dayTo
                        && !w.WasBlockedEarlyAttempt && (employeeId == null || w.EmployeeId == employeeId))
            .Select(w => new { w.Id, w.EmployeeId, F = w.Employee!.FirstName, L = w.Employee!.LastName,
                               w.MovementType, w.MovementDateTime, w.SubmittedToErgani })
            .ToListAsync();

        var schedules = await db.Schedules
            .Where(s => s.Employee != null && s.Employee.CompanyId == companyId
                        && s.ScheduleDate >= from && s.ScheduleDate <= to && (employeeId == null || s.EmployeeId == employeeId))
            .Select(s => new { s.EmployeeId, F = s.Employee!.FirstName, L = s.Employee!.LastName,
                               s.ScheduleDate, s.WorkType, s.StartTime, s.EndTime, s.SubmittedToErgani })
            .ToListAsync();

        var overtimes = await db.Overtimes
            .Where(o => !o.IsCancelled && o.Employee != null && o.Employee.CompanyId == companyId
                        && o.OvertimeDate >= from && o.OvertimeDate <= to && (employeeId == null || o.EmployeeId == employeeId))
            .Select(o => new { o.EmployeeId, F = o.Employee!.FirstName, L = o.Employee!.LastName,
                               o.OvertimeDate, o.StartTime, o.EndTime, o.SubmittedToErgani, o.HttpStatusCode,
                               Response = o.ResponseRawJson == null ? null : o.ResponseRawJson.Substring(0, 300) })
            .ToListAsync();

        var leaves = await db.Leaves
            .Where(l => l.Employee != null && l.Employee.CompanyId == companyId
                        && l.LeaveDate >= from && l.LeaveDate <= to && (employeeId == null || l.EmployeeId == employeeId))
            .Select(l => new { l.EmployeeId, F = l.Employee!.FirstName, L = l.Employee!.LastName,
                               l.LeaveDate, l.LeaveTypeCode, l.SubmittedToErgani, l.HttpStatusCode, l.LastError })
            .ToListAsync();

        // Why the attempts failed: the newest failed log line of each work card / schedule day.
        var logFrom = dayFrom.ToUniversalTime().AddDays(-2);
        var failedLogs = await db.ApiSubmissionLogs
            .Where(l => l.CompanyId == companyId && !l.Success && l.SubmissionDate >= logFrom)
            .OrderByDescending(l => l.SubmissionDate)
            .Select(l => new { l.WorkCardId, l.EmployeeId, l.ScheduleDate, l.SubmissionType, l.ErrorMessage, l.HttpStatusCode })
            .ToListAsync();
        var errorByCard = failedLogs.Where(l => l.WorkCardId != null)
            .GroupBy(l => l.WorkCardId!.Value).ToDictionary(g => g.Key, g => (g.First().ErrorMessage, g.First().HttpStatusCode));
        var errorBySchedule = failedLogs.Where(l => l.SubmissionType == "DailySchedule" && l.EmployeeId != null && l.ScheduleDate != null)
            .GroupBy(l => (l.EmployeeId!.Value, l.ScheduleDate!.Value))
            .ToDictionary(g => g.Key, g => (g.First().ErrorMessage, g.First().HttpStatusCode));

        var pending = new List<(string Type, string Emp, DateTime When, string Detail, string Http, string Error)>();

        foreach (var c in cards.Where(c => !c.SubmittedToErgani))
        {
            errorByCard.TryGetValue(c.Id, out var err);
            pending.Add(("Work card", Name(c.F, c.L), c.MovementDateTime,
                $"{(c.MovementType == MovementType.Arrival ? UiText.T("Arrival") : UiText.T("Departure"))} {c.MovementDateTime:HH:mm}",
                err.HttpStatusCode?.ToString(Inv) ?? "", Trim(err.ErrorMessage)));
        }
        foreach (var s in schedules.Where(s => !s.SubmittedToErgani))
        {
            errorBySchedule.TryGetValue((s.EmployeeId, s.ScheduleDate), out var err);
            var detail = s.WorkType switch
            {
                WorkType.Rest => UiText.T("Rest"),
                WorkType.Absent => UiText.T("Absent"),
                _ => $"{(s.WorkType == WorkType.Home ? UiText.T("Home") : UiText.T("Office"))} {(s.StartTime is { } a ? Clock(a) : "?")}–{(s.EndTime is { } b ? Clock(b) : "?")}"
            };
            pending.Add(("Schedule", Name(s.F, s.L), LocalStart(s.ScheduleDate), detail,
                err.HttpStatusCode?.ToString(Inv) ?? "", Trim(err.ErrorMessage)));
        }
        foreach (var o in overtimes.Where(o => !o.SubmittedToErgani))
            pending.Add(("Overtime", Name(o.F, o.L), LocalStart(o.OvertimeDate),
                $"{Clock(o.StartTime)}–{Clock(o.EndTime)} ({ReportFormat.Hours(SpanHours(o.StartTime, o.EndTime))})",
                o.HttpStatusCode?.ToString(Inv) ?? "", Trim(o.Response)));
        foreach (var l in leaves.Where(l => !l.SubmittedToErgani))
            pending.Add(("Leave", Name(l.F, l.L), LocalStart(l.LeaveDate), l.LeaveTypeCode,
                l.HttpStatusCode?.ToString(Inv) ?? "", Trim(l.LastError)));

        var report = new TableReport { Title = UiText.T("Submission status") };

        var overview = report.AddSection(UiText.T("Overview"),
            (UiText.T("Type"), 160), (UiText.T("Total"), 90), (UiText.T("Sent"), 90), (UiText.T("Not sent"), 90), (UiText.T("% sent"), 90));
        void Line(string type, int total, int sent)
        {
            var notSent = total - sent;
            overview.Add(notSent > 0 ? RowStyle.Warning : RowStyle.Normal, type, total.ToString(Inv), sent.ToString(Inv),
                notSent.ToString(Inv), total == 0 ? "—" : (sent * 100.0 / total).ToString("0", Inv) + "%");
        }
        Line(UiText.T("Work cards"), cards.Count, cards.Count(c => c.SubmittedToErgani));
        Line(UiText.T("Schedules"), schedules.Count, schedules.Count(s => s.SubmittedToErgani));
        Line(UiText.T("Overtime"), overtimes.Count, overtimes.Count(o => o.SubmittedToErgani));
        Line(UiText.T("Leave"), leaves.Count, leaves.Count(l => l.SubmittedToErgani));
        var allTotal = cards.Count + schedules.Count + overtimes.Count + leaves.Count;
        var allSent = cards.Count(c => c.SubmittedToErgani) + schedules.Count(s => s.SubmittedToErgani)
                      + overtimes.Count(o => o.SubmittedToErgani) + leaves.Count(l => l.SubmittedToErgani);
        overview.Add(RowStyle.Total, UiText.T("All"), allTotal.ToString(Inv), allSent.ToString(Inv), (allTotal - allSent).ToString(Inv),
            allTotal == 0 ? "—" : (allSent * 100.0 / allTotal).ToString("0", Inv) + "%");

        var notSentSection = report.AddSection(UiText.T("Not yet sent to Ergani"),
            (UiText.T("Type"), 110), (UiText.T("Employee"), 210), (UiText.T("Date"), 140), (UiText.T("Detail"), 220), (UiText.T("HTTP"), 60), (UiText.T("Last error"), 520));
        foreach (var p in pending.OrderBy(p => p.Type).ThenBy(p => p.Emp, StringComparer.CurrentCultureIgnoreCase).ThenBy(p => p.When))
            notSentSection.Add(RowStyle.Warning, UiText.T(p.Type), p.Emp, p.When.ToString(p.When.TimeOfDay == TimeSpan.Zero ? "ddd dd/MM/yyyy" : "dd/MM/yyyy HH:mm", System.Globalization.CultureInfo.CurrentCulture),
                p.Detail, p.Http, p.Error);

        report.Summary = allTotal == 0
            ? UiText.T("Nothing in this period.")
            : UiText.F("{0} of {1} item(s) are not yet sent to Ergani. ", allTotal - allSent, allTotal) +
              UiText.T("Schedules planned for future days also show here until they are submitted.");
        return report;
    }

    // ── 3. Retry and failure log ───────────────────────────────────────────

    private async Task<TableReport> BuildFailureLogAsync(int companyId, DateOnly from, DateOnly to, int? employeeId)
    {
        await using var db = new AppDbContext(_connectionState.GetDbOptions());
        var fromUtc = LocalStart(from).ToUniversalTime();
        var toUtc = LocalStart(to.AddDays(1)).ToUniversalTime();

        var names = await db.Employees.Where(e => e.CompanyId == companyId)
            .Select(e => new { e.Id, e.FirstName, e.LastName })
            .ToDictionaryAsync(e => e.Id, e => $"{e.FirstName} {e.LastName}".Trim());
        string Emp(int? id) => id != null && names.TryGetValue(id.Value, out var n) ? n : "—";

        // Payload/response JSON are not loaded: they can be large and are not shown here.
        var logs = await db.ApiSubmissionLogs
            .Where(l => l.CompanyId == companyId && l.SubmissionDate >= fromUtc && l.SubmissionDate < toUtc
                        && (employeeId == null || l.EmployeeId == employeeId))
            .Select(l => new { l.SubmissionType, l.EmployeeId, l.SubmissionDate, l.Success, l.HttpStatusCode,
                               l.ErrorMessage, l.DurationMs, l.RetryCount, l.LastRetryAt, l.Protocol })
            .ToListAsync();

        var report = new TableReport { Title = UiText.T("Retry and failure log") };

        var byType = report.AddSection(UiText.T("Summary per submission type"),
            (UiText.T("Type"), 160), (UiText.T("Attempts"), 90), (UiText.T("Failed (final)"), 120), (UiText.T("OK after retry"), 120),
            (UiText.T("Total retries"), 110), (UiText.T("Avg duration"), 110));
        foreach (var g in logs.GroupBy(l => l.SubmissionType).OrderBy(g => g.Key))
        {
            var failed = g.Count(l => !l.Success);
            byType.Add(failed > 0 ? RowStyle.Warning : RowStyle.Normal, g.Key, g.Count().ToString(Inv), failed.ToString(Inv),
                g.Count(l => l.Success && l.RetryCount > 0).ToString(Inv), g.Sum(l => l.RetryCount).ToString(Inv),
                $"{g.Average(l => (double)l.DurationMs):0} " + UiText.T("ms"));
        }
        if (logs.Count > 0)
            byType.Add(RowStyle.Total, UiText.T("All"), logs.Count.ToString(Inv), logs.Count(l => !l.Success).ToString(Inv),
                logs.Count(l => l.Success && l.RetryCount > 0).ToString(Inv), logs.Sum(l => l.RetryCount).ToString(Inv),
                $"{logs.Average(l => (double)l.DurationMs):0} " + UiText.T("ms"));

        var details = report.AddSection(UiText.T("Failed or retried submissions"),
            (UiText.T("Time"), 140), (UiText.T("Type"), 130), (UiText.T("Employee"), 200), (UiText.T("Result"), 120), (UiText.T("HTTP"), 60),
            (UiText.T("Retries"), 70), (UiText.T("Last retry"), 140), (UiText.T("Duration"), 90), (UiText.T("Error / protocol"), 480));
        foreach (var l in logs.Where(l => !l.Success || l.RetryCount > 0).OrderByDescending(l => l.SubmissionDate))
            details.Add(l.Success ? RowStyle.Normal : RowStyle.Warning,
                l.SubmissionDate.ToLocalTime().ToString("dd/MM/yyyy HH:mm", Inv), l.SubmissionType, Emp(l.EmployeeId),
                l.Success ? UiText.T("OK after retry") : UiText.T("FAILED"), l.HttpStatusCode?.ToString(Inv) ?? "", l.RetryCount.ToString(Inv),
                l.LastRetryAt?.ToLocalTime().ToString("dd/MM/yyyy HH:mm", Inv) ?? "", $"{l.DurationMs} " + UiText.T("ms"),
                l.Success ? Trim(l.Protocol) : Trim(l.ErrorMessage));

        // The offline queue of THIS computer (a terminal keeps its own local cache).
        var queue = report.AddSection(UiText.T("Terminal queue on this computer"),
            (UiText.T("Scanned"), 140), (UiText.T("Employee"), 200), (UiText.T("Movement"), 100), (UiText.T("Reason"), 150), (UiText.T("Retries"), 70),
            (UiText.T("Last retry"), 140), (UiText.T("State"), 160), (UiText.T("Error"), 420));
        var waiting = 0;
        try
        {
            using var cache = LocalCacheDbContextFactory.Create();
            var failed = await cache.FailedSubmissions
                .Where(f => f.CompanyId == companyId && f.CreatedAt >= fromUtc && f.CreatedAt < toUtc
                            && (employeeId == null || f.EmployeeId == employeeId))
                .OrderByDescending(f => f.CreatedAt).ToListAsync();
            foreach (var f in failed)
                queue.Add(f.Resolved ? RowStyle.Normal : RowStyle.Warning,
                    f.OriginalScannedAt.ToString("dd/MM/yyyy HH:mm", Inv), Emp(f.EmployeeId), UiText.T(f.MovementType), f.FailureReason,
                    f.RetryCount.ToString(Inv), f.LastRetryAt?.ToLocalTime().ToString("dd/MM/yyyy HH:mm", Inv) ?? "",
                    f.Resolved ? UiText.F("Resolved {0}", f.ResolvedProtocol).Trim() : UiText.T("Open"),
                    Trim(f.LastRetryError ?? f.ErrorDescription));
            waiting = await cache.PendingSubmissions.CountAsync(p => !p.Synced && p.CompanyId == companyId);
        }
        catch (Exception ex)
        {
            queue.Note = UiText.F("The local terminal queue could not be read: {0}", ex.Message);
        }
        if (waiting > 0) queue.Note = UiText.F("{0} scan(s) are still waiting to be sent from this computer.", waiting);

        var stillFailing = logs.Count(l => !l.Success);
        report.Summary = logs.Count == 0
            ? UiText.T("No submissions were logged in this period.")
            : UiText.F("{0} submission(s) logged: {1} still failing, {2} succeeded after a retry.", logs.Count, stillFailing, logs.Count(l => l.Success && l.RetryCount > 0));
        return report;
    }

    // ── 4. Rest-day / schedule consistency ─────────────────────────────────

    private async Task<TableReport> BuildConsistencyAsync(int companyId, DateOnly from, DateOnly to, int? employeeId)
    {
        const int ToleranceMinutes = 30;
        var summary = await GetTimeSummaryReportAsync(companyId, from, to, employeeId);

        await using var db = new AppDbContext(_connectionState.GetDbOptions());
        var leaveDays = (await db.Leaves
                .Where(l => l.Employee != null && l.Employee.CompanyId == companyId
                            && l.LeaveDate >= from && l.LeaveDate <= to && (employeeId == null || l.EmployeeId == employeeId))
                .Select(l => new { l.EmployeeId, l.LeaveDate }).ToListAsync())
            .Select(l => (l.EmployeeId, l.LeaveDate)).ToHashSet();

        var today = DateOnly.FromDateTime(DateTime.Today);
        var findings = new List<(string Emp, DateOnly Date, string Declared, string Actual, string Finding, string Group)>();

        foreach (var month in summary.Months)
        foreach (var d in month.Days)
        {
            var hasScans = d.Pairs.Count > 0;
            var hasLeave = leaveDays.Contains((month.EmployeeId, d.Date));
            var actual = hasScans ? string.Join(" | ", d.Pairs.Select(p => p.Text)) : "—";
            void Add(string finding, string? group = null) => findings.Add((month.EmployeeName, d.Date, d.DeclaredText, actual, finding, group ?? finding));

            if (hasLeave && hasScans) Add(UiText.T("Scans on a leave day"));
            else if (d.DeclaredKind == "Rest" && hasScans) Add(UiText.T("Scans on a rest day"));
            else if (d.DeclaredKind == "Absent" && hasScans) Add(UiText.T("Scans on an absence day"));
            else if (d.DeclaredKind.Length == 0 && hasScans) Add(UiText.T("Scans without a schedule"));

            var isWorkDay = d.DeclaredKind is "Office" or "Home";
            if (isWorkDay && !hasScans && d.Date < today && !hasLeave) Add(UiText.T("No scans on a working day"));

            if (hasScans && d.Date < today && d.Pairs.Any(p => p.In == null || p.Out == null))
                Add(UiText.T("Missing clock-in or clock-out"));

            // Scans well outside the declared hours (only when every pair is complete).
            if (isWorkDay && d.DeclaredFrom is { } declaredFrom && d.DeclaredTo is { } declaredTo
                && hasScans && d.Pairs.All(p => p.In != null && p.Out != null)
                && d.ActualArrival is { } arrival && d.ActualDeparture is { } departure)
            {
                var declaredStart = d.Date.ToDateTime(declaredFrom);
                var declaredEnd = d.Date.ToDateTime(declaredTo);
                if (declaredEnd <= declaredStart) declaredEnd = declaredEnd.AddDays(1);

                var startDiff = (arrival - declaredStart).TotalMinutes;
                if (startDiff > ToleranceMinutes) Add(UiText.F("Arrived {0} min after the start", Math.Round(startDiff).ToString("0")), UiText.F("Arrived more than {0} min late", ToleranceMinutes));
                else if (startDiff < -ToleranceMinutes) Add(UiText.F("Arrived {0} min before the start", Math.Round(-startDiff).ToString("0")), UiText.F("Arrived more than {0} min early", ToleranceMinutes));

                var endDiff = (departure - declaredEnd).TotalMinutes;
                if (endDiff < -ToleranceMinutes) Add(UiText.F("Left {0} min before the end", Math.Round(-endDiff).ToString("0")), UiText.F("Left more than {0} min early", ToleranceMinutes));
                else if (endDiff > ToleranceMinutes) Add(UiText.F("Left {0} min after the end", Math.Round(endDiff).ToString("0")), UiText.F("Left more than {0} min after the end", ToleranceMinutes));
            }
        }

        var report = new TableReport { Title = UiText.T("Rest-day / schedule consistency") };

        var overview = report.AddSection(UiText.T("Findings overview"), (UiText.T("Finding"), 330), (UiText.T("Count"), 90), (UiText.T("Employees"), 110));
        // "Arrived 45 min after the start" is grouped under one heading for the overview
        // (the heading is stored with each finding as its Group).
        foreach (var g in findings.GroupBy(f => f.Group).OrderByDescending(g => g.Count()))
            overview.Add(RowStyle.Warning, g.Key, g.Count().ToString(Inv), g.Select(x => x.Emp).Distinct().Count().ToString(Inv));

        var detail = report.AddSection(UiText.T("Details"),
            (UiText.T("Employee"), 210), (UiText.T("Date"), 140), (UiText.T("Declared"), 150), (UiText.T("Actual scans"), 300), (UiText.T("Finding"), 330));
        foreach (var f in findings.OrderBy(f => f.Emp, StringComparer.CurrentCultureIgnoreCase).ThenBy(f => f.Date))
            detail.Add(f.Emp, f.Date.ToString("ddd dd/MM/yyyy", System.Globalization.CultureInfo.CurrentCulture), f.Declared, f.Actual, f.Finding);

        report.Summary = findings.Count == 0
            ? UiText.T("No inconsistencies found between the schedules, the leave and the clock-in/out scans.")
            : UiText.F("{0} finding(s) for {1} employee(s). ", findings.Count, findings.Select(f => f.Emp).Distinct().Count()) +
              UiText.F("Hours deviations are reported when a scan is more than {0} min away from the declared time.", ToleranceMinutes);
        return report;
    }

    // ── 5. Daily / weekly roster ───────────────────────────────────────────

    private async Task<TableReport> BuildRosterAsync(int companyId, DateOnly from, DateOnly to, int? employeeId)
    {
        const int MaxDays = 31;
        var limited = to.DayNumber - from.DayNumber + 1 > MaxDays;
        if (limited) to = from.AddDays(MaxDays - 1);
        var dayCount = to.DayNumber - from.DayNumber + 1;

        await using var db = new AppDbContext(_connectionState.GetDbOptions());

        var schedules = await db.Schedules
            .Where(s => s.Employee != null && s.Employee.CompanyId == companyId
                        && s.ScheduleDate >= from && s.ScheduleDate <= to && (employeeId == null || s.EmployeeId == employeeId))
            .Select(s => new { s.EmployeeId, s.ScheduleDate, s.WorkType, s.StartTime, s.EndTime })
            .ToListAsync();
        var scheduleBy = schedules.GroupBy(s => (s.EmployeeId, s.ScheduleDate)).ToDictionary(g => g.Key, g => g.First());

        var leaves = (await db.Leaves
                .Where(l => l.Employee != null && l.Employee.CompanyId == companyId
                            && l.LeaveDate >= from && l.LeaveDate <= to && (employeeId == null || l.EmployeeId == employeeId))
                .Select(l => new { l.EmployeeId, l.LeaveDate, l.LeaveTypeCode }).ToListAsync())
            .GroupBy(l => (l.EmployeeId, l.LeaveDate)).ToDictionary(g => g.Key, g => g.First().LeaveTypeCode);

        var branchNames = (await db.Branches.Where(b => b.CompanyId == companyId)
                .Select(b => new { b.Id, b.Name, b.BranchNumber }).ToListAsync())
            .ToDictionary(b => b.Id, b => string.IsNullOrWhiteSpace(b.Name) ? $"Branch {b.BranchNumber}" : b.Name!);

        var withActivity = schedules.Select(s => s.EmployeeId).Concat(leaves.Keys.Select(k => k.EmployeeId)).ToHashSet();
        var employees = (await db.Employees
                .Where(e => e.CompanyId == companyId && (employeeId == null || e.Id == employeeId))
                .Select(e => new { e.Id, e.FirstName, e.LastName, e.BranchId, e.IsActive }).ToListAsync())
            .Where(e => e.IsActive || withActivity.Contains(e.Id))
            .Select(e => (e.Id, Name: $"{e.FirstName} {e.LastName}".Trim(), e.BranchId))
            .OrderBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        string Branch(int id) => branchNames.TryGetValue(id, out var n) ? n : UiText.T("No branch");

        var report = new TableReport { Title = UiText.T("Roster") };

        if (dayCount == 1)
        {
            // ── Daily roster: who works when, per branch ──
            var section = report.AddSection(UiText.F("Roster for {0}", from.ToString("dddd dd/MM/yyyy", CultureInfo.CurrentCulture)),
                (UiText.T("Employee"), 230), (UiText.T("Type"), 130), (UiText.T("From"), 80), (UiText.T("To"), 80), (UiText.T("Hours"), 80));
            int working = 0, rest = 0, absent = 0, onLeave = 0, none = 0;

            var rows = employees.Select(e =>
            {
                scheduleBy.TryGetValue((e.Id, from), out var s);
                var isLeave = leaves.TryGetValue((e.Id, from), out var leaveCode);
                // Sort key: working people first (by start time), then the others.
                var isWork = !isLeave && s is { WorkType: WorkType.Office or WorkType.Home };
                return (e, s, isLeave, leaveCode, isWork);
            }).ToList();

            foreach (var branch in rows.GroupBy(r => Branch(r.e.BranchId)).OrderBy(g => g.Key, StringComparer.CurrentCultureIgnoreCase))
            {
                section.Add(RowStyle.Group, branch.Key);
                foreach (var r in branch.OrderBy(r => r.isWork ? 0 : 1).ThenBy(r => r.s?.StartTime ?? TimeOnly.MaxValue)
                             .ThenBy(r => r.e.Name, StringComparer.CurrentCultureIgnoreCase))
                {
                    if (r.isLeave) { onLeave++; section.Add(r.e.Name, UiText.F("Leave {0}", r.leaveCode)); }
                    else if (r.s == null) { none++; section.Add(RowStyle.Warning, r.e.Name, UiText.T("No schedule")); }
                    else if (r.s.WorkType == WorkType.Rest) { rest++; section.Add(r.e.Name, UiText.T("Rest")); }
                    else if (r.s.WorkType == WorkType.Absent) { absent++; section.Add(r.e.Name, UiText.T("Absent")); }
                    else
                    {
                        working++;
                        var hours = r.s.StartTime is { } a && r.s.EndTime is { } b ? ReportFormat.Hours(SpanHours(a, b)) : "";
                        section.Add(r.e.Name, (r.s.WorkType == WorkType.Home ? UiText.T("Home") : UiText.T("Office")), r.s.StartTime is { } st ? Clock(st) : "",
                            r.s.EndTime is { } et ? Clock(et) : "", hours);
                    }
                }
            }
            report.Summary = UiText.F("{0} working · {1} rest · {2} absent · {3} on leave · {4} without a schedule.", working, rest, absent, onLeave, none);
            return report;
        }

        // ── Weekly / period roster: one row per employee, one column per day ──
        var columns = new List<(string Header, double Width)> { (UiText.T("Employee"), 220) };
        var dates = Enumerable.Range(0, dayCount).Select(i => from.AddDays(i)).ToList();
        columns.AddRange(dates.Select(d => (d.ToString("ddd dd/MM", CultureInfo.CurrentCulture), 108d)));
        columns.Add((UiText.T("Hours"), 80));
        columns.Add((UiText.T("Days"), 60));
        var grid = report.AddSection(UiText.F("Roster {0} – {1}", from.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture), to.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)), columns.ToArray());

        var dayHeads = new int[dayCount];
        var dayHours = new double[dayCount];
        double allHours = 0;
        var allDays = 0;

        foreach (var branch in employees.GroupBy(e => Branch(e.BranchId)).OrderBy(g => g.Key, StringComparer.CurrentCultureIgnoreCase))
        {
            grid.Add(RowStyle.Group, branch.Key);
            foreach (var e in branch)
            {
                var cells = new List<string> { e.Name };
                double hours = 0; var days = 0;
                for (var i = 0; i < dayCount; i++)
                {
                    var date = dates[i];
                    if (leaves.TryGetValue((e.Id, date), out var code)) { cells.Add(UiText.F("Leave {0}", code)); continue; }
                    if (!scheduleBy.TryGetValue((e.Id, date), out var s)) { cells.Add(""); continue; }
                    switch (s.WorkType)
                    {
                        case WorkType.Rest: cells.Add(UiText.T("Rest")); break;
                        case WorkType.Absent: cells.Add(UiText.T("Absent")); break;
                        default:
                            var tag = s.WorkType == WorkType.Home ? " H" : "";
                            if (s.StartTime is { } a && s.EndTime is { } b)
                            {
                                var h = SpanHours(a, b);
                                hours += h; dayHours[i] += h;
                                cells.Add($"{Clock(a)}–{Clock(b)}{tag}");
                            }
                            else cells.Add(UiText.T("Work") + tag);
                            days++; dayHeads[i]++;
                            break;
                    }
                }
                allHours += hours; allDays += days;
                cells.Add(hours > 0 ? ReportFormat.Hours(hours) : "");
                cells.Add(days > 0 ? days.ToString(Inv) : "");
                grid.Add(cells.ToArray());
            }
        }

        if (employees.Count > 0)
        {
            var total = new List<string> { UiText.T("People working") };
            total.AddRange(dayHeads.Select(h => h > 0 ? h.ToString(Inv) : ""));
            total.Add(ReportFormat.Hours(allHours));
            total.Add(allDays.ToString(Inv));
            grid.Add(RowStyle.Total, total.ToArray());
        }
        grid.Note = UiText.T("H = working from home. Hours are the scheduled hours; leave days are shown as Leave.");

        report.Summary = UiText.F("{0} employee(s), {1} day(s).", employees.Count, dayCount) +
                         (limited ? UiText.F(" The roster is limited to {0} days; choose a shorter period to see other days.", MaxDays) : "");
        return report;
    }

    // ── 6. Overtime trend ──────────────────────────────────────────────────

    private async Task<TableReport> BuildOvertimeTrendAsync(int companyId, DateOnly from, DateOnly to, int? employeeId)
    {
        const int MaxMonths = 24;
        var months = new List<DateOnly>();
        for (var m = new DateOnly(from.Year, from.Month, 1); m <= new DateOnly(to.Year, to.Month, 1); m = m.AddMonths(1))
            months.Add(m);
        var trimmed = months.Count > MaxMonths;
        if (trimmed) months = months.Skip(months.Count - MaxMonths).ToList();

        var records = (await _overtime.GetByCompanyAsync(companyId, months[0], to))
            .Where(o => !o.IsCancelled && (employeeId == null || o.EmployeeId == employeeId))
            .ToList();

        var report = new TableReport { Title = UiText.T("Overtime trend") };
        if (records.Count == 0)
        {
            report.Summary = UiText.T("No overtime in this period.");
            return report;
        }

        static string Label(DateOnly m) => $"{m.Month:00}/{m.Year % 100:00}";
        const string Bars = "▁▂▃▄▅▆▇█";
        static string Spark(double[] values)
        {
            var max = values.Max();
            return string.Concat(values.Select(v => max <= 0 ? Bars[0] : Bars[(int)Math.Round(v / max * (Bars.Length - 1))]));
        }

        var columns = new List<(string Header, double Width)> { (UiText.T("Employee"), 220) };
        columns.AddRange(months.Select(m => (Label(m), 72d)));
        columns.Add((UiText.T("Total"), 90));
        columns.Add((UiText.T("Avg / month"), 100));
        columns.Add((UiText.T("Peak month"), 130));
        columns.Add((UiText.T("Trend"), Math.Max(130, months.Count * 14 + 20)));
        var grid = report.AddSection(UiText.T("Overtime hours per month"), columns.ToArray());

        double Value(IEnumerable<OvertimeDto> list, DateOnly m) =>
            list.Where(o => o.OvertimeDate.Year == m.Year && o.OvertimeDate.Month == m.Month).Sum(o => o.HoursWorked);

        var byEmployee = records.GroupBy(o => (o.EmployeeId, o.EmployeeFullName))
            .Select(g => (Name: g.Key.EmployeeFullName, Values: months.Select(m => Value(g, m)).ToArray()))
            .OrderByDescending(e => e.Values.Sum()).ThenBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        string[] Cells(string name, double[] values)
        {
            var total = values.Sum();
            var peak = Array.IndexOf(values, values.Max());
            var cells = new List<string> { name };
            cells.AddRange(values.Select(v => v > 0 ? ReportFormat.Hours(v) : ""));
            cells.Add(ReportFormat.Hours(total));
            cells.Add(ReportFormat.Hours(total / values.Length));
            cells.Add(values[peak] > 0 ? $"{Label(months[peak])} ({ReportFormat.Hours(values[peak])})" : "");
            cells.Add(Spark(values));
            return cells.ToArray();
        }

        foreach (var e in byEmployee) grid.Add(Cells(e.Name, e.Values));
        var totals = months.Select((_, i) => byEmployee.Sum(e => e.Values[i])).ToArray();
        grid.Add(RowStyle.Total, Cells(UiText.T("All employees"), totals));
        grid.Note = UiText.T("The trend bars are scaled per row (each row's own highest month is full height).") +
                    (trimmed ? UiText.F(" Only the last {0} months are shown.", MaxMonths) : "");

        var share = report.AddSection(UiText.T("Share of the total overtime"), (UiText.T("Employee"), 220), (UiText.T("Hours"), 90), (UiText.T("Share"), 90));
        var grand = totals.Sum();
        foreach (var e in byEmployee)
            share.Add(e.Name, ReportFormat.Hours(e.Values.Sum()), (e.Values.Sum() * 100 / grand).ToString("0.0", Inv) + "%");

        var topMonth = Array.IndexOf(totals, totals.Max());
        report.Summary = UiText.F("{0} of overtime for {1} employee(s) in {2} month(s). ", ReportFormat.Hours(grand), byEmployee.Count, months.Count) +
                         UiText.F("Highest month: {0} ({1}).", Label(months[topMonth]), ReportFormat.Hours(totals[topMonth]));
        return report;
    }
}
