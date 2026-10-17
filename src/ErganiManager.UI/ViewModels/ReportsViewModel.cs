using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ErganiManager.Core.Interfaces;
using ErganiManager.Core.Models;
using ErganiManager.UI.Services;

namespace ErganiManager.UI.ViewModels;

public enum ReportKind
{
    Overtime, WorkTime, Compare,
    // analysis reports shown as generic tables
    RoundingImpact, SubmissionStatus, FailureLog, Consistency, Roster, OvertimeTrend, Holidays
}

/// <summary>Reports: overtime, total working time, and declared vs actual time with overtime.</summary>
public partial class ReportsViewModel : ViewModelBase, IAdminSectionViewModel
{
    private readonly IReportService _reports;
    private readonly IEmployeeService _employees;
    private UserSession? _session;

    private OvertimeReport? _overtime;
    private WorkTimeReport? _workTime;
    private TimeSummaryReport? _summary;
    private TableReport? _table;

    public ObservableCollection<EmployeeDto> AvailableEmployees { get; } = new();
    public ObservableCollection<OvertimeReportMonth> OvertimeMonths { get; } = new();
    public ObservableCollection<WorkTimeReportRow> WorkTimeRows { get; } = new();
    public ObservableCollection<TimeSummaryMonth> SummaryMonths { get; } = new();
    public ObservableCollection<ReportEmployeeTotal> EmployeeTotals { get; } = new();
    public ObservableCollection<TableSection> TableSections { get; } = new();

    [ObservableProperty] private bool _hasActiveCompany;
    [ObservableProperty] private string _noCompanyMessage = string.Empty;
    [ObservableProperty] private string _statusMessage = string.Empty;

    [ObservableProperty] private ReportKind _kind = ReportKind.Overtime;

    // Two-way friendly flags for the radio buttons
    public bool IsOvertimeReport { get => Kind == ReportKind.Overtime; set { if (value) Kind = ReportKind.Overtime; } }
    public bool IsWorkTimeReport { get => Kind == ReportKind.WorkTime; set { if (value) Kind = ReportKind.WorkTime; } }
    public bool IsCompareReport  { get => Kind == ReportKind.Compare;  set { if (value) Kind = ReportKind.Compare; } }
    public bool IsRoundingReport     { get => Kind == ReportKind.RoundingImpact;   set { if (value) Kind = ReportKind.RoundingImpact; } }
    public bool IsSubmissionReport   { get => Kind == ReportKind.SubmissionStatus; set { if (value) Kind = ReportKind.SubmissionStatus; } }
    public bool IsFailureLogReport   { get => Kind == ReportKind.FailureLog;       set { if (value) Kind = ReportKind.FailureLog; } }
    public bool IsConsistencyReport  { get => Kind == ReportKind.Consistency;      set { if (value) Kind = ReportKind.Consistency; } }
    public bool IsRosterReport       { get => Kind == ReportKind.Roster;           set { if (value) Kind = ReportKind.Roster; } }
    public bool IsTrendReport        { get => Kind == ReportKind.OvertimeTrend;    set { if (value) Kind = ReportKind.OvertimeTrend; } }
    public bool IsHolidaysReport     { get => Kind == ReportKind.Holidays;         set { if (value) Kind = ReportKind.Holidays; } }

    /// <summary>True for the analysis reports that are shown as generic tables.</summary>
    public bool IsTableReport => Kind >= ReportKind.RoundingImpact;

    [ObservableProperty] private DateTimeOffset? _fromDate =
        new DateTimeOffset(new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1));   // start of the current month
    [ObservableProperty] private DateTimeOffset? _toDate = DateTimeOffset.Now;
    [ObservableProperty] private EmployeeDto? _selectedEmployee;

    [ObservableProperty] private string _grandTotalText = "0:00";
    [ObservableProperty] private string _nightTotalText = "0:00";
    /// <summary>Working-time report: grand total of the hours really worked (from the scans).</summary>
    [ObservableProperty] private string _actualTotalText = "0:00";
    [ObservableProperty] private bool _hasData;
    [ObservableProperty] private bool _hasTableData;

    /// <summary>The export button works for the classic reports and for the table reports.</summary>
    public bool CanExport => HasData || HasTableData;

    partial void OnHasDataChanged(bool value) => OnPropertyChanged(nameof(CanExport));
    partial void OnHasTableDataChanged(bool value) => OnPropertyChanged(nameof(CanExport));

    public string TotalsLabel => Kind switch
    {
        ReportKind.Compare  => Loc["Vm.Total actual working time (from scans, h:mm)"],
        ReportKind.WorkTime => Loc["Vm.Total scheduled working time (h:mm)"],
        _                   => Loc["Vm.Grand total (h:mm)"]
    };

    public string ActualTotalsLabel => Loc["Vm.Total actual working time (from scans, h:mm)"];

    public string ReportTitle => Kind switch
    {
        ReportKind.Overtime => Loc["Vm.Overtime per employee / month"],
        ReportKind.WorkTime => Loc["Vm.Total working time per employee / month"],
        ReportKind.Compare  => Loc["Vm.Declared vs actual time, with overtime"],
        ReportKind.RoundingImpact   => Loc["Vm.Rounding impact"],
        ReportKind.SubmissionStatus => Loc["Vm.Submission status"],
        ReportKind.FailureLog       => Loc["Vm.Retry and failure log"],
        ReportKind.Consistency      => Loc["Vm.Rest-day / schedule consistency"],
        ReportKind.Roster           => Loc["Vm.Roster (same From and To date = daily roster; otherwise one column per day, up to 31 days)"],
        ReportKind.Holidays         => Loc["Vm.Holidays by category"],
        _                           => Loc["Vm.Overtime trend"]
    };

    public ReportsViewModel(IReportService reports, IEmployeeService employees)
    {
        _reports = reports;
        _employees = employees;
    }

    partial void OnKindChanged(ReportKind value)
    {
        OnPropertyChanged(nameof(IsOvertimeReport));
        OnPropertyChanged(nameof(IsWorkTimeReport));
        OnPropertyChanged(nameof(IsCompareReport));
        OnPropertyChanged(nameof(IsRoundingReport));
        OnPropertyChanged(nameof(IsSubmissionReport));
        OnPropertyChanged(nameof(IsFailureLogReport));
        OnPropertyChanged(nameof(IsConsistencyReport));
        OnPropertyChanged(nameof(IsRosterReport));
        OnPropertyChanged(nameof(IsTrendReport));
        OnPropertyChanged(nameof(IsHolidaysReport));
        OnPropertyChanged(nameof(IsTableReport));
        OnPropertyChanged(nameof(ReportTitle));
        OnPropertyChanged(nameof(TotalsLabel));
        _ = GenerateAsync();
    }

    public void Initialize(UserSession session)
    {
        _session = session;
        HasActiveCompany = session.CompanyId.HasValue;
        NoCompanyMessage = session.CompanyId.HasValue ? string.Empty : Loc["Vm.Select a company first."];
        if (HasActiveCompany) _ = LoadEmployeesAsync();
    }

    private async Task LoadEmployeesAsync()
    {
        if (_session?.CompanyId is not int companyId) return;
        var list = await _employees.GetByCompanyAsync(companyId);

        AvailableEmployees.Clear();
        AvailableEmployees.Add(new EmployeeDto { Id = 0, FirstName = Loc["Vm.All employees"] });
        foreach (var e in list.OrderBy(e => e.LastName).ThenBy(e => e.FirstName)) AvailableEmployees.Add(e);
        SelectedEmployee = AvailableEmployees[0];

        await GenerateAsync();
    }

    [RelayCommand]
    private async Task GenerateAsync()
    {
        if (_session?.CompanyId is not int companyId) return;

        var from = DateOnly.FromDateTime((FromDate ?? DateTimeOffset.Now).LocalDateTime);
        var to   = DateOnly.FromDateTime((ToDate ?? DateTimeOffset.Now).LocalDateTime);
        if (to < from) { StatusMessage = Loc["Vm.The 'To' date is before the 'From' date."]; return; }

        int? employeeId = SelectedEmployee is { Id: > 0 } e ? e.Id : null;

        try
        {
            EmployeeTotals.Clear();
            TableSections.Clear();
            HasTableData = false;

            if (IsTableReport)
            {
                _table = await _reports.GetTableReportAsync(
                    Kind switch
                    {
                        ReportKind.RoundingImpact   => TableReportKind.RoundingImpact,
                        ReportKind.SubmissionStatus => TableReportKind.SubmissionStatus,
                        ReportKind.FailureLog       => TableReportKind.FailureLog,
                        ReportKind.Consistency      => TableReportKind.Consistency,
                        ReportKind.Roster           => TableReportKind.Roster,
                        ReportKind.Holidays         => TableReportKind.Holidays,
                        _                           => TableReportKind.OvertimeTrend
                    }, companyId, from, to, employeeId);
                foreach (var s in _table.Sections.Where(s => s.HasRows)) TableSections.Add(s);
                HasData = false;
                GrandTotalText = "0:00";
                NightTotalText = "0:00";
                HasTableData = _table.HasRows;
                StatusMessage = _table.Summary;
                return;
            }

            _table = null;
            if (Kind == ReportKind.Compare)
            {
                _summary = await _reports.GetTimeSummaryReportAsync(companyId, from, to, employeeId);
                SummaryMonths.Clear();
                foreach (var m in _summary.Months) SummaryMonths.Add(m);
                foreach (var g in _summary.Months.GroupBy(m => (m.EmployeeId, m.EmployeeName)))
                {
                    EmployeeTotals.Add(new ReportEmployeeTotal
                        { EmployeeId = g.Key.EmployeeId, EmployeeName = g.Key.EmployeeName, Hours = g.Sum(m => m.ActualHours), NightHours = g.Sum(m => m.NightHours) });
                }
                GrandTotalText = ReportFormat.Hours(_summary.ActualHours);
                NightTotalText = ReportFormat.Hours(_summary.NightHours);
                HasData = _summary.Months.Count > 0;
                StatusMessage = string.Format(Loc["Vm.Declared {0} · Actual (from scans) {1} · Night {2} · Overtime {3}. "], ReportFormat.Hours(_summary.DeclaredHours), ReportFormat.Hours(_summary.ActualHours), ReportFormat.Hours(_summary.NightHours), ReportFormat.Hours(_summary.OvertimeHours)) +
                                Loc["Vm.Actual = first arrival to last departure of the day; '?' = a scan is missing."];
            }
            else if (IsOvertimeReport)
            {
                _overtime = await _reports.GetOvertimeReportAsync(companyId, from, to, employeeId);
                OvertimeMonths.Clear();
                foreach (var m in _overtime.Months) OvertimeMonths.Add(m);
                foreach (var t in _overtime.EmployeeTotals) EmployeeTotals.Add(t);
                GrandTotalText = ReportFormat.Hours(_overtime.GrandTotalHours);
                NightTotalText = "0:00";
                HasData = _overtime.Months.Count > 0;
                StatusMessage = string.Format(Loc["Vm.{0} overtime record(s) in {1} employee-month group(s)."], _overtime.Months.Sum(m => m.Records.Count), _overtime.Months.Count);
            }
            else
            {
                _workTime = await _reports.GetWorkingTimeReportAsync(companyId, from, to, employeeId);
                WorkTimeRows.Clear();
                foreach (var r in _workTime.Rows) WorkTimeRows.Add(r);
                foreach (var t in _workTime.EmployeeTotals) EmployeeTotals.Add(t);
                GrandTotalText = ReportFormat.Hours(_workTime.GrandTotalHours);
                ActualTotalText = ReportFormat.Hours(_workTime.GrandActualHours);
                NightTotalText = "0:00";
                HasData = _workTime.Rows.Count > 0;
                StatusMessage = string.Format(Loc["Vm.{0} employee-month row(s). Scheduled time is from the Office/Home schedule; actual time is from the clock-in/out scans."], _workTime.Rows.Count);
            }
        }
        catch (Exception ex) { StatusMessage = $"❌ {ex.Message}"; }
    }

    /// <summary>Selected From/To dates; false (with a status message) when the range is invalid.</summary>
    private bool TryGetRange(out DateOnly from, out DateOnly to)
    {
        from = DateOnly.FromDateTime((FromDate ?? DateTimeOffset.Now).LocalDateTime);
        to   = DateOnly.FromDateTime((ToDate ?? DateTimeOffset.Now).LocalDateTime);
        if (to >= from) return true;
        StatusMessage = Loc["Vm.The 'To' date is before the 'From' date."];
        return false;
    }

    public string ExcelSuggestedFileName =>
        TryGetRange(out var f, out var t) ? $"working-hours-{f:yyyy-MM-dd}_{t:yyyy-MM-dd}" : "working-hours";

    /// <summary>
    /// Writes one Excel workbook for the current company / dates / employee filter:
    /// scheduled vs actual hours, Saturday hours and Sunday hours (one sheet each).
    /// </summary>
    public async Task ExportTimeWorkbookAsync(string path)
    {
        if (_session?.CompanyId is not int companyId) return;
        if (!TryGetRange(out var from, out var to)) return;
        int? employeeId = SelectedEmployee is { Id: > 0 } e ? e.Id : null;

        try
        {
            var report = await _reports.GetTimeSummaryReportAsync(companyId, from, to, employeeId);
            var rows = AvailableEmployees.Where(x => x.Id > 0 && (employeeId == null || x.Id == employeeId)).ToList();
            var holidays = await _reports.GetTableReportAsync(TableReportKind.Holidays, companyId, from, to, employeeId);

            TimeWorkbookExporter.Save(path, report, from, to, rows, holidays);
            StatusMessage = string.Format(Loc["Vm.Excel Saved"], path);
        }
        catch (Exception ex) { StatusMessage = $"❌ {ex.Message}"; }   // e.g. the file is open in Excel
    }

    public string SuggestedFileName =>
        $"{Kind switch { ReportKind.Overtime => "overtime-report", ReportKind.WorkTime => "working-time-report", ReportKind.Compare => "declared-vs-actual-report", ReportKind.RoundingImpact => "rounding-impact", ReportKind.SubmissionStatus => "submission-status", ReportKind.FailureLog => "retry-failure-log", ReportKind.Consistency => "schedule-consistency", ReportKind.Roster => "roster", ReportKind.Holidays => "holidays", _ => "overtime-trend" }}-{DateTime.Now:yyyyMMdd}.csv";

    /// <summary>
    /// Difference for the CSV. A leading '-' or '+' makes Excel treat the cell as a formula (#VALUE!),
    /// so a negative difference uses the typographic minus (U+2212) and a positive one has no sign.
    /// </summary>
    private static string CsvDiff(double? diff) => diff is not { } d
        ? string.Empty
        : (d < 0 ? "\u2212" : string.Empty) + ReportFormat.Hours(Math.Abs(d));

    /// <summary>The current report as CSV (semicolon separated so Greek Excel opens it directly).</summary>
    public string BuildCsv()
    {
        if (IsTableReport) return _table?.ToCsv() ?? string.Empty;

        var sb = new StringBuilder();
        static string Q(string? v) => "\"" + (v ?? string.Empty).Replace("\"", "\"\"") + "\"";
        var inv = CultureInfo.InvariantCulture;

        if (Kind == ReportKind.Compare && _summary != null)
        {
            sb.AppendLine(string.Join(";", new[] { Loc["Vm.Employee"], Loc["Vm.Month"], Loc["Vm.Date"], Loc["Vm.Declared"], Loc["Vm.Declared hours"], Loc["Vm.Actual"], Loc["Vm.Actual hours"], Loc["Vm.Night hours"], Loc["Vm.Rounded hours"], Loc["Vm.Rounded in → out"], Loc["Vm.Difference"], Loc["Vm.Overtime"], Loc["Vm.Overtime hours"] }));
            foreach (var m in _summary.Months)
            {
                foreach (var d in m.Days)
                    sb.AppendLine(string.Join(";", Q(m.EmployeeName), m.MonthLabel, d.Date.ToString("dd/MM/yyyy", inv),
                        Q(d.DeclaredText), d.DeclaredHoursText, Q(d.ActualText), d.ActualHoursText, d.NightHoursText, d.RoundedHoursText, Q(d.RoundedInOutCsvText), CsvDiff(d.DiffHours),
                        Q(d.OvertimeRanges), d.OvertimeHours > 0 ? ReportFormat.Hours(d.OvertimeHours) : string.Empty));
                sb.AppendLine(string.Join(";", Q(m.EmployeeName), m.MonthLabel, Q(Loc["Vm.MONTH TOTAL"]), "",
                    ReportFormat.Hours(m.DeclaredHours), "", ReportFormat.Hours(m.ActualHours),
                    ReportFormat.Hours(m.NightHours), ReportFormat.Hours(m.RoundedHours), "", CsvDiff(m.DiffHours), "", ReportFormat.Hours(m.OvertimeHours)));
            }
            sb.AppendLine(string.Join(";", Q(Loc["Vm.ALL EMPLOYEES"]), "", Q(Loc["Vm.GRAND TOTAL"]), "",
                ReportFormat.Hours(_summary.DeclaredHours), "", ReportFormat.Hours(_summary.ActualHours), ReportFormat.Hours(_summary.NightHours), ReportFormat.Hours(_summary.RoundedHours), "", "", "", ReportFormat.Hours(_summary.OvertimeHours)));
        }
        else if (IsOvertimeReport && _overtime != null)
        {
            sb.AppendLine(string.Join(";", new[] { Loc["Vm.Employee"], Loc["Vm.Month"], Loc["Vm.Date"], Loc["Vm.From"], Loc["Vm.To"], Loc["Vm.Hours"], Loc["Vm.Justification"], Loc["Vm.Sent to Ergani"], Loc["Vm.Protocol"], Loc["Vm.Month total"] }));
            foreach (var m in _overtime.Months)
            {
                foreach (var r in m.Records)
                    sb.AppendLine(string.Join(";", Q(m.EmployeeName), m.MonthLabel,
                        r.OvertimeDate.ToString("dd/MM/yyyy", inv), r.StartTime.ToString("HH:mm", inv), r.EndTime.ToString("HH:mm", inv),
                        ReportFormat.Hours(r.HoursWorked), Q(r.JustificationLabel),
                        r.SubmittedToErgani ? Loc["Vm.Yes"] : Loc["Vm.No"], Q(r.Protocol), string.Empty));
                sb.AppendLine(string.Join(";", Q(m.EmployeeName), m.MonthLabel, "", "", "", "", Q(Loc["Vm.TOTAL"]), "", "", m.TotalText));
            }
            sb.AppendLine();
            foreach (var t in _overtime.EmployeeTotals)
                sb.AppendLine(string.Join(";", Q(t.EmployeeName), "", "", "", "", "", Q(Loc["Vm.EMPLOYEE TOTAL"]), "", "", t.HoursText));
            sb.AppendLine(string.Join(";", Q(Loc["Vm.ALL EMPLOYEES"]), "", "", "", "", "", Q(Loc["Vm.GRAND TOTAL"]), "", "", GrandTotalText));
        }
        else if (_workTime != null)
        {
            sb.AppendLine(string.Join(";", new[] { Loc["Vm.Employee"], Loc["Vm.Month"], Loc["Vm.Days"], Loc["Vm.Scheduled working time"], Loc["Vm.Actual worked"], Loc["Vm.Normal (up to 8h/day)"], Loc["Vm.Over 8h/day"] }));
            foreach (var r in _workTime.Rows)
                sb.AppendLine(string.Join(";", Q(r.EmployeeName), r.MonthLabel, r.DaysWorked, r.TotalText, r.ActualText, r.NormalText, r.OverText));
            sb.AppendLine();
            foreach (var t in _workTime.EmployeeTotals)
                sb.AppendLine(string.Join(";", Q(t.EmployeeName), Loc["Vm.EMPLOYEE TOTAL"], "", t.HoursText, t.ActualHoursText, "", ""));
            sb.AppendLine(string.Join(";", Q(Loc["Vm.ALL EMPLOYEES"]), Loc["Vm.GRAND TOTAL"], "", GrandTotalText, ActualTotalText, "", ""));
        }
        return sb.ToString();
    }
}
