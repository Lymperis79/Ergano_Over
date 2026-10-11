using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ErganiManager.Core.Interfaces;
using ErganiManager.Core.Models;

namespace ErganiManager.UI.ViewModels;

public enum ReportKind { Overtime, WorkTime, Compare }

/// <summary>Reports: overtime, total working time, and declared vs actual time with overtime.</summary>
public partial class ReportsViewModel : ViewModelBase, IAdminSectionViewModel
{
    private readonly IReportService _reports;
    private readonly IEmployeeService _employees;
    private UserSession? _session;

    private OvertimeReport? _overtime;
    private WorkTimeReport? _workTime;
    private TimeSummaryReport? _summary;

    public ObservableCollection<EmployeeDto> AvailableEmployees { get; } = new();
    public ObservableCollection<OvertimeReportMonth> OvertimeMonths { get; } = new();
    public ObservableCollection<WorkTimeReportRow> WorkTimeRows { get; } = new();
    public ObservableCollection<TimeSummaryMonth> SummaryMonths { get; } = new();
    public ObservableCollection<ReportEmployeeTotal> EmployeeTotals { get; } = new();

    [ObservableProperty] private bool _hasActiveCompany;
    [ObservableProperty] private string _noCompanyMessage = string.Empty;
    [ObservableProperty] private string _statusMessage = string.Empty;

    [ObservableProperty] private ReportKind _kind = ReportKind.Overtime;

    // Two-way friendly flags for the radio buttons
    public bool IsOvertimeReport { get => Kind == ReportKind.Overtime; set { if (value) Kind = ReportKind.Overtime; } }
    public bool IsWorkTimeReport { get => Kind == ReportKind.WorkTime; set { if (value) Kind = ReportKind.WorkTime; } }
    public bool IsCompareReport  { get => Kind == ReportKind.Compare;  set { if (value) Kind = ReportKind.Compare; } }

    [ObservableProperty] private DateTimeOffset? _fromDate =
        new DateTimeOffset(new DateTime(DateTime.Today.Year, 1, 1));
    [ObservableProperty] private DateTimeOffset? _toDate = DateTimeOffset.Now;
    [ObservableProperty] private EmployeeDto? _selectedEmployee;

    [ObservableProperty] private string _grandTotalText = "0:00";
    [ObservableProperty] private string _nightTotalText = "0:00";
    [ObservableProperty] private bool _hasData;

    public string TotalsLabel => Kind == ReportKind.Compare
        ? "Total actual working time (from scans, h:mm)"
        : "Grand total (h:mm)";

    public string ReportTitle => Kind switch
    {
        ReportKind.Overtime => "Overtime per employee / month",
        ReportKind.WorkTime => "Total working time per employee / month",
        _                   => "Declared vs actual time, with overtime"
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
        OnPropertyChanged(nameof(ReportTitle));
        OnPropertyChanged(nameof(TotalsLabel));
        _ = GenerateAsync();
    }

    public void Initialize(UserSession session)
    {
        _session = session;
        HasActiveCompany = session.CompanyId.HasValue;
        NoCompanyMessage = session.CompanyId.HasValue ? string.Empty : "Select a company first.";
        if (HasActiveCompany) _ = LoadEmployeesAsync();
    }

    private async Task LoadEmployeesAsync()
    {
        if (_session?.CompanyId is not int companyId) return;
        var list = await _employees.GetByCompanyAsync(companyId);

        AvailableEmployees.Clear();
        AvailableEmployees.Add(new EmployeeDto { Id = 0, FirstName = "All employees" });
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
        if (to < from) { StatusMessage = "The 'To' date is before the 'From' date."; return; }

        int? employeeId = SelectedEmployee is { Id: > 0 } e ? e.Id : null;

        try
        {
            EmployeeTotals.Clear();
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
                StatusMessage = $"Declared {ReportFormat.Hours(_summary.DeclaredHours)} · Actual (from scans) {ReportFormat.Hours(_summary.ActualHours)} · Night {ReportFormat.Hours(_summary.NightHours)} · Overtime {ReportFormat.Hours(_summary.OvertimeHours)}. " +
                                "Actual = first arrival to last departure of the day; '?' = a scan is missing.";
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
                StatusMessage = $"{_overtime.Months.Sum(m => m.Records.Count)} overtime record(s) in {_overtime.Months.Count} employee-month group(s).";
            }
            else
            {
                _workTime = await _reports.GetWorkingTimeReportAsync(companyId, from, to, employeeId);
                WorkTimeRows.Clear();
                foreach (var r in _workTime.Rows) WorkTimeRows.Add(r);
                foreach (var t in _workTime.EmployeeTotals) EmployeeTotals.Add(t);
                GrandTotalText = ReportFormat.Hours(_workTime.GrandTotalHours);
                NightTotalText = "0:00";
                HasData = _workTime.Rows.Count > 0;
                StatusMessage = $"{_workTime.Rows.Count} employee-month row(s). Based on the Office/Home schedule days.";
            }
        }
        catch (Exception ex) { StatusMessage = $"❌ {ex.Message}"; }
    }

    public string SuggestedFileName =>
        $"{Kind switch { ReportKind.Overtime => "overtime-report", ReportKind.WorkTime => "working-time-report", _ => "declared-vs-actual-report" }}-{DateTime.Now:yyyyMMdd}.csv";

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
        var sb = new StringBuilder();
        static string Q(string? v) => "\"" + (v ?? string.Empty).Replace("\"", "\"\"") + "\"";
        var inv = CultureInfo.InvariantCulture;

        if (Kind == ReportKind.Compare && _summary != null)
        {
            sb.AppendLine("Employee;Month;Date;Declared;Declared hours;Actual;Actual hours;Night hours;Rounded hours;Rounded in → out;Difference;Overtime;Overtime hours");
            foreach (var m in _summary.Months)
            {
                foreach (var d in m.Days)
                    sb.AppendLine(string.Join(";", Q(m.EmployeeName), m.MonthLabel, d.Date.ToString("dd/MM/yyyy", inv),
                        Q(d.DeclaredText), d.DeclaredHoursText, Q(d.ActualText), d.ActualHoursText, d.NightHoursText, d.RoundedHoursText, d.RoundedInOutText, CsvDiff(d.DiffHours),
                        Q(d.OvertimeRanges), d.OvertimeHours > 0 ? ReportFormat.Hours(d.OvertimeHours) : string.Empty));
                sb.AppendLine(string.Join(";", Q(m.EmployeeName), m.MonthLabel, Q("MONTH TOTAL"), "",
                    ReportFormat.Hours(m.DeclaredHours), "", ReportFormat.Hours(m.ActualHours),
                    ReportFormat.Hours(m.NightHours), ReportFormat.Hours(m.RoundedHours), "", CsvDiff(m.DiffHours), "", ReportFormat.Hours(m.OvertimeHours)));
            }
            sb.AppendLine(string.Join(";", Q("ALL EMPLOYEES"), "", Q("GRAND TOTAL"), "",
                ReportFormat.Hours(_summary.DeclaredHours), "", ReportFormat.Hours(_summary.ActualHours), ReportFormat.Hours(_summary.NightHours), ReportFormat.Hours(_summary.RoundedHours), "", "", "", ReportFormat.Hours(_summary.OvertimeHours)));
        }
        else if (IsOvertimeReport && _overtime != null)
        {
            sb.AppendLine("Employee;Month;Date;From;To;Hours;Justification;Sent to Ergani;Protocol;Month total");
            foreach (var m in _overtime.Months)
            {
                foreach (var r in m.Records)
                    sb.AppendLine(string.Join(";", Q(m.EmployeeName), m.MonthLabel,
                        r.OvertimeDate.ToString("dd/MM/yyyy", inv), r.StartTime.ToString("HH:mm", inv), r.EndTime.ToString("HH:mm", inv),
                        ReportFormat.Hours(r.HoursWorked), Q(r.JustificationLabel),
                        r.SubmittedToErgani ? "Yes" : "No", Q(r.Protocol), string.Empty));
                sb.AppendLine(string.Join(";", Q(m.EmployeeName), m.MonthLabel, "", "", "", "", Q("TOTAL"), "", "", m.TotalText));
            }
            sb.AppendLine();
            foreach (var t in _overtime.EmployeeTotals)
                sb.AppendLine(string.Join(";", Q(t.EmployeeName), "", "", "", "", "", Q("EMPLOYEE TOTAL"), "", "", t.HoursText));
            sb.AppendLine(string.Join(";", Q("ALL EMPLOYEES"), "", "", "", "", "", Q("GRAND TOTAL"), "", "", GrandTotalText));
        }
        else if (_workTime != null)
        {
            sb.AppendLine("Employee;Month;Days;Total working time;Normal (up to 8h/day);Over 8h/day");
            foreach (var r in _workTime.Rows)
                sb.AppendLine(string.Join(";", Q(r.EmployeeName), r.MonthLabel, r.DaysWorked, r.TotalText, r.NormalText, r.OverText));
            sb.AppendLine();
            foreach (var t in _workTime.EmployeeTotals)
                sb.AppendLine(string.Join(";", Q(t.EmployeeName), "EMPLOYEE TOTAL", "", t.HoursText, "", ""));
            sb.AppendLine(string.Join(";", Q("ALL EMPLOYEES"), "GRAND TOTAL", "", GrandTotalText, "", ""));
        }
        return sb.ToString();
    }
}
