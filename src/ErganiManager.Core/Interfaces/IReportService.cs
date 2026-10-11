namespace ErganiManager.Core.Interfaces;

/// <summary>Formatting shared by the reports (hours as H:mm so 7.5h shows 7:30).</summary>
public static class ReportFormat
{
    public static string Hours(double hours)
    {
        var total = TimeSpan.FromHours(hours);
        return $"{(int)total.TotalHours}:{total.Minutes:00}";
    }

    public static string MonthLabel(int year, int month) => $"{month:00}/{year}";
}

/// <summary>Total for one employee (all months of the report).</summary>
public sealed class ReportEmployeeTotal
{
    public int EmployeeId { get; init; }
    public string EmployeeName { get; init; } = string.Empty;
    public double Hours { get; init; }
    public double NightHours { get; init; }
    public string HoursText => ReportFormat.Hours(Hours);
    public string NightHoursText => ReportFormat.Hours(NightHours);
}

/// <summary>All overtime of one employee in one month, with the month total.</summary>
public sealed class OvertimeReportMonth
{
    public int EmployeeId { get; init; }
    public string EmployeeName { get; init; } = string.Empty;
    public int Year { get; init; }
    public int Month { get; init; }
    public string MonthLabel => ReportFormat.MonthLabel(Year, Month);
    public List<OvertimeDto> Records { get; init; } = new();
    public double TotalHours { get; init; }
    public string TotalText => ReportFormat.Hours(TotalHours);
    public string Header => $"{EmployeeName} — {MonthLabel}";
}

/// <summary>Working time of one employee in one month, taken from the schedules.</summary>
public sealed class WorkTimeReportRow
{
    public int EmployeeId { get; init; }
    public string EmployeeName { get; init; } = string.Empty;
    public int Year { get; init; }
    public int Month { get; init; }
    public string MonthLabel => ReportFormat.MonthLabel(Year, Month);
    public int DaysWorked { get; init; }

    /// <summary>Total scheduled working time (Office + Home days).</summary>
    public double TotalHours { get; init; }
    /// <summary>The part of it within the normal 8 hours per day.</summary>
    public double NormalHours { get; init; }
    /// <summary>The part of it over 8 hours per day (what goes to Ergani as overtime).</summary>
    public double OverHours { get; init; }

    public string TotalText  => ReportFormat.Hours(TotalHours);
    public string NormalText => ReportFormat.Hours(NormalHours);
    public string OverText   => ReportFormat.Hours(OverHours);
}

public sealed class OvertimeReport
{
    public List<OvertimeReportMonth> Months { get; init; } = new();
    public List<ReportEmployeeTotal> EmployeeTotals { get; init; } = new();
    public double GrandTotalHours { get; init; }
}

public sealed class WorkTimeReport
{
    public List<WorkTimeReportRow> Rows { get; init; } = new();
    public List<ReportEmployeeTotal> EmployeeTotals { get; init; } = new();
    public double GrandTotalHours { get; init; }
}


/// <summary>One clock-in / clock-out pair of a day (either side may be missing).</summary>
public sealed class ScanPair
{
    public DateTime? In { get; init; }
    public DateTime? Out { get; init; }
    public double? Hours => In is { } i && Out is { } o && o > i ? (o - i).TotalHours : null;

    public string Text =>
        $"{(In is { } a ? a.ToString("HH:mm") : "?")} → {(Out is { } b ? b.ToString("HH:mm") : "?")}";
    public string HoursText => Hours is { } h ? ReportFormat.Hours(h) : "—";
    public string Line => $"{Text}  ({HoursText})";
}

/// <summary>One day of an employee: declared (schedule) vs actual (scans) vs overtime.</summary>
public sealed class DayTimeRow
{
    public DateOnly Date { get; init; }
    public string DateText => Date.ToString("ddd dd/MM/yyyy");

    public string DeclaredKind { get; init; } = string.Empty;           // Office / Home / Rest / Absent / ""
    public TimeOnly? DeclaredFrom { get; init; }
    public TimeOnly? DeclaredTo { get; init; }
    public double? DeclaredHours { get; init; }

    /// <summary>Every clock-in / clock-out pair of the day, in time order.</summary>
    public List<ScanPair> Pairs { get; init; } = new();
    public DateTime? ActualArrival => Pairs.Select(p => p.In).FirstOrDefault(x => x != null);
    public DateTime? ActualDeparture => Pairs.Select(p => p.Out).LastOrDefault(x => x != null);
    /// <summary>Sum of the complete pairs.</summary>
    public double? ActualHours { get; init; }

    /// <summary>Actual worked time that falls inside the configured night-work window.</summary>
    public double NightHours { get; init; }
    public string NightHoursText => NightHours > 0 ? ReportFormat.Hours(NightHours) : string.Empty;

    /// <summary>Actual hours rounded with the rules from Administration (null when there is no actual time).</summary>
    public double? RoundedHours { get; init; }
    public string RoundedHoursText => RoundedHours is { } r ? ReportFormat.Hours(r) : string.Empty;

    /// <summary>
    /// Clock-out after rounding: the real last clock-out moved by the rounding (rounded − actual hours),
    /// so with one in/out pair it is simply first clock-in + rounded hours.
    /// </summary>
    public DateTime? RoundedClockOut =>
        ActualDeparture is { } dep && ActualHours is { } act && RoundedHours is { } rnd
            ? dep + TimeSpan.FromHours(rnd - act)
            : null;

    /// <summary>First exact clock-in → rounded clock-out, e.g. "07:56 → 15:56".</summary>
    public string RoundedInOutText =>
        ActualArrival is { } a && RoundedClockOut is { } o ? $"{a:HH:mm} → {o:HH:mm}" : string.Empty;

    /// <summary>Actual − declared, only when both are known.</summary>
    public double? DiffHours { get; init; }

    public string OvertimeRanges { get; init; } = string.Empty;
    public double OvertimeHours { get; init; }

    public string DeclaredText => DeclaredFrom is { } f && DeclaredTo is { } t
        ? $"{f:HH\\:mm} → {t:HH\\:mm}"
        : (DeclaredKind.Length > 0 ? DeclaredKind : "—");
    public string DeclaredHoursText => DeclaredHours is { } h ? ReportFormat.Hours(h) : string.Empty;

    public string ActualText => Pairs.Count == 0 ? "—" : string.Join(" | ", Pairs.Select(p => p.Text));
    public string ActualHoursText => ActualHours is { } h ? ReportFormat.Hours(h) : string.Empty;

    public string DiffText => DiffHours is { } d
        ? (d < 0 ? "-" : "+") + ReportFormat.Hours(Math.Abs(d))
        : string.Empty;
    public bool IsLate => DiffHours is < -0.0833;     // more than 5 minutes short

    public string OvertimeText => OvertimeHours > 0 ? $"{OvertimeRanges} ({ReportFormat.Hours(OvertimeHours)})" : string.Empty;
}

/// <summary>Declared / actual / overtime of one employee in one month with the month totals.</summary>
public sealed class TimeSummaryMonth
{
    public int EmployeeId { get; init; }
    public string EmployeeName { get; init; } = string.Empty;
    public int Year { get; init; }
    public int Month { get; init; }
    public string MonthLabel => ReportFormat.MonthLabel(Year, Month);
    public List<DayTimeRow> Days { get; init; } = new();

    public double DeclaredHours { get; init; }
    public double ActualHours { get; init; }
    public double DiffHours { get; init; }
    public double OvertimeHours { get; init; }
    public double RoundedHours { get; init; }
    public double NightHours { get; init; }
    public string NightHoursText => ReportFormat.Hours(NightHours);

    public string Header => $"{EmployeeName} — {MonthLabel}";
    public string TotalsText =>
        $"Declared {ReportFormat.Hours(DeclaredHours)}  ·  Actual {ReportFormat.Hours(ActualHours)}  ·  Rounded {ReportFormat.Hours(RoundedHours)}  ·  Night {ReportFormat.Hours(NightHours)}  ·  Overtime {ReportFormat.Hours(OvertimeHours)}";
}

public sealed class TimeSummaryReport
{
    public List<TimeSummaryMonth> Months { get; init; } = new();
    public double DeclaredHours { get; init; }
    public double ActualHours { get; init; }
    public double OvertimeHours { get; init; }
    public double RoundedHours { get; init; }
    public double NightHours { get; init; }
}

public interface IReportService
{
    /// <summary>Overtime records (cancelled ones excluded), grouped per employee and month.</summary>
    Task<OvertimeReport> GetOvertimeReportAsync(int companyId, DateOnly from, DateOnly to, int? employeeId);

    /// <summary>Total working time per employee and month from the Office/Home schedule days.</summary>
    Task<WorkTimeReport> GetWorkingTimeReportAsync(int companyId, DateOnly from, DateOnly to, int? employeeId);

    /// <summary>Per day: declared schedule, actual time from the scans and overtime side by side.</summary>
    Task<TimeSummaryReport> GetTimeSummaryReportAsync(int companyId, DateOnly from, DateOnly to, int? employeeId);
}
