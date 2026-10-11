using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ErganiManager.Core.Interfaces;
using ErganiManager.Core.Models;
using ErganiManager.UI.Services;

namespace ErganiManager.UI.ViewModels;

public enum ScheduleViewMode { Month, Week }

/// <summary>One visual cell in the calendar grid.</summary>
public partial class CalendarCellViewModel : ViewModelBase
{
    public bool IsInMonth { get; init; }
    public DateOnly? Date { get; init; }

    [ObservableProperty] private AppWorkType? _workType;
    [ObservableProperty] private string _timeRangeText = string.Empty;
    /// <summary>Second line of the cell: the overtime of the day ("⏱ 17:00–18:00"), shown apart from the work time.</summary>
    [ObservableProperty] private string _overtimeText = string.Empty;
    public bool HasOvertimeText => !string.IsNullOrWhiteSpace(OvertimeText);
    [ObservableProperty] private bool _hasSchedule;
    [ObservableProperty] private bool _isToday;
    [ObservableProperty] private bool _isSelected;
    [ObservableProperty] private bool _isSubmitted;
    [ObservableProperty] private bool _isErganiImported;
    [ObservableProperty] private string _officialHolidayText = string.Empty;
    [ObservableProperty] private string _userHolidayText = string.Empty;
    [ObservableProperty] private string _holidayTooltip = string.Empty;

    public bool HasOfficialHoliday => !string.IsNullOrWhiteSpace(OfficialHolidayText);
    public bool HasUserHoliday => !string.IsNullOrWhiteSpace(UserHolidayText);
    public bool HasHoliday => HasOfficialHoliday || HasUserHoliday;

    public string DayNumberText => Date?.Day.ToString() ?? string.Empty;
    public string WorkTypeIcon => WorkType switch
    {
        AppWorkType.Office => "🏢",
        AppWorkType.Home   => "🏠",
        AppWorkType.Rest   => "💤",
        AppWorkType.Absent => "❌",
        _                  => ""
    };
}

/// <summary>One line of the Ergani schedule submission log.</summary>
public class ScheduleLogRow
{
    public int Id { get; init; }
    public DateOnly? ScheduleDate { get; init; }
    /// <summary>UTC time of the attempt.</summary>
    public DateTime SubmissionDate { get; init; }
    public bool Success { get; init; }
    public string? Protocol { get; init; }
    public string? ErrorMessage { get; init; }
    public int? HttpStatusCode { get; init; }
    public string? ResponseRawJson { get; init; }

    public string StatusIcon => Success ? "✅" : "❌";
    public string ScheduleDateText => ScheduleDate?.ToString("dd/MM/yyyy") ?? "—";
    public string SubmittedAtText => SubmissionDate.ToLocalTime().ToString("dd/MM/yyyy HH:mm:ss");
    public bool HasProtocol => !string.IsNullOrWhiteSpace(Protocol);
    public string Summary => Success
        ? (HasProtocol ? $"Protocol: {Protocol}" : "Accepted by Ergani")
        : (string.IsNullOrWhiteSpace(ErrorMessage) ? "Failed" : ErrorMessage!);
}

public partial class SchedulesViewModel : ViewModelBase, IAdminSectionViewModel
{
    private readonly IScheduleService   _scheduleService;
    private readonly IEmployeeService   _employeeService;
    private readonly IBranchService     _branchService;
    private readonly IScheduleSubmitter _scheduleSubmitter;
    private readonly IErganiDocumentService _documentService;
    private readonly IRoundingSettingsService _roundingSettings;
    private readonly ILeaveService _leaveService;
    private readonly IOvertimeService _overtimeService;
    private readonly IImportLogService _importLogService;
    private readonly IErganiPortalService _portalService;
    private readonly ILeaveSubmitter _leaveSubmitter;
    private UserSession? _session;

    // ── Employee / navigation ─────────────────────────────────────────────────
    public ObservableCollection<EmployeeDto> AvailableEmployees { get; } = new();
    [ObservableProperty] private EmployeeDto? _selectedEmployee;

    [ObservableProperty] private int _year  = DateTime.Today.Year;
    [ObservableProperty] private int _month = DateTime.Today.Month;
    [ObservableProperty] private int _weekOffset = 0; // 0 = current week
    public string MonthLabel => new DateOnly(Year, Month, 1).ToString("MMMM yyyy");
    public string WeekLabel
    {
        get
        {
            var (from, to) = CurrentWeekRange;
            return $"{from:dd/MM} – {to:dd/MM/yyyy}";
        }
    }
    private (DateOnly From, DateOnly To) CurrentWeekRange
    {
        get
        {
            var today = DateOnly.FromDateTime(DateTime.Today);
            var monday = today.AddDays(-(int)today.DayOfWeek == 0 ? 6 : (int)today.DayOfWeek - 1);
            monday = monday.AddDays(_weekOffset * 7);
            return (monday, monday.AddDays(6));
        }
    }

    [ObservableProperty] private ScheduleViewMode _viewMode = ScheduleViewMode.Month;
    public bool IsMonthView => ViewMode == ScheduleViewMode.Month;
    public bool IsWeekView  => ViewMode == ScheduleViewMode.Week;

    public ObservableCollection<CalendarCellViewModel> Cells { get; } = new();

    /// <summary>Three predefined shifts configured in Administration.</summary>
    public ObservableCollection<WorkShiftDefinition> AvailableShifts { get; } = new();
    [ObservableProperty] private WorkShiftDefinition? _selectedEditingShift;
    [ObservableProperty] private WorkShiftDefinition? _selectedBulkShift;

    [ObservableProperty] private bool _hasActiveCompany;
    [ObservableProperty] private string _noCompanyMessage = string.Empty;
    [ObservableProperty] private string _statusMessage = string.Empty;

    // ── Ergani Excel import confirmation ─────────────────────────────────────
    [ObservableProperty] private bool _isImportDialogOpen;
    [ObservableProperty] private string _importSummary = string.Empty;
    [ObservableProperty] private string _importWarnings = string.Empty;
    private List<ExcelImportExportService.ErganiScheduleImportRow> _pendingErganiRows = new();
    private string _pendingImportFilePath = string.Empty;

    // Notes about skipped rows/warnings of the last prepared import, appended to the result message.
    private string _importNotes = string.Empty;
    private int _pendingSkippedRows;

    // Days (employee, date) of the file that already have different data in the database.
    private HashSet<(int Employee, DateOnly Date)> _conflictKeys = new();
    [ObservableProperty] private bool _isConflictDialogOpen;
    [ObservableProperty] private string _conflictText = string.Empty;
    private string _pendingImportOrigin = "Manual";

    // Import history dialog
    [ObservableProperty] private bool _isImportHistoryOpen;
    public ObservableCollection<ImportLogDto> ImportHistory { get; } = new();

    // The portal service is a singleton; only the most recently created view model reacts to a download.
    private static SchedulesViewModel? _latest;
    private string _portalDownloadedFile = string.Empty;
    [ObservableProperty] private bool _isPortalImportPromptOpen;
    [ObservableProperty] private string _portalImportPromptText = string.Empty;

    // ── Multi-day selection ───────────────────────────────────────────────────
    private readonly HashSet<DateOnly> _selectedDates = new();
    public int SelectedDayCount => _selectedDates.Count;
    public bool HasSelection => _selectedDates.Count > 0;
    public string SelectionLabel => _selectedDates.Count == 0
        ? "No days selected"
        : $"{_selectedDates.Count} day(s) selected";

    /// <summary>With days selected the button submits only those; otherwise it submits the month.</summary>
    public string SubmitScheduleButtonText => HasSelection
        ? $"📤 Submit selected ({_selectedDates.Count})"
        : "📤 Submit Schedule";
    public string SubmitOvertimeButtonText => HasSelection
        ? $"⏱ Overtime of selected ({_selectedDates.Count})"
        : "⏱ Submit Overtime";
    public string SubmitHolidaysButtonText => HasSelection
        ? $"🏖 Holidays of selected ({_selectedDates.Count})"
        : "🏖 Submit Holidays";
    public string DeleteSelectedButtonText => $"🗑 Delete selected ({_selectedDates.Count})";

    // ── Bulk / day dialog ─────────────────────────────────────────────────────
    [ObservableProperty] private bool _isDayDialogOpen;
    [ObservableProperty] private DateOnly _editingDate;
    [ObservableProperty] private int _editingScheduleId;
    [ObservableProperty] private AppWorkType _editingWorkType = AppWorkType.Office;
    [ObservableProperty] private TimeSpan? _editingStartTime = new(9, 0, 0);
    [ObservableProperty] private TimeSpan? _editingEndTime   = new(17, 0, 0);
    [ObservableProperty] private string _editingComments = string.Empty;
    [ObservableProperty] private string _editingActualText = string.Empty;
    [ObservableProperty] private string _editingSubmissionText = string.Empty;
    [ObservableProperty] private BranchDto? _editingBranch;
    [ObservableProperty] private bool _editingSubmitted;

    // ── Delete confirmation ───────────────────────────────────────────────────
    [ObservableProperty] private bool _isConfirmDeleteOpen;
    [ObservableProperty] private string _confirmDeleteMessage = string.Empty;
    private readonly List<DateOnly> _pendingDeleteDates = new();

    // ── Submission log (Ergani responses + PDF) ───────────────────────────────
    /// <summary>The holidays form (opened with the 🏖 Holidays button).</summary>
    public HolidayDialogViewModel Holidays { get; }

    public ObservableCollection<ScheduleLogRow> LogRows { get; } = new();
    [ObservableProperty] private bool _isLogOpen;
    [ObservableProperty] private bool _hasLogRows;
    [ObservableProperty] private string _logTitle = string.Empty;
    [ObservableProperty] private string _logStatus = string.Empty;
    [ObservableProperty] private bool _isResponseOpen;
    [ObservableProperty] private string _responseTitle = string.Empty;
    [ObservableProperty] private string _responseText = string.Empty;

    // ── Week bulk ─────────────────────────────────────────────────────────────
    [ObservableProperty] private bool _isBulkDialogOpen;
    [ObservableProperty] private AppWorkType _bulkWorkType = AppWorkType.Office;
    [ObservableProperty] private TimeSpan? _bulkStartTime = new(9, 0, 0);
    [ObservableProperty] private TimeSpan? _bulkEndTime   = new(17, 0, 0);
    [ObservableProperty] private bool _bulkMonday    = true;
    [ObservableProperty] private bool _bulkTuesday   = true;
    [ObservableProperty] private bool _bulkWednesday = true;
    [ObservableProperty] private bool _bulkThursday  = true;
    [ObservableProperty] private bool _bulkFriday    = true;
    [ObservableProperty] private bool _bulkSaturday  = false;
    [ObservableProperty] private bool _bulkSunday    = false;

    // ── Clone to other employees ──────────────────────────────────────────────
    [ObservableProperty] private bool _isCloneDialogOpen;
    [ObservableProperty] private DateOnly? _cloneFrom;
    [ObservableProperty] private DateOnly? _cloneTo;
    public ObservableCollection<CloneTargetEmployee> CloneTargets { get; } = new();

    public ObservableCollection<BranchDto> AvailableBranches { get; } = new();
    public ObservableCollection<AppWorkType> AvailableWorkTypes { get; } =
        new(Enum.GetValues<AppWorkType>());
    public bool ShowTimeFields     => EditingWorkType is AppWorkType.Office or AppWorkType.Home;
    public bool BulkShowTimeFields => BulkWorkType    is AppWorkType.Office or AppWorkType.Home;
    public bool IsEditingExisting  => EditingScheduleId != 0;
    /// <summary>Only Office/Home days that are not yet at Ergani can be submitted from the day dialog.</summary>
    public bool CanSubmitEditingDay => ShowTimeFields && !EditingSubmitted;

    /// <summary>Shown when the day is longer than 8 hours: how it will be split for Ergani.</summary>
    public string EditingOvertimeNote
    {
        get
        {
            if (!ShowTimeFields || !EditingStartTime.HasValue || !EditingEndTime.HasValue) return string.Empty;
            var start = TimeOnly.FromTimeSpan(EditingStartTime.Value);
            var end   = TimeOnly.FromTimeSpan(EditingEndTime.Value);
            if (ScheduleHours.GetOvertimeRange(start, end) is not { } ot) return string.Empty;
            return $"⏱ Over {ScheduleHours.NormalDailyHours} hours — {start:HH:mm}–{ot.From:HH:mm} is submitted as the schedule, " +
                   $"{ot.From:HH:mm}–{ot.To:HH:mm} is submitted as overtime.";
        }
    }

    public SchedulesViewModel(IScheduleService scheduleService,
        IEmployeeService employeeService, IBranchService branchService,
        IScheduleSubmitter scheduleSubmitter, IErganiDocumentService documentService,
        ILeaveService leaveService, ILeaveSubmitter leaveSubmitter,
        IErganiPortalService portalService, IRoundingSettingsService roundingSettings,
        IOvertimeService overtimeService, IImportLogService importLogService)
    {
        _overtimeService = overtimeService;
        _importLogService = importLogService;
        _portalService  = portalService;
        _portalService.Progress += message =>
            Avalonia.Threading.Dispatcher.UIThread.Post(() => StatusMessage = message);
        _latest = this;
        _portalService.ExcelDownloaded += filePath =>
            Avalonia.Threading.Dispatcher.UIThread.Post(() => OnPortalExcelDownloaded(filePath));
        _leaveService   = leaveService;
        _leaveSubmitter = leaveSubmitter;
        Holidays = new HolidayDialogViewModel(leaveService, leaveSubmitter, documentService);
        Holidays.HolidaysChanged += (_, _) => _ = RefreshCalendarAsync();
        _scheduleService   = scheduleService;
        _employeeService   = employeeService;
        _branchService     = branchService;
        _scheduleSubmitter = scheduleSubmitter;
        _documentService   = documentService;
        _roundingSettings  = roundingSettings;
        LoadAvailableShifts();
    }

    private void LoadAvailableShifts()
    {
        AvailableShifts.Clear();
        var shifts = (_roundingSettings.Current.Shifts ?? RoundingRules.DefaultShifts()).Take(3).ToList();
        var defaults = RoundingRules.DefaultShifts();
        for (var i = 0; i < 3; i++)
        {
            var shift = i < shifts.Count ? shifts[i] : defaults[i];
            AvailableShifts.Add(shift.Clone());
        }
    }

    private WorkShiftDefinition? FindMatchingShift(TimeSpan? start, TimeSpan? end) =>
        start is { } s && end is { } e
            ? AvailableShifts.FirstOrDefault(x => x.StartTime == s && x.EndTime == e)
            : null;

    partial void OnSelectedEditingShiftChanged(WorkShiftDefinition? value)
    {
        if (value == null) return;
        EditingStartTime = value.StartTime;
        EditingEndTime = value.EndTime;
    }

    partial void OnSelectedBulkShiftChanged(WorkShiftDefinition? value)
    {
        if (value == null) return;
        BulkStartTime = value.StartTime;
        BulkEndTime = value.EndTime;
    }

    public void Initialize(UserSession session)
    {
        _session = session;
        HasActiveCompany = session.CompanyId.HasValue;
        NoCompanyMessage = session.CompanyId.HasValue ? string.Empty
            : "Select a company first.";
        if (HasActiveCompany) _ = LoadFiltersAsync();
    }

    private async Task LoadFiltersAsync()
    {
        if (_session?.CompanyId is not int companyId) return;

        var employees = await _employeeService
            .GetByCompanyAsync(companyId, activeOnly: true).ConfigureAwait(false);
        var branches  = await _branchService
            .GetByCompanyAsync(companyId).ConfigureAwait(false);

        await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
        {
            AvailableEmployees.Clear();
            foreach (var e in employees) AvailableEmployees.Add(e);
            AvailableBranches.Clear();
            foreach (var b in branches) AvailableBranches.Add(b);
            SelectedEmployee = AvailableEmployees.FirstOrDefault();
            EditingBranch    = AvailableBranches.FirstOrDefault();
        });

        await RefreshCalendarAsync();
    }

    // ── Navigation ────────────────────────────────────────────────────────────

    [RelayCommand]
    private async Task PreviousMonthAsync()
    {
        if (Month == 1) { Month = 12; Year--; } else Month--;
        OnPropertyChanged(nameof(MonthLabel));
        await RefreshCalendarAsync();
    }

    [RelayCommand]
    private async Task NextMonthAsync()
    {
        if (Month == 12) { Month = 1; Year++; } else Month++;
        OnPropertyChanged(nameof(MonthLabel));
        await RefreshCalendarAsync();
    }

    [RelayCommand]
    private async Task PreviousWeekAsync()
    {
        _weekOffset--;
        OnPropertyChanged(nameof(WeekLabel));
        await RefreshCalendarAsync();
    }

    [RelayCommand]
    private async Task NextWeekAsync()
    {
        _weekOffset++;
        OnPropertyChanged(nameof(WeekLabel));
        await RefreshCalendarAsync();
    }

    [RelayCommand]
    private async Task SetViewModeAsync(string mode)
    {
        ViewMode = mode == "Week" ? ScheduleViewMode.Week : ScheduleViewMode.Month;
        OnPropertyChanged(nameof(IsMonthView));
        OnPropertyChanged(nameof(IsWeekView));
        await RefreshCalendarAsync();
    }

    partial void OnSelectedEmployeeChanged(EmployeeDto? value) =>
        _ = RefreshCalendarAsync();

    // ── Calendar build ────────────────────────────────────────────────────────

    private async Task RefreshCalendarAsync()
    {
        if (SelectedEmployee == null) return;

        _selectedDates.Clear();
        NotifySelectionChanged();

        if (ViewMode == ScheduleViewMode.Month)
            await BuildMonthCalendarAsync();
        else
            await BuildWeekCalendarAsync();
    }

    /// <summary>Overtime records (not cancelled) of the selected employee, grouped by day.</summary>
    private async Task<Dictionary<DateOnly, List<OvertimeDto>>> LoadOvertimeByDateAsync(DateOnly from, DateOnly to)
    {
        if (SelectedEmployee == null || _session?.CompanyId is not int companyId)
            return new Dictionary<DateOnly, List<OvertimeDto>>();

        var list = await _overtimeService.GetByCompanyAsync(companyId, from, to).ConfigureAwait(false);
        return list
            .Where(o => o.EmployeeId == SelectedEmployee.Id && !o.IsCancelled)
            .GroupBy(o => o.OvertimeDate)
            .ToDictionary(g => g.Key, g => g.OrderBy(o => o.StartTime).ToList());
    }

    /// <summary>The overtime line of a calendar cell. Saved overtime records win; for a manual day
    /// longer than 8 hours the part past the normal day is shown (that is what gets submitted).</summary>
    private static string BuildOvertimeText(ScheduleDayDto? sched, List<OvertimeDto>? records)
    {
        if (records is { Count: > 0 })
            return "⏱ " + string.Join(", ", records.Select(o => $"{o.StartTime:HH:mm}–{o.EndTime:HH:mm}"));

        if (sched is { WorkType: AppWorkType.Office or AppWorkType.Home } &&
            ScheduleHours.GetOvertimeRange(sched.StartTime, sched.EndTime) is { } ot)
            return $"⏱ {ot.From:HH:mm}–{ot.To:HH:mm}";

        return string.Empty;
    }

    /// <summary>The work line of a calendar cell: for a day longer than 8 hours only the normal part,
    /// the rest is the overtime line.</summary>
    private static string WorkRangeText(ScheduleDayDto? sched)
    {
        if (sched is not { StartTime: not null, EndTime: not null }) return string.Empty;
        var end = sched.EndTime.Value;
        if (sched.WorkType is AppWorkType.Office or AppWorkType.Home &&
            ScheduleHours.GetOvertimeRange(sched.StartTime, sched.EndTime) is { } ot)
            end = ot.From;
        return $"{sched.StartTime:HH:mm}–{end:HH:mm}";
    }

    private async Task BuildMonthCalendarAsync()
    {
        var first = new DateOnly(Year, Month, 1);
        var days  = DateTime.DaysInMonth(Year, Month);
        var last  = new DateOnly(Year, Month, days);

        var schedules = await _scheduleService
            .GetMonthAsync(SelectedEmployee!.Id, Year, Month).ConfigureAwait(false);
        var leaves = _session?.CompanyId is int companyId
            ? await Holidays.GetCalendarLeavesAsync(companyId, SelectedEmployee.Id, first, last).ConfigureAwait(false)
            : new List<LeaveDto>();
        var byDate = schedules.ToDictionary(s => s.ScheduleDate);
        var overtimeByDate = await LoadOvertimeByDateAsync(first, last).ConfigureAwait(false);
        var leavesByDate = leaves.GroupBy(l => l.LeaveDate).ToDictionary(g => g.Key, g => g.ToList());
        var official = GreekHolidays.ForYear(Year).ToDictionary(h => h.Date, h => h.Name);
        var today  = DateOnly.FromDateTime(DateTime.Today);
        int startDow = ((int)first.DayOfWeek + 6) % 7; // Mon=0

        var cells = new List<CalendarCellViewModel>();
        for (int i = 0; i < startDow; i++)
            cells.Add(new CalendarCellViewModel { IsInMonth = false });

        for (int d = 1; d <= days; d++)
        {
            var date = new DateOnly(Year, Month, d);
            byDate.TryGetValue(date, out var sched);
            leavesByDate.TryGetValue(date, out var dayLeaves);
            overtimeByDate.TryGetValue(date, out var dayOvertime);
            var overtimeText = BuildOvertimeText(sched, dayOvertime);
            official.TryGetValue(date, out var officialName);
            var userHolidayText = dayLeaves is null ? string.Empty : string.Join(" · ", dayLeaves.Select(l => LeaveTypes.Find(l.LeaveTypeCode)?.Description ?? l.LeaveTypeCode));
            var tooltip = string.Join("\n", new[]
            {
                string.IsNullOrWhiteSpace(officialName) ? null : $"🇬🇷 {officialName}",
                string.IsNullOrWhiteSpace(userHolidayText) ? null : $"🏖 {userHolidayText}",
                string.IsNullOrWhiteSpace(overtimeText) ? null : $"{overtimeText} overtime",
                sched?.Source == ScheduleSource.ErganiImported ? "🇬🇷 Ergani imported" : null
            }.Where(x => !string.IsNullOrWhiteSpace(x)));
            cells.Add(new CalendarCellViewModel
            {
                IsInMonth          = true,
                Date               = date,
                HasSchedule        = sched != null,
                IsSubmitted        = sched?.SubmittedToErgani ?? false,
                IsErganiImported  = sched?.Source == ScheduleSource.ErganiImported,
                WorkType           = sched != null ? (AppWorkType?)sched.WorkType : null,
                TimeRangeText      = WorkRangeText(sched),
                OvertimeText  = overtimeText,
                OfficialHolidayText = officialName ?? string.Empty,
                UserHolidayText     = userHolidayText,
                HolidayTooltip      = tooltip,
                IsToday             = date == today
            });
        }

        await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
        {
            Cells.Clear();
            foreach (var c in cells) Cells.Add(c);
        });
    }

    private async Task BuildWeekCalendarAsync()
    {
        var (from, to) = CurrentWeekRange;
        var schedules  = await _scheduleService
            .GetMonthAsync(SelectedEmployee!.Id, from.Year, from.Month).ConfigureAwait(false);

        // If week spans two months, also fetch next month
        if (to.Month != from.Month)
        {
            var extra = await _scheduleService
                .GetMonthAsync(SelectedEmployee.Id, to.Year, to.Month).ConfigureAwait(false);
            schedules = schedules.Concat(extra).ToList();
        }

        var byDate = schedules.ToDictionary(s => s.ScheduleDate);
        var leaves = _session?.CompanyId is int companyId
            ? await Holidays.GetCalendarLeavesAsync(companyId, SelectedEmployee.Id, from, to).ConfigureAwait(false)
            : new List<LeaveDto>();
        var overtimeByDate = await LoadOvertimeByDateAsync(from, to).ConfigureAwait(false);
        var leavesByDate = leaves.GroupBy(l => l.LeaveDate).ToDictionary(g => g.Key, g => g.ToList());
        var officialByDate = Enumerable.Range(from.Year, to.Year - from.Year + 1)
            .SelectMany(y => GreekHolidays.ForYear(y))
            .Where(h => h.Date >= from && h.Date <= to)
            .ToDictionary(h => h.Date, h => h.Name);
        var today  = DateOnly.FromDateTime(DateTime.Today);
        var cells  = new List<CalendarCellViewModel>();

        for (var d = from; d <= to; d = d.AddDays(1))
        {
            byDate.TryGetValue(d, out var sched);
            leavesByDate.TryGetValue(d, out var dayLeaves);
            overtimeByDate.TryGetValue(d, out var dayOvertime);
            var overtimeText = BuildOvertimeText(sched, dayOvertime);
            officialByDate.TryGetValue(d, out var officialName);
            var userHolidayText = dayLeaves is null ? string.Empty : string.Join(" · ", dayLeaves.Select(l => LeaveTypes.Find(l.LeaveTypeCode)?.Description ?? l.LeaveTypeCode));
            var tooltip = string.Join("\n", new[]
            {
                string.IsNullOrWhiteSpace(officialName) ? null : $"🇬🇷 {officialName}",
                string.IsNullOrWhiteSpace(userHolidayText) ? null : $"🏖 {userHolidayText}",
                string.IsNullOrWhiteSpace(overtimeText) ? null : $"{overtimeText} overtime",
                sched?.Source == ScheduleSource.ErganiImported ? "🇬🇷 Ergani imported" : null
            }.Where(x => !string.IsNullOrWhiteSpace(x)));
            cells.Add(new CalendarCellViewModel
            {
                IsInMonth           = true,
                Date                = d,
                HasSchedule         = sched != null,
                IsSubmitted         = sched?.SubmittedToErgani ?? false,
                IsErganiImported   = sched?.Source == ScheduleSource.ErganiImported,
                WorkType            = sched != null ? (AppWorkType?)sched.WorkType : null,
                TimeRangeText       = WorkRangeText(sched),
                OvertimeText   = overtimeText,
                OfficialHolidayText = officialName ?? string.Empty,
                UserHolidayText     = userHolidayText,
                HolidayTooltip      = tooltip,
                IsToday              = d == today
            });
        }

        await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
        {
            Cells.Clear();
            foreach (var c in cells) Cells.Add(c);
        });
    }

    // ── Cell click — multi-select with Ctrl support ───────────────────────────

    /// <summary>Called from code-behind. ctrl=true → toggle selection.
    /// ctrl=false → open day dialog if single day already selected or toggle.</summary>
    public void OnCellClick(CalendarCellViewModel cell, bool ctrl)
    {
        if (!cell.IsInMonth || cell.Date == null) return;
        var date = cell.Date.Value;

        if (ctrl)
        {
            // Toggle selection
            if (_selectedDates.Contains(date)) { _selectedDates.Remove(date); cell.IsSelected = false; }
            else { _selectedDates.Add(date); cell.IsSelected = true; }
            NotifySelectionChanged();
        }
        else if (_selectedDates.Count == 0)
        {
            // Single click with no existing selection → open day dialog
            _ = OpenDayDialogAsync(cell);
        }
        else
        {
            // Toggle this date and keep multi-select mode
            if (_selectedDates.Contains(date)) { _selectedDates.Remove(date); cell.IsSelected = false; }
            else { _selectedDates.Add(date); cell.IsSelected = true; }
            NotifySelectionChanged();
        }
    }

    private void NotifySelectionChanged()
    {
        OnPropertyChanged(nameof(SelectedDayCount));
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(SelectionLabel));
        OnPropertyChanged(nameof(SubmitScheduleButtonText));
        OnPropertyChanged(nameof(SubmitOvertimeButtonText));
        OnPropertyChanged(nameof(SubmitHolidaysButtonText));
        OnPropertyChanged(nameof(DeleteSelectedButtonText));
    }

    [RelayCommand]
    private void ClearSelection()
    {
        _selectedDates.Clear();
        foreach (var c in Cells) c.IsSelected = false;
        NotifySelectionChanged();
    }

    // ── Single day dialog ─────────────────────────────────────────────────────

    private async Task OpenDayDialogAsync(CalendarCellViewModel cell)
    {
        if (cell.Date is not DateOnly date || SelectedEmployee == null) return;
        LoadAvailableShifts();

        ScheduleDayDto? day;
        try { day = await _scheduleService.GetByDateAsync(SelectedEmployee.Id, date); }
        catch (Exception ex) { StatusMessage = $"❌ {ex.Message}"; return; }

        // GetByDateAsync also returns a stub (Id = 0) when the day only has work cards.
        var existing = day is { Id: > 0 } ? day : null;

        EditingDate       = date;
        EditingScheduleId = existing?.Id ?? 0;
        EditingWorkType   = existing?.WorkType ?? AppWorkType.Office;
        SelectedEditingShift = FindMatchingShift(existing?.StartTime?.ToTimeSpan(), existing?.EndTime?.ToTimeSpan())
            ?? AvailableShifts.FirstOrDefault();
        EditingStartTime  = existing?.StartTime?.ToTimeSpan() ?? SelectedEditingShift?.StartTime ?? new TimeSpan(9, 0, 0);
        EditingEndTime    = existing?.EndTime?.ToTimeSpan()   ?? SelectedEditingShift?.EndTime ?? new TimeSpan(17, 0, 0);
        EditingComments   = existing?.Comments ?? string.Empty;
        EditingSubmitted  = existing?.SubmittedToErgani ?? false;
        EditingBranch     = (existing != null
                                ? AvailableBranches.FirstOrDefault(b => b.Id == existing.BranchId)
                                : null)
                            ?? AvailableBranches.FirstOrDefault();

        EditingActualText = day?.ActualArrival != null || day?.ActualDeparture != null
            ? string.Format(Loc[L.ActualClockTimes],
                day?.ActualArrival?.ToString("HH:mm") ?? "—",
                day?.ActualDeparture?.ToString("HH:mm") ?? "—")
            : string.Empty;

        EditingSubmissionText = existing == null
            ? string.Empty
            : existing.SubmittedToErgani
                ? string.Format(Loc[L.SubmittedProtocol], existing.Protocol)
                : Loc[L.NotSubmitted];

        IsDayDialogOpen = true;
    }

    [RelayCommand]
    private void CloseDayDialog() => IsDayDialogOpen = false;

    partial void OnEditingWorkTypeChanged(AppWorkType value)
    {
        OnPropertyChanged(nameof(ShowTimeFields));
        OnPropertyChanged(nameof(CanSubmitEditingDay));
        OnPropertyChanged(nameof(EditingOvertimeNote));
    }

    partial void OnEditingStartTimeChanged(TimeSpan? value) =>
        OnPropertyChanged(nameof(EditingOvertimeNote));

    partial void OnEditingEndTimeChanged(TimeSpan? value) =>
        OnPropertyChanged(nameof(EditingOvertimeNote));

    partial void OnEditingScheduleIdChanged(int value) =>
        OnPropertyChanged(nameof(IsEditingExisting));

    partial void OnEditingSubmittedChanged(bool value) =>
        OnPropertyChanged(nameof(CanSubmitEditingDay));

    private ScheduleDayDto BuildEditingDto() => new()
    {
        Id           = EditingScheduleId,
        EmployeeId   = SelectedEmployee!.Id,
        BranchId     = EditingBranch!.Id,
        ScheduleDate = EditingDate,
        WorkType     = EditingWorkType,
        StartTime    = EditingStartTime.HasValue ? TimeOnly.FromTimeSpan(EditingStartTime.Value) : null,
        EndTime      = EditingEndTime.HasValue   ? TimeOnly.FromTimeSpan(EditingEndTime.Value)   : null,
        Comments     = EditingComments
    };

    [RelayCommand]
    private async Task SaveDayAsync()
    {
        if (SelectedEmployee == null || EditingBranch == null) return;
        try
        {
            await _scheduleService.UpsertDayAsync(BuildEditingDto());
            IsDayDialogOpen = false;
            await RefreshCalendarAsync();
        }
        catch (Exception ex) { StatusMessage = $"❌ {ex.Message}"; }
    }

    /// <summary>Saves the day as shown in the dialog and submits just that one day to Ergani.</summary>
    [RelayCommand]
    private async Task SaveAndSubmitDayAsync()
    {
        if (SelectedEmployee == null || EditingBranch == null || _session?.CompanyId is not int companyId) return;
        try
        {
            var date = EditingDate;
            await _scheduleService.UpsertDayAsync(BuildEditingDto());
            IsDayDialogOpen = false;

            var saved = await _scheduleService.GetByDateAsync(SelectedEmployee.Id, date);
            if (saved is not { Id: > 0 })
            { StatusMessage = "❌ Could not reload the saved day."; return; }

            await SubmitDaysAsync(companyId, new List<ScheduleDayDto> { saved }, 1);
        }
        catch (Exception ex) { StatusMessage = $"❌ {ex.Message}"; }
    }

    // ── Delete (single day from the dialog, or all selected days) ─────────────

    [RelayCommand]
    private void DeleteDay()
    {
        if (EditingScheduleId == 0) return;
        RequestDelete(new[] { EditingDate });
    }

    [RelayCommand]
    private void DeleteSelected() => RequestDelete(_selectedDates.ToList());

    private void RequestDelete(IEnumerable<DateOnly> dates)
    {
        var byDate = Cells.Where(c => c.Date.HasValue).ToDictionary(c => c.Date!.Value);
        var withSchedule = dates
            .Distinct()
            .Where(d => byDate.TryGetValue(d, out var c) && c.HasSchedule)
            .OrderBy(d => d)
            .ToList();

        if (withSchedule.Count == 0)
        {
            StatusMessage = "There is no schedule to delete on the selected day(s).";
            return;
        }

        var submitted = withSchedule.Count(d => byDate[d].IsSubmitted);

        var message = withSchedule.Count == 1
            ? $"Delete the schedule for {withSchedule[0]:dd/MM/yyyy}?"
            : $"Delete the schedule of {withSchedule.Count} days?";

        if (submitted > 0)
            message += submitted == 1 && withSchedule.Count == 1
                ? "\n\nThis day was already sent to Ergani. Deleting only removes it from this app — it does not change what Ergani received."
                : $"\n\n{submitted} of them were already sent to Ergani. Deleting only removes them from this app — it does not change what Ergani received.";

        _pendingDeleteDates.Clear();
        _pendingDeleteDates.AddRange(withSchedule);
        ConfirmDeleteMessage = message;
        IsConfirmDeleteOpen = true;
    }

    [RelayCommand]
    private void CancelDelete() => IsConfirmDeleteOpen = false;

    [RelayCommand]
    private async Task ConfirmDeleteAsync()
    {
        if (SelectedEmployee == null) { IsConfirmDeleteOpen = false; return; }

        var dates = _pendingDeleteDates.ToList();
        IsConfirmDeleteOpen = false;

        try
        {
            var removed = await _scheduleService.DeleteDaysAsync(SelectedEmployee.Id, dates);
            IsDayDialogOpen = false;
            StatusMessage = $"✅ Deleted the schedule of {removed} day(s).";
            await RefreshCalendarAsync();
        }
        catch (Exception ex) { StatusMessage = $"❌ {ex.Message}"; }
    }

    // ── Bulk apply (multi-selected days or week) ──────────────────────────────

    [RelayCommand]
    private void OpenBulkDialog()
    {
        if (SelectedEmployee == null) return;
        LoadAvailableShifts();
        // Pre-fill from / to based on current view and selection
        IsBulkDialogOpen = true;
        BulkWorkType  = AppWorkType.Office;
        SelectedBulkShift = AvailableShifts.FirstOrDefault();
        BulkStartTime = SelectedBulkShift?.StartTime ?? new TimeSpan(9, 0, 0);
        BulkEndTime   = SelectedBulkShift?.EndTime ?? new TimeSpan(17, 0, 0);
        // Day-of-week checkboxes stay as user left them
    }

    [RelayCommand]
    private void CloseBulkDialog() => IsBulkDialogOpen = false;

    partial void OnBulkWorkTypeChanged(AppWorkType value) =>
        OnPropertyChanged(nameof(BulkShowTimeFields));

    [RelayCommand]
    private async Task ApplyBulkAsync()
    {
        if (SelectedEmployee == null || EditingBranch == null) return;

        DateOnly from, to;
        IReadOnlySet<DayOfWeek>? dowFilter = null;

        if (_selectedDates.Count > 0)
        {
            // Apply to exactly the selected dates — ignore day-of-week checkboxes
            from      = _selectedDates.Min();
            to        = _selectedDates.Max();
            dowFilter = _selectedDates.Select(d => d.DayOfWeek).ToHashSet();
        }
        else if (ViewMode == ScheduleViewMode.Week)
        {
            var (wFrom, wTo) = CurrentWeekRange;
            from = wFrom; to = wTo;
            dowFilter = BuildDowFilter();
        }
        else
        {
            from = new DateOnly(Year, Month, 1);
            to   = new DateOnly(Year, Month, DateTime.DaysInMonth(Year, Month));
            dowFilter = BuildDowFilter();
        }

        try
        {
            var count = await _scheduleService.BulkSetAsync(
                SelectedEmployee.Id, EditingBranch.Id, from, to,
                BulkWorkType,
                BulkShowTimeFields && BulkStartTime.HasValue ? TimeOnly.FromTimeSpan(BulkStartTime.Value) : null,
                BulkShowTimeFields && BulkEndTime.HasValue   ? TimeOnly.FromTimeSpan(BulkEndTime.Value)   : null,
                dowFilter);

            IsBulkDialogOpen = false;
            StatusMessage = $"✅ Applied to {count} day(s).";
            ClearSelection();
            await RefreshCalendarAsync();
        }
        catch (Exception ex) { StatusMessage = $"❌ {ex.Message}"; }
    }

    private HashSet<DayOfWeek> BuildDowFilter()
    {
        var s = new HashSet<DayOfWeek>();
        if (BulkMonday)    s.Add(DayOfWeek.Monday);
        if (BulkTuesday)   s.Add(DayOfWeek.Tuesday);
        if (BulkWednesday) s.Add(DayOfWeek.Wednesday);
        if (BulkThursday)  s.Add(DayOfWeek.Thursday);
        if (BulkFriday)    s.Add(DayOfWeek.Friday);
        if (BulkSaturday)  s.Add(DayOfWeek.Saturday);
        if (BulkSunday)    s.Add(DayOfWeek.Sunday);
        return s;
    }

    // ── Clone to other employees ──────────────────────────────────────────────

    [RelayCommand]
    private async Task OpenCloneDialogAsync()
    {
        if (SelectedEmployee == null) return;

        if (ViewMode == ScheduleViewMode.Week)
        {
            var (f, t) = CurrentWeekRange;
            CloneFrom = f; CloneTo = t;
        }
        else
        {
            CloneFrom = new DateOnly(Year, Month, 1);
            CloneTo   = new DateOnly(Year, Month, DateTime.DaysInMonth(Year, Month));
        }

        var targets = AvailableEmployees
            .Where(e => e.Id != SelectedEmployee.Id)
            .Select(e => new CloneTargetEmployee { Employee = e })
            .ToList();

        await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
        {
            CloneTargets.Clear();
            foreach (var t in targets) CloneTargets.Add(t);
        });

        IsCloneDialogOpen = true;
    }

    [RelayCommand]
    private void CloseCloneDialog() => IsCloneDialogOpen = false;

    [RelayCommand]
    private async Task ApplyCloneAsync()
    {
        if (SelectedEmployee == null || CloneFrom == null || CloneTo == null) return;

        var targetIds = CloneTargets.Where(t => t.IsSelected).Select(t => t.Employee.Id).ToList();
        if (targetIds.Count == 0)
        {
            StatusMessage = "Select at least one employee to copy to.";
            return;
        }

        try
        {
            var count = await _scheduleService.CopyToEmployeesAsync(
                SelectedEmployee.Id, CloneFrom.Value, CloneTo.Value, targetIds);

            IsCloneDialogOpen = false;
            StatusMessage = $"✅ Copied {count} schedule entry(ies) to {targetIds.Count} employee(s).";
        }
        catch (Exception ex) { StatusMessage = $"❌ {ex.Message}"; }
    }

    // ── Export ────────────────────────────────────────────────────────────────

    [RelayCommand]
    private async Task ExportMonthAsync()
    {
        if (SelectedEmployee == null) { StatusMessage = "Select an employee first."; return; }
        try
        {
            var schedules = await _scheduleService
                .GetMonthAsync(SelectedEmployee.Id, Year, Month).ConfigureAwait(false);
            var folder   = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            var filePath = ExcelImportExportService.ExportMonthSchedule(
                SelectedEmployee.FullName, Year, Month, schedules, folder);
            StatusMessage = $"✅ Exported: {Path.GetFileName(filePath)}";
        }
        catch (Exception ex) { StatusMessage = $"❌ {ex.Message}"; }
    }

    // ── Ergani Excel import ───────────────────────────────────────────────────

    public event EventHandler? ImportRequested;

    [RelayCommand]
    private void RequestImport() => ImportRequested?.Invoke(this, EventArgs.Empty);

    /// <summary>The portal finished downloading the schedule Excel: ask whether to import it right away.</summary>
    private void OnPortalExcelDownloaded(string filePath)
    {
        if (!ReferenceEquals(_latest, this)) return;

        _portalDownloadedFile = filePath;
        PortalImportPromptText =
            $"The Ergani schedule file was downloaded:\n{Path.GetFileName(filePath)}\n\n" +
            "Do you want to import its data into the schedules now?";
        IsPortalImportPromptOpen = true;
    }

    /// <summary>"Yes": import the downloaded file directly, then show the result message.</summary>
    [RelayCommand]
    private async Task ImportPortalFileAsync()
    {
        IsPortalImportPromptOpen = false;
        var file = _portalDownloadedFile;
        _portalDownloadedFile = string.Empty;

        if (string.IsNullOrWhiteSpace(file) || !File.Exists(file))
        {
            StatusMessage = "❌ The downloaded file could not be found.";
            return;
        }

        await PrepareErganiImportFromFileAsync(file, autoConfirm: true);
    }

    /// <summary>"No": just close the question.</summary>
    [RelayCommand]
    private void DismissPortalImport()
    {
        IsPortalImportPromptOpen = false;
        _portalDownloadedFile = string.Empty;
    }

    public async Task PrepareErganiImportFromFileAsync(string filePath, bool autoConfirm = false)
    {
        try
        {
            StatusMessage = "Reading Ergani Excel export…";

            var result = await Task.Run(() =>
                ExcelImportExportService.ParseErganiScheduleExport(filePath));

            if (result.Rows.Count == 0)
            {
                StatusMessage = "❌ No valid schedule rows were found in the Excel file.";
                return;
            }

            var employeesByAfm = AvailableEmployees
                .Where(e => !string.IsNullOrWhiteSpace(e.TaxId))
                .GroupBy(e => NormalizeTaxId(e.TaxId))
                .ToDictionary(g => g.Key, g => g.First());

            var unmatched = result.Rows
                .Where(r => !employeesByAfm.ContainsKey(NormalizeTaxId(r.TaxId)))
                .ToList();

            _pendingErganiRows = result.Rows
                .Where(r => employeesByAfm.ContainsKey(NormalizeTaxId(r.TaxId)))
                .ToList();
            _pendingImportFilePath = filePath;
            _pendingSkippedRows = unmatched.Count;
            _pendingImportOrigin = autoConfirm ? "Portal" : "Manual";

            var workDays = _pendingErganiRows.Count(r => r.StartTime.HasValue && r.EndTime.HasValue);
            var leaveDays = _pendingErganiRows.Count(r => r.IsLeaveOnly);
            var overtimeDays = _pendingErganiRows.Count(r => r.HasOvertime);
            var overtimeRecords = _pendingErganiRows.Sum(r => r.OvertimeRanges.Count);
            var employeeCount = _pendingErganiRows
                .Select(r => NormalizeTaxId(r.TaxId))
                .Distinct()
                .Count();

            ImportSummary =
                $"File: {Path.GetFileName(filePath)}\n" +
                $"Excel rows: {result.TotalRows}\n" +
                $"Employees matched: {employeeCount}\n" +
                $"Schedule rows to import: {_pendingErganiRows.Count}\n" +
                $"Normal working-time rows: {workDays}\n" +
                $"Days with overtime: {overtimeDays} ({overtimeRecords} overtime record(s), imported separately from the work time)\n" +
                $"Holiday days (imported as holidays, not as absent): {leaveDays}\n" +
                $"Employees/rows not found in the database: {unmatched.Count}\n\n" +
                "Imported schedule days, overtime and holidays will be marked ERGANI IMPORTED and already SUBMITTED, so they are not sent to Ergani again.";

            var warnings = new List<string>();
            warnings.AddRange(result.Warnings.Take(10));
            if (unmatched.Count > 0)
            {
                foreach (var row in unmatched.Take(10))
                    warnings.Add($"Row {row.RowNumber}: employee AFM {row.TaxId} was not found.");
            }

            ImportWarnings = warnings.Count == 0
                ? "No warnings."
                : string.Join("\n", warnings);

            if (_pendingErganiRows.Count == 0)
            {
                StatusMessage = "❌ No rows could be matched to employees in the database.";
                return;
            }

            _importNotes =
                (unmatched.Count > 0 ? $" ⚠ {unmatched.Count} row(s) skipped: employee AFM not found." : string.Empty) +
                (result.Warnings.Count > 0 ? $" ⚠ {result.Warnings.Count} warning(s) while reading the file." : string.Empty);

            if (autoConfirm)
            {
                // Imported straight from the portal download: no second confirmation, the result message follows.
                await ConfirmImportAsync();
                return;
            }

            IsImportDialogOpen = true;
            StatusMessage = "Review the import summary and confirm before writing to the database.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"❌ {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task OpenImportHistoryAsync()
    {
        if (_session?.CompanyId is not int companyId) { StatusMessage = "Select a company first."; return; }
        try
        {
            var list = await _importLogService.GetRecentAsync(companyId);
            ImportHistory.Clear();
            foreach (var item in list) ImportHistory.Add(item);
            IsImportHistoryOpen = true;
        }
        catch (Exception ex) { StatusMessage = $"❌ {ex.Message}"; }
    }

    [RelayCommand]
    private void CloseImportHistory() => IsImportHistoryOpen = false;

    [RelayCommand]
    private void CancelImport()
    {
        _pendingErganiRows.Clear();
        _conflictKeys.Clear();
        _pendingImportFilePath = string.Empty;
        IsImportDialogOpen = false;
        StatusMessage = "Ergani schedule import cancelled.";
    }

    /// <summary>Import confirmed: first look for days that already have different data in the database.</summary>
    [RelayCommand]
    private async Task ConfirmImportAsync()
    {
        if (_pendingErganiRows.Count == 0)
        {
            IsImportDialogOpen = false;
            return;
        }

        IsImportDialogOpen = false;

        try
        {
            StatusMessage = "Checking for existing data on the imported dates…";
            var (keys, samples) = await FindConflictsAsync();
            _conflictKeys = keys;

            if (keys.Count == 0)
            {
                await RunImportAsync(replaceExisting: false);
                return;
            }

            var shown = samples.Take(8).Select(x => "• " + x);
            ConflictText =
                $"{keys.Count} day(s) of the file already have different data in the database:\n" +
                string.Join("\n", shown) +
                (samples.Count > 8 ? $"\n… and {samples.Count - 8} more" : string.Empty) +
                "\n\nDo you want to REPLACE the old data with the data from the file?\n" +
                "Yes = replace the old data of those days.\n" +
                "No = keep the old data and skip those lines (the other lines are still imported).";
            StatusMessage = "Existing data found — choose whether to replace it.";
            IsConflictDialogOpen = true;
        }
        catch (Exception ex)
        {
            StatusMessage = $"❌ {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task ReplaceExistingAsync()
    {
        IsConflictDialogOpen = false;
        await RunImportAsync(replaceExisting: true);
    }

    [RelayCommand]
    private async Task KeepExistingAsync()
    {
        IsConflictDialogOpen = false;
        await RunImportAsync(replaceExisting: false);
    }

    /// <summary>Finds the days of the file whose database data differs from what the file says.</summary>
    private async Task<(HashSet<(int Employee, DateOnly Date)> Keys, List<string> Samples)> FindConflictsAsync()
    {
        var keys = new HashSet<(int Employee, DateOnly Date)>();
        var samples = new List<string>();
        if (_session?.CompanyId is not int companyId || _pendingErganiRows.Count == 0)
            return (keys, samples);

        var employeesByAfm = AvailableEmployees
            .Where(e => !string.IsNullOrWhiteSpace(e.TaxId))
            .GroupBy(e => NormalizeTaxId(e.TaxId))
            .ToDictionary(g => g.Key, g => g.First());

        var from = _pendingErganiRows.Min(r => r.Date);
        var to   = _pendingErganiRows.Max(r => r.Date);

        var overtime = (await _overtimeService.GetByCompanyAsync(companyId, from, to))
            .Where(o => !o.IsCancelled)
            .ToLookup(o => (o.EmployeeId, o.OvertimeDate));
        var leavesByEmployee = new Dictionary<int, ILookup<DateOnly, LeaveDto>>();

        foreach (var row in _pendingErganiRows)
        {
            if (!employeesByAfm.TryGetValue(NormalizeTaxId(row.TaxId), out var employee))
                continue;

            var existing = await _scheduleService.GetByDateAsync(employee.Id, row.Date);

            if (!leavesByEmployee.TryGetValue(employee.Id, out var leaveLookup))
            {
                leaveLookup = (await _leaveService.GetByEmployeeDateRangeAsync(companyId, employee.Id, from, to))
                    .ToLookup(l => l.LeaveDate);
                leavesByEmployee[employee.Id] = leaveLookup;
            }

            var reason = DescribeConflict(row, existing,
                overtime[(employee.Id, row.Date)].ToList(), leaveLookup[row.Date].ToList());

            if (reason != null && keys.Add((employee.Id, row.Date)))
                samples.Add($"{employee.FullName} — {row.Date:dd/MM/yyyy} ({reason})");
        }

        return (keys, samples);
    }

    /// <summary>Null when the database has nothing for that day, or exactly what the file says.</summary>
    private static string? DescribeConflict(ExcelImportExportService.ErganiScheduleImportRow row,
        ScheduleDayDto? existing, List<OvertimeDto> overtime, List<LeaveDto> leaves)
    {
        if (row.IsLeaveOnly)
        {
            if (existing is { WorkType: AppWorkType.Office or AppWorkType.Home })
                return "a work schedule exists";
            if (existing != null && existing.Source != ScheduleSource.ErganiImported)
                return "a schedule exists";
            if (overtime.Count > 0)
                return "overtime exists";
            if (leaves.Any(l => !string.Equals(l.LeaveTypeCode, row.LeaveTypeCode, StringComparison.OrdinalIgnoreCase)))
                return "a different holiday exists";
            return null;
        }

        if (existing != null &&
            !(existing.WorkType == row.WorkType && existing.StartTime == row.StartTime && existing.EndTime == row.EndTime))
            return "a different schedule exists";

        var fileOvertime = row.OvertimeRanges.Select(r => (r.Start, r.End)).ToHashSet();
        var dbOvertime   = overtime.Select(o => (o.StartTime, o.EndTime)).ToHashSet();
        if (dbOvertime.Count > 0 && !fileOvertime.SetEquals(dbOvertime))
            return "different overtime exists";

        if (leaves.Count > 0)
            return "a holiday exists";

        return null;
    }

    private async Task RunImportAsync(bool replaceExisting)
    {
        if (_pendingErganiRows.Count == 0)
        {
            IsImportDialogOpen = false;
            return;
        }

        try
        {
            StatusMessage = "Importing Ergani declared schedule…";

            var employeesByAfm = AvailableEmployees
                .Where(e => !string.IsNullOrWhiteSpace(e.TaxId))
                .GroupBy(e => NormalizeTaxId(e.TaxId))
                .ToDictionary(g => g.Key, g => g.First());

            int saved = 0;
            int overwritten = 0;
            int overtimeCreated = 0;
            int overtimeExisting = 0;
            int leavesCreated = 0;
            int leavesExisting = 0;
            int staleRemoved = 0;
            int manualKept = 0;
            int replacedDays = 0;
            int skippedExisting = 0;

            // Overtime already in the database (so a second import does not duplicate it).
            var knownOvertime = new HashSet<(int Employee, DateOnly Date, TimeOnly Start, TimeOnly End)>();
            if (_pendingErganiRows.Any(r => r.HasOvertime) && _session?.CompanyId is int companyId)
            {
                var from = _pendingErganiRows.Min(r => r.Date);
                var to   = _pendingErganiRows.Max(r => r.Date);
                foreach (var o in await _overtimeService.GetByCompanyAsync(companyId, from, to).ConfigureAwait(false))
                    if (!o.IsCancelled)
                        knownOvertime.Add((o.EmployeeId, o.OvertimeDate, o.StartTime, o.EndTime));
            }

            foreach (var row in _pendingErganiRows)
            {
                if (!employeesByAfm.TryGetValue(NormalizeTaxId(row.TaxId), out var employee))
                    continue;

                var existing = await _scheduleService
                    .GetByDateAsync(employee.Id, row.Date)
                    .ConfigureAwait(false);

                var isConflict = _conflictKeys.Contains((employee.Id, row.Date));
                if (isConflict && !replaceExisting)
                {
                    // "No, keep the old data": this line of the file is skipped.
                    skippedExisting++;
                    continue;
                }

                if (isConflict)
                {
                    // "Yes, replace": remove the old overtime/holiday records of that day that the
                    // file does not contain; the schedule day itself is overwritten below.
                    replacedDays++;
                    if (_session?.CompanyId is int replaceCompanyId)
                    {
                        var keepOvertime = row.OvertimeRanges.Select(r => (r.Start, r.End)).ToHashSet();
                        foreach (var o in await _overtimeService.GetByCompanyAsync(replaceCompanyId, row.Date, row.Date).ConfigureAwait(false))
                            if (o.EmployeeId == employee.Id && !o.IsCancelled && !keepOvertime.Contains((o.StartTime, o.EndTime)))
                                await _overtimeService.DeleteAsync(o.Id).ConfigureAwait(false);

                        foreach (var l in await _leaveService.GetByEmployeeDateRangeAsync(replaceCompanyId, employee.Id, row.Date, row.Date).ConfigureAwait(false))
                            if (!(row.IsLeaveOnly && string.Equals(l.LeaveTypeCode, row.LeaveTypeCode, StringComparison.OrdinalIgnoreCase)))
                                await _leaveService.DeleteAsync(l.Id).ConfigureAwait(false);
                    }
                }

                if (row.IsLeaveOnly)
                {
                    // "Κανονική άδεια Έτος" etc. is a HOLIDAY (leave record), not an absence.
                    var comment = row.Employment.Length > 200 ? row.Employment[..200] : row.Employment;
                    var leave = await _leaveService.CreateRangeAsync(new LeaveDto
                    {
                        EmployeeId        = employee.Id,
                        BranchId          = employee.BranchId,
                        LeaveDate         = row.Date,
                        LeaveTypeCode     = row.LeaveTypeCode ?? "ΑΔΑΛ",
                        ReferenceYear     = row.LeaveReferenceYear,
                        Comments          = comment,
                        SubmittedToErgani = true // it comes from Ergani, so it must not be sent again
                    }, row.Date, row.Date, skipWeekends: false).ConfigureAwait(false);
                    leavesCreated  += leave.Created;
                    leavesExisting += leave.Duplicates;

                    // An earlier import stored such days as "Absent": that imported row is replaced
                    // by the holiday. A manually entered day is never deleted.
                    if (existing != null && (existing.Source == ScheduleSource.ErganiImported || (isConflict && replaceExisting)))
                    {
                        await _scheduleService.DeleteDayAsync(existing.Id).ConfigureAwait(false);
                        staleRemoved++;
                    }
                    else if (existing != null)
                        manualKept++;

                    continue;
                }

                // Work part only (ΕΡΓΑΣΙΑ hh:mm-hh:mm). Overtime is NOT merged into this block.
                await _scheduleService.UpsertDayAsync(new ScheduleDayDto
                {
                    Id           = existing?.Id ?? 0,
                    EmployeeId   = employee.Id,
                    BranchId     = employee.BranchId,
                    ScheduleDate = row.Date,
                    WorkType     = row.WorkType,
                    Source       = ScheduleSource.ErganiImported,
                    StartTime    = row.StartTime,
                    EndTime      = row.EndTime,
                    Comments     = row.Employment,
                    SubmittedToErgani = true // already declared at Ergani: must not be submitted again
                }).ConfigureAwait(false);

                if (existing != null)
                    overwritten++;
                else
                    saved++;

                // Overtime part (Υπερωρία hh:mm-hh:mm): its own record, already declared at Ergani.
                foreach (var (otStart, otEnd) in row.OvertimeRanges)
                {
                    if (!knownOvertime.Add((employee.Id, row.Date, otStart, otEnd)))
                    {
                        overtimeExisting++;
                        continue;
                    }

                    await _overtimeService.CreateAsync(new OvertimeDto
                    {
                        EmployeeId           = employee.Id,
                        BranchId             = employee.BranchId,
                        OvertimeDate         = row.Date,
                        StartTime            = otStart,
                        EndTime              = otEnd,
                        // The Ergani schedule export does not contain the justification.
                        Justification        = AppOvertimeJustification.ExceptionalWorkload,
                        WeeklyWorkdaysNumber = employee.WeeklyWorkdays,
                        SubmittedToErgani    = true
                    }, employee.CompanyId).ConfigureAwait(false);
                    overtimeCreated++;
                }
            }

            var total = saved + overwritten;

            // Record that this data was imported, with the dates it covers.
            string recorded = string.Empty;
            try
            {
                if (_session?.CompanyId is int logCompanyId && _pendingErganiRows.Count > 0)
                {
                    var entry = new ImportLogDto
                    {
                        ImportedBy      = _session.Username,
                        FileName        = Path.GetFileName(_pendingImportFilePath),
                        Origin          = _pendingImportOrigin,
                        FromDate        = _pendingErganiRows.Min(r => r.Date),
                        ToDate          = _pendingErganiRows.Max(r => r.Date),
                        EmployeeCount   = _pendingErganiRows.Select(r => NormalizeTaxId(r.TaxId)).Distinct().Count(),
                        ScheduleDays    = total,
                        OvertimeRecords = overtimeCreated,
                        HolidayDays     = leavesCreated,
                        SkippedRows     = _pendingSkippedRows + skippedExisting
                    };
                    await _importLogService.AddAsync(entry, logCompanyId);
                    recorded = $" 📝 Import recorded for {entry.PeriodText}.";
                }
            }
            catch (Exception ex)
            {
                // The history is a convenience: it must never make a finished import look failed.
                Serilog.Log.Warning(ex, "Could not record the Ergani import in the history.");
            }

            _pendingErganiRows.Clear();
            _pendingImportFilePath = string.Empty;
            IsImportDialogOpen = false;

            var extra = new List<string>();
            if (overtimeCreated + overtimeExisting > 0)
                extra.Add($"{overtimeCreated} overtime record(s) ({overtimeExisting} already existed)");
            if (leavesCreated + leavesExisting > 0)
                extra.Add($"{leavesCreated} holiday day(s) ({leavesExisting} already existed)");
            if (staleRemoved > 0)
                extra.Add($"{staleRemoved} old imported \"absent\" day(s) replaced by holidays");
            if (replacedDays > 0)
                extra.Add($"{replacedDays} day(s) with old data replaced");
            if (skippedExisting > 0)
                extra.Add($"{skippedExisting} day(s) skipped because the database already had data (kept)");
            if (manualKept > 0)
                extra.Add($"{manualKept} manual schedule day(s) on holidays left unchanged");

            StatusMessage =
                $"✅ Ergani import completed: {total} schedule day(s) imported " +
                $"({saved} new, {overwritten} updated)." +
                (extra.Count > 0 ? " " + string.Join(", ", extra) + "." : string.Empty) +
                " Imported rows are marked ERGANI IMPORTED and already SUBMITTED (they will not be sent again)." + recorded + _importNotes;

            await RefreshCalendarAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = $"❌ Ergani import failed: {ex.Message}";
        }
    }

    private static string NormalizeTaxId(string value)
    {
        var digits = new string(value.Where(char.IsDigit).ToArray());
        return digits.Length > 0 && digits.Length < 9 ? digits.PadLeft(9, '0') : digits;
    }

    // ── Submit to Ergani ──────────────────────────────────────────────────────

    /// <summary>Submits the selected days when any are selected; with no selection it submits
    /// the whole displayed month (the previous behaviour).</summary>
    [RelayCommand]
    private async Task SubmitScheduleToErganiAsync()
    {
        if (SelectedEmployee == null || _session?.CompanyId is not int companyId)
        { StatusMessage = "Select an employee first."; return; }
        try
        {
            var picked = _selectedDates.ToHashSet();
            var candidates = await LoadScopeDaysAsync(SelectedEmployee.Id, picked);

            await SubmitDaysAsync(companyId, candidates, picked.Count > 0 ? picked.Count : null);
        }
        catch (Exception ex) { StatusMessage = $"❌ {ex.Message}"; }
    }

    /// <summary>The selected days, or the whole displayed month when nothing is selected.</summary>
    private async Task<List<ScheduleDayDto>> LoadScopeDaysAsync(int employeeId, HashSet<DateOnly> picked)
    {
        if (picked.Count == 0)
            return await _scheduleService.GetMonthAsync(employeeId, Year, Month);

        // A selected week can span two months — load each month once.
        var all = new List<ScheduleDayDto>();
        foreach (var ym in picked.Select(d => (d.Year, d.Month)).Distinct())
            all.AddRange(await _scheduleService.GetMonthAsync(employeeId, ym.Year, ym.Month));
        return all.Where(c => picked.Contains(c.ScheduleDate)).ToList();
    }

    private async Task SubmitDaysAsync(int companyId, List<ScheduleDayDto> candidates, int? pickedCount)
    {
        var toSubmit = candidates
            .Where(c => !c.SubmittedToErgani && (c.WorkType is AppWorkType.Office or AppWorkType.Home))
            .ToList();

        var notes = new List<string>();
        var alreadySent = candidates.Count(c => c.SubmittedToErgani);
        var restAbsent  = candidates.Count(c => !c.SubmittedToErgani && (c.WorkType is AppWorkType.Rest or AppWorkType.Absent));
        var noSchedule  = pickedCount.HasValue ? pickedCount.Value - candidates.Count : 0;
        if (alreadySent > 0) notes.Add($"{alreadySent} already submitted");
        if (restAbsent  > 0) notes.Add($"{restAbsent} rest/absent (not sent to Ergani)");
        if (noSchedule  > 0) notes.Add($"{noSchedule} without a schedule");
        var skipped = notes.Count > 0 ? $" Skipped: {string.Join(", ", notes)}." : string.Empty;

        if (toSubmit.Count == 0)
        { StatusMessage = "Nothing to submit." + skipped; return; }

        StatusMessage = $"Submitting {toSubmit.Count} day(s) to Ergani…";

        var result = await _scheduleSubmitter.SubmitScheduleDaysAsync(companyId, toSubmit);

        // Days over 8 hours also produce an overtime record for the extra hours.
        var overtime = string.Empty;
        if (result.OvertimeSubmitted > 0 || result.OvertimeFailed > 0)
        {
            overtime = $" Overtime (hours over 8): {result.OvertimeSubmitted} sent";
            if (result.OvertimeFailed > 0)
            {
                var bad = result.Days.First(d => d.OvertimeFrom.HasValue && !d.OvertimeSubmitted);
                overtime += $", {result.OvertimeFailed} NOT sent — {bad.Date:dd/MM}: {bad.OvertimeError} " +
                            "(saved on the Overtime page)";
            }
            overtime += ".";
        }

        if (result.Error != null)
            StatusMessage = $"❌ {result.Error}";
        else if (result.Failed == 0)
            StatusMessage = $"{(result.OvertimeFailed == 0 ? "✅" : "⚠")} Submitted {result.Submitted} day(s) to Ergani.{overtime}{skipped}";
        else
        {
            var first = result.Days.First(d => !d.Success);
            StatusMessage = $"❌ {result.Submitted} submitted, {result.Failed} failed — " +
                            $"{first.Date:dd/MM}: {first.Error}. Open 📜 Log for the details.{overtime}{skipped}";
        }

        await RefreshCalendarAsync();
    }

    // ── Submission log: Ergani responses + PDF ────────────────────────────────

    /// <summary>Opens the holidays form. If the selected calendar days form one continuous block,
    /// the form starts with those dates.</summary>
    [RelayCommand]
    private async Task OpenHolidaysAsync()
    {
        if (SelectedEmployee == null || _session?.CompanyId is not int companyId)
        { StatusMessage = "Select an employee first."; return; }
        try
        {
            DateOnly? from = null, to = null;
            if (_selectedDates.Count > 0)
            {
                var min = _selectedDates.Min();
                var max = _selectedDates.Max();
                if (max.DayNumber - min.DayNumber + 1 == _selectedDates.Count) { from = min; to = max; }
            }

            await Holidays.OpenAsync(companyId, SelectedEmployee, AvailableBranches.ToList(), from, to);
        }
        catch (Exception ex) { StatusMessage = $"❌ {ex.Message}"; }
    }

    [RelayCommand]
    private async Task OpenLogAsync()
    {
        if (SelectedEmployee == null || _session?.CompanyId is not int companyId)
        { StatusMessage = "Select an employee first."; return; }
        try
        {
            var entries = await _scheduleService.GetSubmissionLogAsync(companyId, SelectedEmployee.Id);

            LogRows.Clear();
            foreach (var e in entries)
                LogRows.Add(new ScheduleLogRow
                {
                    Id              = e.Id,
                    ScheduleDate    = e.ScheduleDate,
                    SubmissionDate  = e.SubmissionDate,
                    Success         = e.Success,
                    Protocol        = e.Protocol,
                    ErrorMessage    = e.ErrorMessage,
                    HttpStatusCode  = e.HttpStatusCode,
                    ResponseRawJson = e.ResponseRawJson
                });

            HasLogRows = LogRows.Count > 0;
            LogTitle   = $"📜 Schedule log — {SelectedEmployee.FullName}";
            LogStatus  = HasLogRows ? $"{LogRows.Count} submission(s), newest first." : string.Empty;
            IsLogOpen  = true;
        }
        catch (Exception ex) { StatusMessage = $"❌ {ex.Message}"; }
    }

    [RelayCommand]
    private void CloseLog() => IsLogOpen = false;

    [RelayCommand]
    private void ShowResponse(ScheduleLogRow row)
    {
        // A failure with no HTTP response (e.g. no connection) has only the error text.
        var text = !string.IsNullOrWhiteSpace(row.ResponseRawJson) ? row.ResponseRawJson : row.ErrorMessage;
        if (string.IsNullOrWhiteSpace(text))
        { LogStatus = "No stored Ergani response for this entry."; return; }

        ResponseTitle = $"Ergani response — {row.ScheduleDateText} ({row.SubmittedAtText})";
        ResponseText  = text;
        IsResponseOpen = true;
    }

    [RelayCommand]
    private void CloseResponse() => IsResponseOpen = false;

    [RelayCommand]
    private async Task DownloadPdfAsync(ScheduleLogRow row)
    {
        if (_session?.CompanyId is not int companyId || !row.HasProtocol)
        { LogStatus = "A protocol is required before downloading the PDF."; return; }
        try
        {
            var submittedDate = DateOnly.FromDateTime(row.SubmissionDate.ToLocalTime());
            var bytes = await _documentService.DownloadPdfAsync(
                companyId, "Documents/WTODaily", row.Protocol!, submittedDate);

            var folder   = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            var who      = SelectedEmployee?.FullName ?? "employee";
            var safeName = string.Join("_", who.Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries));
            var day      = row.ScheduleDate?.ToString("yyyyMMdd") ?? "schedule";
            var path     = Path.Combine(folder, $"Ergani_Schedule_{safeName}_{day}_{row.Protocol}.pdf");

            await File.WriteAllBytesAsync(path, bytes);
            LogStatus = $"✅ PDF saved to Desktop: {Path.GetFileName(path)}";
        }
        catch (Exception ex) { LogStatus = $"❌ PDF download failed: {ex.Message}"; }
    }

    [RelayCommand]
    private async Task SubmitOvertimesAsync()
    {
        if (SelectedEmployee == null || _session?.CompanyId is not int companyId)
        { StatusMessage = "Select an employee first."; return; }
        try
        {
            var picked = _selectedDates.ToHashSet();
            var days   = await LoadScopeDaysAsync(SelectedEmployee.Id, picked);

            StatusMessage = "Detecting overtime and submitting…";
            var result = await _scheduleSubmitter.SubmitOvertimeFromScheduleAsync(companyId, days);

            if (result.Days.Count == 0)
            {
                StatusMessage = picked.Count > 0
                    ? "No overtime (over 8 hours) on the selected day(s)."
                    : "No overtime days found this month (over 8 hours).";
                return;
            }

            if (result.OvertimeFailed == 0)
                StatusMessage = $"✅ Submitted {result.OvertimeSubmitted} overtime record(s) to Ergani.";
            else
            {
                var bad = result.Days.First(d => !d.OvertimeSubmitted);
                StatusMessage = $"❌ {result.OvertimeSubmitted} overtime sent, {result.OvertimeFailed} NOT sent — " +
                                $"{bad.Date:dd/MM}: {bad.OvertimeError} (the unsent ones are saved on the Overtime page).";
            }

            await RefreshCalendarAsync();
        }
        catch (Exception ex) { StatusMessage = $"❌ {ex.Message}"; }
    }

    /// <summary>Opens the Ergani web portal in the browser, logged in with the company's saved credentials.</summary>
    [RelayCommand]
    private async Task OpenErganiPortalAsync()
    {
        if (_session?.CompanyId is not int companyId) { StatusMessage = "Select a company first."; return; }
        StatusMessage = "Opening the Ergani portal…";
        // The month shown in the calendar: its first and last day fill the page's date boxes
        var first = new DateOnly(Year, Month, 1);
        var last  = new DateOnly(Year, Month, DateTime.DaysInMonth(Year, Month));
        try { StatusMessage = await _portalService.OpenAsync(companyId, first, last); }
        catch (Exception ex) { StatusMessage = $"❌ {ex.Message}"; }
    }

    /// <summary>
    /// Same idea as <see cref="SubmitOvertimesAsync"/>: with days selected only the saved,
    /// unsent holidays on those days are submitted to Ergani; otherwise the displayed month.
    /// </summary>
    [RelayCommand]
    private async Task SubmitHolidaysAsync()
    {
        if (SelectedEmployee == null || _session?.CompanyId is not int companyId)
        { StatusMessage = "Select an employee first."; return; }
        try
        {
            var picked = _selectedDates.ToHashSet();
            DateOnly from, to;
            if (picked.Count > 0) { from = picked.Min(); to = picked.Max(); }
            else
            {
                from = new DateOnly(Year, Month, 1);
                to   = from.AddMonths(1).AddDays(-1);
            }

            var leaves = await _leaveService.GetByEmployeeDateRangeAsync(companyId, SelectedEmployee.Id, from, to);
            if (picked.Count > 0) leaves = leaves.Where(l => picked.Contains(l.LeaveDate)).ToList();

            var unsent = leaves.Where(l => !l.SubmittedToErgani).ToList();
            if (unsent.Count == 0)
            {
                StatusMessage = leaves.Count > 0
                    ? "All holidays on the selected day(s) are already submitted."
                    : picked.Count > 0
                        ? "No saved holidays on the selected day(s). Use 🏖 Holidays to add them."
                        : "No saved holidays this month. Use 🏖 Holidays to add them.";
                return;
            }

            StatusMessage = $"Submitting {unsent.Count} holiday day(s) to Ergani…";
            var result = await _leaveSubmitter.SubmitAsync(companyId, unsent.Select(l => l.Id).ToList());

            if (result.FailedCount == 0 && result.SubmittedCount > 0)
                StatusMessage = $"✅ Submitted {result.SubmittedCount} holiday day(s) to Ergani.";
            else if (result.SubmittedCount == 0)
                StatusMessage = $"❌ Not sent — {result.ErrorMessage}";
            else
                StatusMessage = $"❌ {result.SubmittedCount} day(s) sent, {result.FailedCount} NOT sent — {result.ErrorMessage}";

            await RefreshCalendarAsync();
        }
        catch (Exception ex) { StatusMessage = $"❌ {ex.Message}"; }
    }
}

/// <summary>Row in the Clone dialog — one per employee with a checkbox.</summary>
public partial class CloneTargetEmployee : ObservableObject
{
    public EmployeeDto Employee { get; init; } = null!;
    [ObservableProperty] private bool _isSelected;
    public string Name => Employee.FullName;
}
