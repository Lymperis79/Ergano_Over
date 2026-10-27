using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ErganiManager.Core.Interfaces;
using ErganiManager.Core.Models;
using ErganiManager.Data;
using Microsoft.EntityFrameworkCore;
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
    [ObservableProperty] private string _overtimeStatusText = string.Empty;
    public bool HasOvertimeStatusText => !string.IsNullOrWhiteSpace(OvertimeStatusText);
    [ObservableProperty] private bool _hasSchedule;
    [ObservableProperty] private bool _isToday;
    [ObservableProperty] private bool _isSelected;
    [ObservableProperty] private bool _isSubmitted;
    [ObservableProperty] private bool _isErganiImported;
    /// <summary>The protocol returned by Ergani for this schedule submission, if available.</summary>
    public string? Protocol { get; init; }
    public bool HasSubmissionProtocol => !string.IsNullOrWhiteSpace(Protocol);
    /// <summary>Imported schedules are already submitted, but their old protocol must not be displayed as the current status.</summary>
    public bool ShowSubmissionProtocol => IsSubmitted && !IsErganiImported && HasSubmissionProtocol;
    /// <summary>Keep the submitted status concise; show the protocol on its own line below.</summary>
    public string SubmittedStatusText => $"{Loc["Sent"]}";
    public string ProtocolStatusText => HasSubmissionProtocol
        ? string.Format(Loc["Vm.Protocol: {0}"], Protocol)
        : string.Empty;
    [ObservableProperty] private string _officialHolidayText = string.Empty;
    [ObservableProperty] private string _userHolidayText = string.Empty;
    [ObservableProperty] private string _holidayTooltip = string.Empty;

    public bool HasOfficialHoliday => !string.IsNullOrWhiteSpace(OfficialHolidayText);
    public bool HasUserHoliday => !string.IsNullOrWhiteSpace(UserHolidayText);
    public bool HasHoliday => HasOfficialHoliday || HasUserHoliday;

    public string DayNumberText => Date?.Day.ToString() ?? string.Empty;
    // ISO-8601 week number, shown on Mondays to label each calendar row.
    public string WeekNumberText => Date is DateOnly date && date.DayOfWeek == DayOfWeek.Monday
        ? $"W{System.Globalization.ISOWeek.GetWeekOfYear(date.ToDateTime(TimeOnly.MinValue)):00}"
        : string.Empty;
    public bool HasWeekNumber => !string.IsNullOrEmpty(WeekNumberText);
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
    public string EmployeeNames { get; init; } = string.Empty;
    public bool IsBatch { get; init; }
    public string SubmissionType { get; init; } = "DailySchedule";
    public string DocumentCode => SubmissionType switch
    {
        "Overtime" => "Documents/WTOOv",
        "Leave" => "Documents/WTOLeave",
        _ => "Documents/WTODaily"
    };

    public bool IsPending => ErrorMessage == "PENDING_OVERTIME";
    public string StatusIcon => IsPending ? "⏳" : Success ? "✅" : "❌";
    public string ScheduleDateText => (ScheduleDate ?? DateOnly.FromDateTime(SubmissionDate.ToLocalTime())).ToString("dd/MM/yyyy");
    public string SubmittedAtText => SubmissionDate.ToLocalTime().ToString("dd/MM/yyyy HH:mm:ss");
    public bool HasProtocol => !string.IsNullOrWhiteSpace(Protocol);
    public string Summary
    {
        get
        {
            var result = IsPending
                ? Tr.Loc["Vm.⏳ Pending — generated from schedule; submit from Overtime page"]
                : Success
                    ? (HasProtocol ? string.Format(Tr.Loc["Vm.Protocol: {0}"], Protocol) : Tr.Loc["Vm.Accepted by Ergani"])
                    : (string.IsNullOrWhiteSpace(ErrorMessage) ? Tr.Loc["Vm.Failed"] : ErrorMessage!);
            return IsBatch && !string.IsNullOrWhiteSpace(EmployeeNames)
                ? $"{EmployeeNames} · {result}"
                : result;
        }
    }
}

public partial class SchedulesViewModel : ViewModelBase, IAdminSectionViewModel
{
    private readonly IScheduleService   _scheduleService;
    private readonly IEmployeeService   _employeeService;
    private readonly IBranchService     _branchService;
    private readonly IScheduleSubmitter _scheduleSubmitter;
    private readonly IErganiDocumentService _documentService;
    private readonly IConnectionStateService _connectionState;
    private readonly IRoundingSettingsService _roundingSettings;
    private readonly ILeaveService _leaveService;
    private readonly IOvertimeService _overtimeService;
    private readonly IImportLogService _importLogService;
    private readonly IErganiPortalService _portalService;
    private readonly ILeaveSubmitter _leaveSubmitter;
    private UserSession? _session;

    // ── Employee / navigation ─────────────────────────────────────────────────
    public ObservableCollection<EmployeeDto> AvailableEmployees { get; } = new();
    public ObservableCollection<ErganiManager.Data.Entities.OvertimeJustification> AvailableOvertimeJustifications { get; } =
        new(Enum.GetValues<ErganiManager.Data.Entities.OvertimeJustification>());
    [ObservableProperty] private ErganiManager.Data.Entities.OvertimeJustification _editingOvertimeJustification =
        ErganiManager.Data.Entities.OvertimeJustification.ExceptionalWorkload;
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
            monday = monday.AddDays(WeekOffset * 7);
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
        ? Loc["Vm.No days selected"]
        : string.Format(Loc["Vm.{0} day(s) selected"], _selectedDates.Count);

    /// <summary>With days selected the button submits only those; otherwise it submits the month.</summary>
    public string SubmitScheduleButtonText => HasSelection
        ? string.Format(Loc["Vm.📤 Submit selected ({0})"], _selectedDates.Count)
        : Loc["Vm.📤 Submit Schedule"];
    public string SubmitOvertimeButtonText => HasSelection
        ? string.Format(Loc["Vm.⏱ Overtime of selected ({0})"], _selectedDates.Count)
        : Loc["Vm.⏱ Submit Overtime"];
    public string SubmitHolidaysButtonText => HasSelection
        ? string.Format(Loc["Vm.🏖 Holidays of selected ({0})"], _selectedDates.Count)
        : Loc["Vm.🏖 Submit Holidays"];
    public string DeleteSelectedButtonText => string.Format(Loc["Vm.🗑 Delete selected ({0})"], _selectedDates.Count);

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

    // ── Submit pending schedules for every active employee in the selected month ──
    // The bulk dialog is shared: either "submit all pending schedules" or "submit overtime of all employees".
    private bool _bulkOvertimeMode;
    private bool _bulkHolidayMode;
    private List<int> _pendingLeaveIds = new();
    private List<ScheduleDayDto> _pendingOvertimeDays = new();
    [ObservableProperty] private bool _isBulkSubmitDialogOpen;
    [ObservableProperty] private string _bulkSubmitSummary = string.Empty;
    private List<(EmployeeDto Employee, List<ScheduleDayDto> Days)> _pendingBulkSubmit = new();
    private int _pendingBulkSubmitYear;
    private int _pendingBulkSubmitMonth;
    [ObservableProperty] private bool _isBulkSubmitting;

    // ── Submission log (Ergani responses + PDF) ───────────────────────────────
    /// <summary>The holidays form (opened with the 🏖 Holidays button).</summary>
    public HolidayDialogViewModel Holidays { get; }

    public ObservableCollection<ScheduleLogRow> LogRows { get; } = new();
    [ObservableProperty] private bool _isLogOpen;
    [ObservableProperty] private bool _hasLogRows;
    [ObservableProperty] private string _logTitle = string.Empty;
    [ObservableProperty] private string _logStatus = string.Empty;
    [ObservableProperty] private string _logEmptyText = string.Empty;
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
    private List<DateOnly> _cloneDates = new();
    // The keys are the plain L. constants (no "Vm." prefix): with the prefix the lookup found nothing
    // and the dialog showed the bare key name instead of the message.
    public string CloneScopeDescription => _cloneDates.Count > 0
        ? string.Format(Loc[L.CloneSelectedDaysConfirmation], _cloneDates.Count, string.Join(", ", _cloneDates.OrderBy(d => d).Select(d => d.ToString("dd/MM/yyyy"))))
        : string.Format(Loc[L.CloneWholePeriodConfirmation], CloneFrom?.ToString("dd/MM/yyyy"), CloneTo?.ToString("dd/MM/yyyy"));
    public ObservableCollection<CloneTargetEmployee> CloneTargets { get; } = new();
    public ObservableCollection<CloneTargetEmployee> FilteredCloneTargets { get; } = new();
    [ObservableProperty] private string _cloneTargetSearch = string.Empty;

    partial void OnCloneTargetSearchChanged(string value) => RefreshFilteredCloneTargets();

    private void RefreshFilteredCloneTargets()
    {
        var query = CloneTargetSearch.Trim();
        var matches = string.IsNullOrEmpty(query)
            ? CloneTargets.ToList()
            : CloneTargets.Where(t => t.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase)).ToList();

        FilteredCloneTargets.Clear();
        foreach (var target in matches)
            FilteredCloneTargets.Add(target);
    }

    // What the copy would replace for the employees ticked in the dialog (filled from the database).
    private Dictionary<int, CloneImpactDto> _cloneImpact = new();
    // Holidays: copied together with the schedule when ticked (ticked automatically if the source has any).
    private int _cloneSourceLeaveCount;
    [ObservableProperty] private bool _cloneIncludeHolidays;
    partial void OnCloneIncludeHolidaysChanged(bool value) => UpdateCloneImpactText();
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasCloneImpactInfo))] private string _cloneImpactInfoText = string.Empty;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasCloneImpactDanger))] private string _cloneImpactDangerText = string.Empty;
    public bool HasCloneImpactInfo => !string.IsNullOrEmpty(CloneImpactInfoText);
    public bool HasCloneImpactDanger => !string.IsNullOrEmpty(CloneImpactDangerText);

    // ── Clone history / Undo last clone ───────────────────────────────────────
    private CloneBatchInfoDto? _lastClone;
    [ObservableProperty] private bool _canUndoLastClone;
    [ObservableProperty] private bool _isUndoCloneDialogOpen;
    [ObservableProperty] private bool _undoCloneBlocked;
    [ObservableProperty] private string _undoCloneSummary = string.Empty;
    [ObservableProperty] private string _undoCloneScope = string.Empty;
    [ObservableProperty] private string _undoCloneEffect = string.Empty;
    [ObservableProperty] private string _undoCloneBlockedText = string.Empty;
    public ObservableCollection<CloneHistoryRow> CloneHistory { get; } = new();
    [ObservableProperty] private bool _hasCloneHistory;

    public ObservableCollection<BranchDto> AvailableBranches { get; } = new();
    public ObservableCollection<AppWorkType> AvailableWorkTypes { get; } =
        new(Enum.GetValues<AppWorkType>());
    public bool ShowTimeFields     => EditingWorkType is AppWorkType.Office or AppWorkType.Home;
    public bool BulkShowTimeFields => BulkWorkType    is AppWorkType.Office or AppWorkType.Home;
    public bool IsEditingExisting  => EditingScheduleId != 0;
    /// <summary>Only Office/Home days that are not yet at Ergani can be submitted from the day dialog.</summary>
    public bool CanSubmitEditingDay => ShowTimeFields && !EditingSubmitted;
    public bool HasEditingOvertime =>
        ShowTimeFields && EditingStartTime.HasValue && EditingEndTime.HasValue &&
        ScheduleHours.GetOvertimeRange(
            EditingStartTime.HasValue ? TimeOnly.FromTimeSpan(EditingStartTime.Value) : null,
            EditingEndTime.HasValue ? TimeOnly.FromTimeSpan(EditingEndTime.Value) : null).HasValue;

    /// <summary>Shown when the day is longer than 8 hours: how it will be split for Ergani.</summary>
    public string EditingOvertimeNote
    {
        get
        {
            if (!ShowTimeFields || !EditingStartTime.HasValue || !EditingEndTime.HasValue) return string.Empty;
            var start = TimeOnly.FromTimeSpan(EditingStartTime.Value);
            var end   = TimeOnly.FromTimeSpan(EditingEndTime.Value);
            if (ScheduleHours.GetOvertimeRange(start, end) is not { } ot) return string.Empty;
            return string.Format(Loc["Vm.⏱ Over {0} hours — {1}–{2} is submitted as the schedule, {2}–{3} is submitted as overtime."],
                ScheduleHours.NormalDailyHours, start.ToString("HH:mm"), ot.From.ToString("HH:mm"), ot.To.ToString("HH:mm"));
        }
    }

    public SchedulesViewModel(IScheduleService scheduleService,
        IEmployeeService employeeService, IBranchService branchService,
        IScheduleSubmitter scheduleSubmitter, IErganiDocumentService documentService,
        ILeaveService leaveService, ILeaveSubmitter leaveSubmitter,
        IErganiPortalService portalService, IRoundingSettingsService roundingSettings,
        IOvertimeService overtimeService, IImportLogService importLogService,
        IConnectionStateService connectionState)
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
        _connectionState   = connectionState;
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
            : Loc["Vm.Select a company first."];
        if (HasActiveCompany)
        {
            _ = LoadFiltersAsync();
            _ = RefreshCloneStateAsync();
        }
    }

    /// <summary>Refreshes the visible calendar when returning to the Schedules section.</summary>
    public Task RefreshAsync() => RefreshCalendarAsync();

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
        WeekOffset--;
        OnPropertyChanged(nameof(WeekLabel));
        await RefreshCalendarAsync();
    }

    [RelayCommand]
    private async Task NextWeekAsync()
    {
        WeekOffset++;
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
        {
            // Merge overlapping/adjacent overtime entries for the same day so extending
            // 16:00–17:00 to 16:00–18:00 is displayed as one interval, not both.
            var merged = new List<(TimeOnly From, TimeOnly To)>();
            foreach (var record in records.OrderBy(o => o.StartTime).ThenBy(o => o.EndTime))
            {
                if (merged.Count == 0 || record.StartTime > merged[^1].To)
                {
                    merged.Add((record.StartTime, record.EndTime));
                    continue;
                }

                var last = merged[^1];
                if (record.EndTime > last.To)
                    merged[^1] = (last.From, record.EndTime);
            }

            return "⏱ " + string.Join(", ", merged.Select(o => $"{o.From:HH:mm}–{o.To:HH:mm}"));
        }

        if (sched is { WorkType: AppWorkType.Office or AppWorkType.Home } &&
            ScheduleHours.GetOvertimeRange(sched.StartTime, sched.EndTime) is { } ot)
            return $"⏱ {ot.From:HH:mm}–{ot.To:HH:mm}";

        return string.Empty;
    }

    private string BuildOvertimeStatusText(List<OvertimeDto>? records)
    {
        if (records is not { Count: > 0 }) return string.Empty;
        return records.All(o => o.SubmittedToErgani) ? Loc["Sent"] : Loc["Pending"];
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

    private static string DeclaredLeaveRangeText(List<LeaveDto>? leaves)
    {
        var leave = leaves?.FirstOrDefault(l => l.DeclaredStartTime.HasValue && l.DeclaredEndTime.HasValue);
        return leave is null ? string.Empty : $"{leave.DeclaredStartTime:HH:mm}–{leave.DeclaredEndTime:HH:mm}";
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
            // The calendar already displays the schedule/leave "Sent" badge below. Avoid a second identical badge when overtime is also submitted.
            var overtimeStatusText = sched is { SubmittedToErgani: true }
                ? string.Empty
                : BuildOvertimeStatusText(dayOvertime);
            official.TryGetValue(date, out var officialName);
            var userHolidayText = dayLeaves is null ? string.Empty : string.Join(" · ", dayLeaves.Select(l => LeaveTypes.Find(l.LeaveTypeCode)?.LocalizedDescription ?? l.LeaveTypeCode));
            // Annual leave takes precedence over the imported work-schedule status in the calendar.
            var hasAnnualLeave = dayLeaves?.Any(l => string.Equals(l.LeaveTypeCode, "ΑΔΚΑΝ", StringComparison.OrdinalIgnoreCase)) == true;
            var tooltip = string.Join("\n", new[]
            {
                string.IsNullOrWhiteSpace(officialName) ? null : $"🇬🇷 {officialName}",
                string.IsNullOrWhiteSpace(userHolidayText) ? null : $"🏖 {userHolidayText}",
                string.IsNullOrWhiteSpace(overtimeText) ? null : string.Format(Loc["Vm.{0} overtime"], overtimeText),
                !hasAnnualLeave && sched?.Source == ScheduleSource.ErganiImported
                    ? Loc["Vm.🇬🇷 Ergani imported"]
                    : !hasAnnualLeave && sched is { SubmittedToErgani: true }
                        ? (string.IsNullOrWhiteSpace(sched.Protocol)
                            ? Loc["Sent"]
                            : string.Format(Loc["Vm.Protocol: {0}"], sched.Protocol))
                        : null
            }.Where(x => !string.IsNullOrWhiteSpace(x)));
            cells.Add(new CalendarCellViewModel
            {
                IsInMonth          = true,
                Date               = date,
                HasSchedule        = sched != null,
                // Show a sent badge for annual leave only when Ergani has accepted the leave record.
                IsSubmitted        = hasAnnualLeave
                    ? dayLeaves!.Where(l => string.Equals(l.LeaveTypeCode, "ΑΔΚΑΝ", StringComparison.OrdinalIgnoreCase)).All(l => l.SubmittedToErgani)
                    : sched is { SubmittedToErgani: true },
                IsErganiImported  = !hasAnnualLeave && sched?.Source == ScheduleSource.ErganiImported,
                Protocol           = hasAnnualLeave ? null : sched?.Protocol,
                WorkType           = sched != null ? (AppWorkType?)sched.WorkType : null,
                TimeRangeText      = WorkRangeText(sched) is { Length: > 0 } workRange ? workRange : DeclaredLeaveRangeText(dayLeaves),
                OvertimeText  = overtimeText,
                OvertimeStatusText = overtimeStatusText,
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
            var overtimeStatusText = BuildOvertimeStatusText(dayOvertime);
            officialByDate.TryGetValue(d, out var officialName);
            var userHolidayText = dayLeaves is null ? string.Empty : string.Join(" · ", dayLeaves.Select(l => LeaveTypes.Find(l.LeaveTypeCode)?.LocalizedDescription ?? l.LeaveTypeCode));
            // Annual leave takes precedence over the imported work-schedule status in the calendar.
            var hasAnnualLeave = dayLeaves?.Any(l => string.Equals(l.LeaveTypeCode, "ΑΔΚΑΝ", StringComparison.OrdinalIgnoreCase)) == true;
            var tooltip = string.Join("\n", new[]
            {
                string.IsNullOrWhiteSpace(officialName) ? null : $"🇬🇷 {officialName}",
                string.IsNullOrWhiteSpace(userHolidayText) ? null : $"🏖 {userHolidayText}",
                string.IsNullOrWhiteSpace(overtimeText) ? null : string.Format(Loc["Vm.{0} overtime"], overtimeText),
                !hasAnnualLeave && sched?.Source == ScheduleSource.ErganiImported
                    ? Loc["Vm.🇬🇷 Ergani imported"]
                    : !hasAnnualLeave && sched is { SubmittedToErgani: true }
                        ? (string.IsNullOrWhiteSpace(sched.Protocol)
                            ? Loc["Sent"]
                            : string.Format(Loc["Vm.Protocol: {0}"], sched.Protocol))
                        : null
            }.Where(x => !string.IsNullOrWhiteSpace(x)));
            cells.Add(new CalendarCellViewModel
            {
                IsInMonth           = true,
                Date                = d,
                HasSchedule         = sched != null,
                // Show a sent badge for annual leave only when Ergani has accepted the leave record.
                IsSubmitted         = hasAnnualLeave
                    ? dayLeaves!.Where(l => string.Equals(l.LeaveTypeCode, "ΑΔΚΑΝ", StringComparison.OrdinalIgnoreCase)).All(l => l.SubmittedToErgani)
                    : sched is { SubmittedToErgani: true },
                IsErganiImported   = !hasAnnualLeave && sched?.Source == ScheduleSource.ErganiImported,
                Protocol            = hasAnnualLeave ? null : sched?.Protocol,
                WorkType            = sched != null ? (AppWorkType?)sched.WorkType : null,
                TimeRangeText       = WorkRangeText(sched) is { Length: > 0 } workRange ? workRange : DeclaredLeaveRangeText(dayLeaves),
                OvertimeText   = overtimeText,
                OvertimeStatusText = overtimeStatusText,
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
        EditingOvertimeJustification = ErganiManager.Data.Entities.OvertimeJustification.ExceptionalWorkload;
        if (_session?.CompanyId is int companyIdForOvertime)
        {
            var overtimeRecords = await _overtimeService.GetByCompanyAsync(companyIdForOvertime, date, date);
            var overtimeRange = ScheduleHours.GetOvertimeRange(
                EditingStartTime.HasValue ? TimeOnly.FromTimeSpan(EditingStartTime.Value) : null,
                EditingEndTime.HasValue ? TimeOnly.FromTimeSpan(EditingEndTime.Value) : null);
            var savedOvertime = overtimeRange.HasValue
                ? overtimeRecords.FirstOrDefault(o => o.EmployeeId == SelectedEmployee.Id &&
                    o.StartTime == overtimeRange.Value.From && o.EndTime == overtimeRange.Value.To && !o.IsCancelled)
                : null;
            if (savedOvertime != null &&
                Enum.TryParse<ErganiManager.Data.Entities.OvertimeJustification>(savedOvertime.Justification.ToString(), out var savedJustification))
                EditingOvertimeJustification = savedJustification;
        }
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
        OnPropertyChanged(nameof(HasEditingOvertime));
    }

    partial void OnEditingStartTimeChanged(TimeSpan? value)
    {
        OnPropertyChanged(nameof(EditingOvertimeNote));
        OnPropertyChanged(nameof(HasEditingOvertime));
    }

    partial void OnEditingEndTimeChanged(TimeSpan? value)
    {
        OnPropertyChanged(nameof(EditingOvertimeNote));
        OnPropertyChanged(nameof(HasEditingOvertime));
    }

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
            var saved = await _scheduleService.GetByDateAsync(SelectedEmployee.Id, EditingDate);
            if (saved is { Id: > 0 })
                await EnsureScheduleOvertimePendingAsync(saved);
            IsDayDialogOpen = false;
            await RefreshCalendarAsync();
        }
        catch (Exception ex) { StatusMessage = $"❌ {ex.Message}"; }
    }

    private async Task EnsureScheduleOvertimePendingAsync(ScheduleDayDto schedule)
    {
        if (_session?.CompanyId is not int companyId) return;
        await using var db = new AppDbContext(_connectionState.GetDbOptions());
        var range = schedule.WorkType is AppWorkType.Office or AppWorkType.Home
            ? ScheduleHours.GetOvertimeRange(schedule.StartTime, schedule.EndTime) : null;

        if (range is null)
        {
            var generated = await db.Overtimes.Where(o =>
                o.EmployeeId == schedule.EmployeeId && o.OvertimeDate == schedule.ScheduleDate &&
                !o.SubmittedToErgani && !o.IsCancelled && o.RequestPayloadJson != null &&
                o.RequestPayloadJson.Contains("\"source\":\"schedule\"")).ToListAsync();
            if (generated.Count > 0)
            {
                var ids = generated.Select(o => o.Id).ToHashSet();
                db.Overtimes.RemoveRange(generated);
                var logs = await db.ApiSubmissionLogs.Where(l =>
                    l.CompanyId == companyId && l.SubmissionType == "Overtime" && l.ErrorMessage == "PENDING_OVERTIME").ToListAsync();
                foreach (var log in logs)
                    if (TryGetPendingOvertimeId(log.RequestPayloadJson, out var id) && ids.Contains(id)) db.ApiSubmissionLogs.Remove(log);
                await db.SaveChangesAsync();
            }
            return;
        }

        var staleGenerated = await db.Overtimes.Where(o =>
            o.EmployeeId == schedule.EmployeeId && o.OvertimeDate == schedule.ScheduleDate &&
            !o.SubmittedToErgani && !o.IsCancelled && o.RequestPayloadJson != null &&
            o.RequestPayloadJson.Contains("\"source\":\"schedule\"") &&
            (o.StartTime != range.Value.From || o.EndTime != range.Value.To)).ToListAsync();
        if (staleGenerated.Count > 0)
        {
            var staleIds = staleGenerated.Select(o => o.Id).ToHashSet();
            db.Overtimes.RemoveRange(staleGenerated);
            var staleLogs = await db.ApiSubmissionLogs.Where(l =>
                l.CompanyId == companyId && l.SubmissionType == "Overtime" && l.ErrorMessage == "PENDING_OVERTIME").ToListAsync();
            foreach (var log in staleLogs)
                if (TryGetPendingOvertimeId(log.RequestPayloadJson, out var staleId) && staleIds.Contains(staleId))
                    db.ApiSubmissionLogs.Remove(log);
            await db.SaveChangesAsync();
        }

        var existing = await db.Overtimes.FirstOrDefaultAsync(o =>
            o.EmployeeId == schedule.EmployeeId && o.OvertimeDate == schedule.ScheduleDate &&
            o.StartTime == range.Value.From && o.EndTime == range.Value.To && !o.IsCancelled);
        if (existing?.SubmittedToErgani == true) return;
        if (existing == null)
        {
            var employee = await db.Employees.FindAsync(schedule.EmployeeId);
            if (employee == null) return;
            existing = new ErganiManager.Data.Entities.Overtime
            {
                EmployeeId = schedule.EmployeeId, BranchId = schedule.BranchId,
                OvertimeDate = schedule.ScheduleDate, StartTime = range.Value.From, EndTime = range.Value.To,
                Justification = EditingOvertimeJustification, WeeklyWorkdaysNumber = employee.WeeklyWorkdays,
                RequestPayloadJson = System.Text.Json.JsonSerializer.Serialize(new { source = "schedule", scheduleId = schedule.Id }),
                CreatedAt = DateTime.UtcNow
            };
            db.Overtimes.Add(existing);
            await db.SaveChangesAsync();
        }
        else
        {
            existing.Justification = EditingOvertimeJustification;
            if (string.IsNullOrWhiteSpace(existing.RequestPayloadJson))
                existing.RequestPayloadJson = System.Text.Json.JsonSerializer.Serialize(new { source = "schedule", scheduleId = schedule.Id });
            await db.SaveChangesAsync();
        }

        var marker = "\"overtimeId\":" + existing.Id;
        var pendingLogExists = await db.ApiSubmissionLogs.AnyAsync(l =>
            l.CompanyId == companyId && l.SubmissionType == "Overtime" && l.ErrorMessage == "PENDING_OVERTIME" &&
            l.RequestPayloadJson != null && l.RequestPayloadJson.Contains(marker));
        if (!pendingLogExists)
        {
            db.ApiSubmissionLogs.Add(new ErganiManager.Data.Entities.ApiSubmissionLog
            {
                CompanyId = companyId, EmployeeId = schedule.EmployeeId,
                EmployeeIdsJson = System.Text.Json.JsonSerializer.Serialize(new[] { schedule.EmployeeId }),
                SubmissionType = "Overtime", ScheduleDate = schedule.ScheduleDate,
                RequestPayloadJson = System.Text.Json.JsonSerializer.Serialize(new { overtimeId = existing.Id, source = "schedule" }),
                SubmissionDate = DateTime.UtcNow, Success = false, ErrorMessage = "PENDING_OVERTIME"
            });
            await db.SaveChangesAsync();
        }
    }

    private static bool TryGetPendingOvertimeId(string? payload, out int id)
    {
        id = 0;
        if (string.IsNullOrWhiteSpace(payload)) return false;
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(payload);
            return doc.RootElement.TryGetProperty("overtimeId", out var value) && value.TryGetInt32(out id);
        }
        catch (System.Text.Json.JsonException) { return false; }
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
            { StatusMessage = Loc["Vm.❌ Could not reload the saved day."]; return; }

            await EnsureScheduleOvertimePendingAsync(saved);
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
            StatusMessage = Loc["Vm.There is no schedule to delete on the selected day(s)."];
            return;
        }

        var submitted = withSchedule.Count(d => byDate[d].IsSubmitted);

        var message = withSchedule.Count == 1
            ? string.Format(Loc["Vm.Delete the schedule for {0}?"], withSchedule[0].ToString("dd/MM/yyyy"))
            : string.Format(Loc["Vm.Delete the schedule of {0} days?"], withSchedule.Count);

        if (submitted > 0)
            message += submitted == 1 && withSchedule.Count == 1
                ? "\n\n" + Loc["Vm.This day was already sent to Ergani. Deleting only removes it from this app — it does not change what Ergani received."]
                : "\n\n" + string.Format(Loc["Vm.{0} of them were already sent to Ergani. Deleting only removes them from this app — it does not change what Ergani received."], submitted);

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
            StatusMessage = string.Format(Loc["Vm.✅ Deleted the schedule of {0} day(s)."], removed);
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
            StatusMessage = string.Format(Loc["Vm.✅ Applied to {0} day(s)."], count);
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

        if (HasSelection)
        {
            _cloneDates = _selectedDates.OrderBy(d => d).ToList();
            CloneFrom = _cloneDates.First();
            CloneTo = _cloneDates.Last();
        }
        else
        {
            _cloneDates.Clear();
            if (ViewMode == ScheduleViewMode.Week)
            {
                var (f, t) = CurrentWeekRange;
                CloneFrom = f; CloneTo = t;
            }
            else
            {
                CloneFrom = new DateOnly(Year, Month, 1);
                CloneTo = new DateOnly(Year, Month, DateTime.DaysInMonth(Year, Month));
            }
        }
        OnPropertyChanged(nameof(CloneScopeDescription));

        var targets = AvailableEmployees
            .Where(e => e.Id != SelectedEmployee.Id)
            .Select(e => new CloneTargetEmployee { Employee = e })
            .ToList();

        // How much of each employee's existing schedule the copy would replace (and how much of it was
        // already sent to Ergani). A failure here only hides the extra warning; it never blocks the copy.
        _cloneImpact = new Dictionary<int, CloneImpactDto>();
        _cloneSourceLeaveCount = 0;
        CloneIncludeHolidays = false;
        try
        {
            var impactDates = _cloneDates.Count > 0
                ? _cloneDates.ToList()
                : ExpandDates(CloneFrom!.Value, CloneTo!.Value);

            if (_session?.CompanyId is int leaveCompanyId && impactDates.Count > 0)
            {
                var dateSet = impactDates.ToHashSet();
                var sourceLeaves = await _leaveService.GetByEmployeeDateRangeAsync(
                    leaveCompanyId, SelectedEmployee.Id, impactDates.Min(), impactDates.Max());
                _cloneSourceLeaveCount = sourceLeaves.Count(l => dateSet.Contains(l.LeaveDate));
                CloneIncludeHolidays = _cloneSourceLeaveCount > 0;
            }
            var impact = await _scheduleService.GetCloneImpactAsync(
                targets.Select(t => t.Employee.Id).ToList(), impactDates);
            _cloneImpact = impact.ToDictionary(i => i.EmployeeId);
        }
        catch { /* the warning below simply stays hidden */ }

        await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
        {
            CloneTargets.Clear();
            foreach (var t in targets)
            {
                t.SelectionChanged += (_, _) => UpdateCloneImpactText();
                CloneTargets.Add(t);
            }
            CloneTargetSearch = string.Empty;
            RefreshFilteredCloneTargets();
            UpdateCloneImpactText();
        });

        IsCloneDialogOpen = true;
    }

    private static List<DateOnly> ExpandDates(DateOnly from, DateOnly to)
    {
        var list = new List<DateOnly>();
        for (var d = from; d <= to; d = d.AddDays(1)) list.Add(d);
        return list;
    }

    /// <summary>Warning under the scope text, for the employees currently ticked in the Clone dialog.</summary>
    private void UpdateCloneImpactText()
    {
        var selected = CloneTargets.Where(t => t.IsSelected).ToList();
        if (selected.Count == 0)
        {
            CloneImpactInfoText = string.Empty;
            CloneImpactDangerText = string.Empty;
            return;
        }

        int existing = 0, sent = 0, overtimeUnsent = 0, overtimeSent = 0, leaveUnsent = 0, leaveSent = 0;
        foreach (var t in selected)
        {
            if (!_cloneImpact.TryGetValue(t.Employee.Id, out var impact)) continue;
            existing += impact.ExistingEntries;
            sent += impact.SubmittedEntries;
            overtimeUnsent += impact.UnsentOvertimeEntries;
            overtimeSent += impact.SubmittedOvertimeEntries;
            leaveUnsent += impact.UnsentLeaveEntries;
            leaveSent += impact.SubmittedLeaveEntries;
        }

        string text;
        if (sent > 0)
            text = string.Format(
                Loc["Vm.⚠ {0} existing schedule entry(ies) will be replaced, and {1} of them were already sent to Ergani. The new entries will appear as not sent and must be submitted again; what Ergani already received is not changed by this copy."],
                existing, sent);
        else if (existing == 0)
            text = Loc["Vm.The selected employees have no schedule on these dates, so nothing will be replaced."];
        else
            text = string.Format(Loc["Vm.{0} existing schedule entry(ies) of the selected employees will be replaced."], existing);

        // Overtime (hours over 8) follows the end time: unsent records are removed, sent ones stay in Ergani.
        if (overtimeUnsent > 0)
            text += " " + string.Format(
                Loc["Vm.{0} unsent overtime record(s) of these employees on these dates will be removed; overtime over 8 hours is created again from the new end times when you send it."],
                overtimeUnsent);
        if (overtimeSent > 0)
            text += " " + string.Format(
                Loc["Vm.⚠ {0} overtime record(s) on these dates were already sent to Ergani; they stay in Ergani and are not changed by this copy. If the new end time differs, send the new overtime and correct the old one in Ergani."],
                overtimeSent);

        var leaveDanger = false;
        if (CloneIncludeHolidays)
        {
            if (_cloneSourceLeaveCount > 0)
                text += " " + string.Format(
                    Loc["Vm.{0} holiday day(s) of the source will be copied to each selected employee."], _cloneSourceLeaveCount);
            if (leaveUnsent > 0)
                text += " " + string.Format(
                    Loc["Vm.{0} unsent holiday day(s) of these employees on these dates will be replaced."], leaveUnsent);
            if (leaveSent > 0)
            {
                leaveDanger = true;
                text += " " + string.Format(
                    Loc["Vm.⚠ {0} holiday day(s) on these dates were already sent to Ergani; they stay in Ergani and those dates are skipped."], leaveSent);
            }
        }

        var danger = sent > 0 || overtimeSent > 0 || leaveDanger;
        CloneImpactDangerText = danger ? text : string.Empty;
        CloneImpactInfoText = danger ? string.Empty : text;
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
            StatusMessage = Loc["Vm.Select at least one employee to copy to."];
            return;
        }

        try
        {
            var user = _session?.Username;
            var count = _cloneDates.Count > 0
                ? await _scheduleService.CopyToEmployeesAsync(SelectedEmployee.Id, _cloneDates, targetIds, user, CloneIncludeHolidays)
                : await _scheduleService.CopyToEmployeesAsync(SelectedEmployee.Id, CloneFrom.Value, CloneTo.Value, targetIds, user, CloneIncludeHolidays);

            IsCloneDialogOpen = false;
            StatusMessage = count == 0
                ? Loc["Vm.Nothing was copied: this employee has no schedule on the selected dates."]
                : string.Format(Loc["Vm.✅ Copied {0} entry(ies) to {1} employee(s). You can undo this until the copied entries are submitted."], count, targetIds.Count);

            await RefreshCloneStateAsync();
        }
        catch (Exception ex) { StatusMessage = $"❌ {ex.Message}"; }
    }

    // ── Undo last clone ───────────────────────────────────────────────────────

    private async Task RefreshCloneStateAsync()
    {
        if (_session?.CompanyId is not int companyId)
        {
            _lastClone = null;
            CanUndoLastClone = false;
            return;
        }

        try
        {
            _lastClone = await _scheduleService.GetLastCloneAsync(companyId);
            CanUndoLastClone = _lastClone != null;
        }
        catch
        {
            _lastClone = null;
            CanUndoLastClone = false;
        }
    }

    [RelayCommand]
    private async Task OpenUndoCloneDialogAsync()
    {
        if (_session?.CompanyId is not int companyId) return;

        try
        {
            // Read again: the entries may have been submitted since the dialog was last opened.
            _lastClone = await _scheduleService.GetLastCloneAsync(companyId);
            var history = await _scheduleService.GetCloneHistoryAsync(companyId, 10);
            CanUndoLastClone = _lastClone != null;

            if (_lastClone == null)
            {
                StatusMessage = Loc["Vm.There is no clone to undo."];
                return;
            }

            BuildUndoCloneTexts(_lastClone);

            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
            {
                CloneHistory.Clear();
                foreach (var h in history) CloneHistory.Add(ToHistoryRow(h));
                HasCloneHistory = CloneHistory.Count > 0;
            });

            IsUndoCloneDialogOpen = true;
        }
        catch (Exception ex) { StatusMessage = $"❌ {ex.Message}"; }
    }

    private void BuildUndoCloneTexts(CloneBatchInfoDto clone)
    {
        UndoCloneSummary = string.Format(Loc["Vm.Schedule copied from {0} to {1} on {2}."],
            clone.SourceEmployeeName, FormatCloneTargets(clone), clone.CreatedAtUtc.ToLocalTime().ToString("dd/MM/yyyy HH:mm"));

        UndoCloneScope = string.Format(Loc["Vm.Dates: {0} day(s) between {1} and {2}."],
            clone.DayCount, clone.FirstDate.ToString("dd/MM/yyyy"), clone.LastDate.ToString("dd/MM/yyyy"));

        if (clone.SubmittedEntries > 0)
        {
            // Part of what the copy created has already gone to Ergani: undoing only here would be misleading.
            UndoCloneBlocked = true;
            UndoCloneBlockedText = string.Format(
                Loc["Vm.⛔ Undo is not possible: {0} entry(ies) on these dates have already been submitted to Ergani. Undoing here cannot withdraw a submission from Ergani and would leave this app showing something different from what Ergani holds. Correct them with a new submission or in the Ergani portal."],
                clone.SubmittedEntries);
            UndoCloneEffect = string.Empty;
            return;
        }

        UndoCloneBlocked = false;
        UndoCloneBlockedText = string.Empty;
        if (clone.ReplacedEntries == 0)
        {
            UndoCloneEffect = Loc["Vm.Undo will remove the copied entries. The destination employees had no schedule on these dates before the copy."];
        }
        else
        {
            var effect = string.Format(
                Loc["Vm.Undo will remove the copied entries and restore the {0} previous entry(ies) of the destination employees."],
                clone.ReplacedEntries);
            if (clone.ReplacedSubmittedEntries > 0)
                effect += " " + string.Format(
                    Loc["Vm.{0} of them had already been sent to Ergani; they will return with their previous status and protocol numbers."],
                    clone.ReplacedSubmittedEntries);
            UndoCloneEffect = effect;
        }

        if (clone.ReplacedLeaveEntries > 0)
            UndoCloneEffect += " " + string.Format(
                Loc["Vm.{0} unsent holiday day(s) of these days will be restored too."], clone.ReplacedLeaveEntries);

        if (clone.ReplacedOvertimeEntries > 0)
            UndoCloneEffect += " " + string.Format(
                Loc["Vm.{0} unsent overtime record(s) of these days will be restored too."], clone.ReplacedOvertimeEntries);
    }

    /// <summary>"Maria K., Nikos P., Eleni A. (+2)" — the first three names, then how many more.</summary>
    private string FormatCloneTargets(CloneBatchInfoDto clone)
    {
        if (clone.TargetNames.Count == 0)
            return string.Format(Loc["Vm.{0} employee(s)"], clone.TargetCount);
        var shown = string.Join(", ", clone.TargetNames.Take(3));
        return clone.TargetNames.Count > 3 ? $"{shown} (+{clone.TargetNames.Count - 3})" : shown;
    }

    private CloneHistoryRow ToHistoryRow(CloneBatchInfoDto h) => new()
    {
        StatusIcon = h.IsUndone ? "↩" : "✅",
        Title = $"{h.SourceEmployeeName} → {FormatCloneTargets(h)}",
        Detail = string.Format(Loc["Vm.{0} · {1} day(s) · {2} entry(ies)"],
                     h.CreatedAtUtc.ToLocalTime().ToString("dd/MM/yyyy HH:mm"), h.DayCount, h.EntriesWritten)
                 + (h.IsUndone
                     ? " · " + string.Format(Loc["Vm.Undone on {0}"], h.UndoneAtUtc!.Value.ToLocalTime().ToString("dd/MM/yyyy HH:mm"))
                     : string.Empty)
    };

    [RelayCommand]
    private void CloseUndoCloneDialog() => IsUndoCloneDialogOpen = false;

    [RelayCommand]
    private async Task ConfirmUndoCloneAsync()
    {
        if (_lastClone == null || UndoCloneBlocked) return;

        try
        {
            var result = await _scheduleService.UndoCloneAsync(_lastClone.Id, _session?.Username);
            IsUndoCloneDialogOpen = false;

            StatusMessage = result.Status switch
            {
                CloneUndoStatus.Done => string.Format(
                    Loc["Vm.↩ Copy undone: {0} entry(ies) removed, {1} previous entry(ies) restored."],
                    result.RemovedEntries, result.RestoredEntries),
                CloneUndoStatus.BlockedSubmitted => string.Format(
                    Loc["Vm.⛔ Nothing was undone: {0} entry(ies) have already been submitted to Ergani."],
                    result.SubmittedEntries),
                CloneUndoStatus.NewerCloneExists => Loc["Vm.A newer copy exists. Reopen “Undo last clone” to see it."],
                _ => Loc["Vm.There is no clone to undo."]
            };

            await RefreshCloneStateAsync();
            await RefreshCalendarAsync();   // the employee on screen may be one of the destination employees
        }
        catch (Exception ex) { StatusMessage = $"❌ {ex.Message}"; }
    }

    // ── Export ────────────────────────────────────────────────────────────────

    [RelayCommand]
    private async Task ExportMonthAsync()
    {
        if (SelectedEmployee == null) { StatusMessage = Loc["Vm.Select an employee first."]; return; }
        try
        {
            var schedules = await _scheduleService
                .GetMonthAsync(SelectedEmployee.Id, Year, Month).ConfigureAwait(false);
            var folder   = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            var filePath = ExcelImportExportService.ExportMonthSchedule(
                SelectedEmployee.FullName, Year, Month, schedules, folder);
            StatusMessage = string.Format(Loc["Vm.✅ Exported: {0}"], Path.GetFileName(filePath));
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
            string.Format(Loc["Vm.The Ergani schedule file was downloaded:\n{0}\n\nDo you want to import its data into the schedules now?"], Path.GetFileName(filePath));
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
            StatusMessage = Loc["Vm.❌ The downloaded file could not be found."];
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
            StatusMessage = Loc["Vm.Reading Ergani Excel export…"];

            var result = await Task.Run(() =>
                ExcelImportExportService.ParseErganiScheduleExport(filePath));

            if (result.Rows.Count == 0)
            {
                StatusMessage = Loc["Vm.❌ No valid schedule rows were found in the Excel file."];
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
                string.Format(Loc["Vm.File: {0}\nExcel rows: {1}\nEmployees matched: {2}\nSchedule rows to import: {3}\nNormal working-time rows: {4}\nDays with overtime: {5} ({6} overtime record(s), imported separately from the work time)\nHoliday days (imported as holidays, not as absent): {7}\nEmployees/rows not found in the database: {8}\n\n"],
                    Path.GetFileName(filePath), result.TotalRows, employeeCount, _pendingErganiRows.Count, workDays, overtimeDays, overtimeRecords, leaveDays, unmatched.Count) +
                Loc["Vm.Imported schedule days, overtime and holidays will be marked ERGANI IMPORTED and already SUBMITTED, so they are not sent to Ergani again."];

            var warnings = new List<string>();
            warnings.AddRange(result.Warnings.Take(10));
            if (unmatched.Count > 0)
            {
                foreach (var row in unmatched.Take(10))
                    warnings.Add(string.Format(Loc["Vm.Row {0}: employee AFM {1} was not found."], row.RowNumber, row.TaxId));
            }

            ImportWarnings = warnings.Count == 0
                ? Loc["Vm.No warnings."]
                : string.Join("\n", warnings);

            if (_pendingErganiRows.Count == 0)
            {
                StatusMessage = Loc["Vm.❌ No rows could be matched to employees in the database."];
                return;
            }

            _importNotes =
                (unmatched.Count > 0 ? string.Format(Loc["Vm. ⚠ {0} row(s) skipped: employee AFM not found."], unmatched.Count) : string.Empty) +
                (result.Warnings.Count > 0 ? string.Format(Loc["Vm. ⚠ {0} warning(s) while reading the file."], result.Warnings.Count) : string.Empty);

            if (autoConfirm)
            {
                // Imported straight from the portal download: no second confirmation, the result message follows.
                await ConfirmImportAsync();
                return;
            }

            IsImportDialogOpen = true;
            StatusMessage = Loc["Vm.Review the import summary and confirm before writing to the database."];
        }
        catch (Exception ex)
        {
            StatusMessage = $"❌ {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task OpenImportHistoryAsync()
    {
        if (_session?.CompanyId is not int companyId) { StatusMessage = Loc["Vm.Select a company first."]; return; }
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
        StatusMessage = Loc["Vm.Ergani schedule import cancelled."];
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
            StatusMessage = Loc["Vm.Checking for existing data on the imported dates…"];
            var (keys, samples) = await FindConflictsAsync();
            _conflictKeys = keys;

            if (keys.Count == 0)
            {
                await RunImportAsync(replaceExisting: false);
                return;
            }

            var shown = samples.Take(8).Select(x => "• " + x);
            ConflictText =
                string.Format(Loc["Vm.{0} day(s) of the file already have different data in the database:\n"], keys.Count) +
                string.Join("\n", shown) +
                (samples.Count > 8 ? string.Format(Loc["Vm.\n… and {0} more"], samples.Count - 8) : string.Empty) +
                "\n\n" + Loc["Vm.Do you want to REPLACE the old data with the data from the file?"] + "\n" +
                Loc["Vm.Yes = replace the old data of those days."] + "\n" +
                Loc["Vm.No = keep the old data and skip those lines (the other lines are still imported)."];
            StatusMessage = Loc["Vm.Existing data found — choose whether to replace it."];
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
                return Tr.Loc["Vm.a work schedule exists"];
            if (existing != null && existing.Source != ScheduleSource.ErganiImported)
                return Tr.Loc["Vm.a schedule exists"];
            if (overtime.Count > 0)
                return Tr.Loc["Vm.overtime exists"];
            if (leaves.Any(l => !string.Equals(l.LeaveTypeCode, row.LeaveTypeCode, StringComparison.OrdinalIgnoreCase)))
                return Tr.Loc["Vm.a different holiday exists"];
            return null;
        }

        if (existing != null &&
            !(existing.WorkType == row.WorkType && existing.StartTime == row.StartTime && existing.EndTime == row.EndTime))
            return Tr.Loc["Vm.a different schedule exists"];

        var fileOvertime = row.OvertimeRanges.Select(r => (r.Start, r.End)).ToHashSet();
        var dbOvertime   = overtime.Select(o => (o.StartTime, o.EndTime)).ToHashSet();
        if (dbOvertime.Count > 0 && !fileOvertime.SetEquals(dbOvertime))
            return Tr.Loc["Vm.different overtime exists"];

        if (leaves.Count > 0)
            return Tr.Loc["Vm.a holiday exists"];

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
            StatusMessage = Loc["Vm.Importing Ergani declared schedule…"];

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
                    recorded = string.Format(Loc["Vm. 📝 Import recorded for {0}."], entry.PeriodText);
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
                extra.Add(string.Format(Loc["Vm.{0} overtime record(s) ({1} already existed)"], overtimeCreated, overtimeExisting));
            if (leavesCreated + leavesExisting > 0)
                extra.Add(string.Format(Loc["Vm.{0} holiday day(s) ({1} already existed)"], leavesCreated, leavesExisting));
            if (staleRemoved > 0)
                extra.Add(string.Format(Loc["Vm.{0} old imported \"absent\" day(s) replaced by holidays"], staleRemoved));
            if (replacedDays > 0)
                extra.Add(string.Format(Loc["Vm.{0} day(s) with old data replaced"], replacedDays));
            if (skippedExisting > 0)
                extra.Add(string.Format(Loc["Vm.{0} day(s) skipped because the database already had data (kept)"], skippedExisting));
            if (manualKept > 0)
                extra.Add(string.Format(Loc["Vm.{0} manual schedule day(s) on holidays left unchanged"], manualKept));

            StatusMessage =
                string.Format(Loc["Vm.✅ Ergani import completed: {0} schedule day(s) imported ({1} new, {2} updated)."], total, saved, overwritten) +
                (extra.Count > 0 ? " " + string.Join(", ", extra) + "." : string.Empty) +
                Loc["Vm. Imported rows are marked ERGANI IMPORTED and already SUBMITTED (they will not be sent again)."] + recorded + _importNotes;

            await RefreshCalendarAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = string.Format(Loc["Vm.❌ Ergani import failed: {0}"], ex.Message);
        }
    }

    private static string NormalizeTaxId(string value)
    {
        var digits = new string(value.Where(char.IsDigit).ToArray());
        return digits.Length > 0 && digits.Length < 9 ? digits.PadLeft(9, '0') : digits;
    }

    // ── Submit to Ergani ──────────────────────────────────────────────────────

    /// <summary>
    /// Prepares a company-wide submission for the currently selected month. Only saved,
    /// eligible schedule rows that have not already been submitted are included.
    /// </summary>
    [RelayCommand]
    private async Task PrepareSubmitAllPendingSchedulesAsync()
    {
        if (_session?.CompanyId is not int companyId)
        {
            StatusMessage = Loc["Vm.Select a company first."];
            return;
        }

        try
        {
            _bulkOvertimeMode = false;
            _bulkHolidayMode = false;
            IsBulkSubmitting = true;
            StatusMessage = Loc["Vm.Checking pending schedules…"];
            var targetYear = Year;
            var targetMonth = Month;
            var pending = new List<(EmployeeDto Employee, List<ScheduleDayDto> Days)>();

            foreach (var employee in AvailableEmployees)
            {
                var monthSchedules = await _scheduleService.GetMonthAsync(employee.Id, targetYear, targetMonth);
                var eligible = monthSchedules
                    .Where(day => !day.SubmittedToErgani &&
                                  (day.WorkType is AppWorkType.Office or AppWorkType.Home))
                    .OrderBy(day => day.ScheduleDate)
                    .ToList();
                if (eligible.Count > 0)
                    pending.Add((employee, eligible));
            }

            _pendingBulkSubmit = pending;
            _pendingBulkSubmitYear = targetYear;
            _pendingBulkSubmitMonth = targetMonth;
            var dayCount = pending.Sum(x => x.Days.Count);
            if (dayCount == 0)
            {
                IsBulkSubmitDialogOpen = false;
                StatusMessage = string.Format(Loc["Vm.No pending schedule changes found for {0}."],
                    new DateOnly(targetYear, targetMonth, 1).ToString("MMMM yyyy"));
                return;
            }

            BulkSubmitSummary = string.Format(
                Loc["Vm.This will submit {0} pending day(s) for {1} employee(s) in {2}."],
                dayCount, pending.Count, new DateOnly(targetYear, targetMonth, 1).ToString("MMMM yyyy"));
            IsBulkSubmitDialogOpen = true;
            StatusMessage = string.Empty;
        }
        catch (Exception ex)
        {
            StatusMessage = string.Format(Loc["Vm.Could not check pending schedules: {0}"], ex.Message);
        }
        finally
        {
            IsBulkSubmitting = false;
        }
    }

    [RelayCommand]
    private void CancelBulkSubmitSchedules()
    {
        IsBulkSubmitDialogOpen = false;
        _pendingBulkSubmit.Clear();
        _pendingOvertimeDays.Clear();
        _bulkOvertimeMode = false;
        _pendingLeaveIds.Clear();
        _bulkHolidayMode = false;
    }

    /// <summary>All unsent holidays of ALL employees for the shown month, sent together:
    /// one Ergani request per branch, so one protocol for all of them.</summary>
    [RelayCommand]
    private async Task PrepareSubmitAllHolidaysAsync()
    {
        if (_session?.CompanyId is not int companyId)
        {
            StatusMessage = Loc["Vm.Select a company first."];
            return;
        }

        try
        {
            _bulkOvertimeMode = false;
            IsBulkSubmitting = true;
            StatusMessage = Loc["Vm.Checking pending schedules…"];
            var first = new DateOnly(Year, Month, 1);
            var last = first.AddMonths(1).AddDays(-1);

            var ids = new List<int>();
            var employeeCount = 0;
            foreach (var employee in AvailableEmployees)
            {
                var leaves = await _leaveService.GetByEmployeeDateRangeAsync(companyId, employee.Id, first, last);
                var unsent = leaves.Where(l => !l.SubmittedToErgani).ToList();
                if (unsent.Count == 0) continue;
                ids.AddRange(unsent.Select(l => l.Id));
                employeeCount++;
            }

            if (ids.Count == 0)
            {
                IsBulkSubmitDialogOpen = false;
                StatusMessage = string.Format(Loc["Vm.No unsent holidays found for {0}."], first.ToString("MMMM yyyy"));
                return;
            }

            _pendingLeaveIds = ids;
            _bulkHolidayMode = true;
            BulkSubmitSummary = string.Format(
                Loc["Vm.This will send the holidays of {0} day(s) for {1} employee(s) in {2} in one request per branch, so you get one protocol."],
                ids.Count, employeeCount, first.ToString("MMMM yyyy"));
            IsBulkSubmitDialogOpen = true;
            StatusMessage = string.Empty;
        }
        catch (Exception ex)
        {
            StatusMessage = string.Format(Loc["Vm.Could not check pending schedules: {0}"], ex.Message);
        }
        finally
        {
            IsBulkSubmitting = false;
        }
    }

    private async Task ConfirmSubmitAllHolidaysAsync(int companyId)
    {
        var ids = _pendingLeaveIds.ToList();
        _pendingLeaveIds.Clear();
        _bulkHolidayMode = false;
        IsBulkSubmitDialogOpen = false;
        if (ids.Count == 0)
        {
            StatusMessage = Loc["Vm.Nothing to submit."];
            return;
        }

        IsBulkSubmitting = true;
        try
        {
            StatusMessage = string.Format(
                Loc["Vm.Sending {0} holiday day(s) to Ergani, all employees in one request per branch…"], ids.Count);
            var result = await _leaveSubmitter.SubmitAsync(companyId, ids);

            if (result.FailedCount == 0 && result.SubmittedCount > 0)
            {
                StatusMessage = string.Format(Loc["Vm.✅ Submitted {0} holiday day(s) to Ergani."], result.SubmittedCount)
                    + (string.IsNullOrEmpty(result.Protocol) ? string.Empty : " " + string.Format(Loc["Vm.Protocol: {0}."], result.Protocol));
            }
            else if (result.SubmittedCount == 0)
                StatusMessage = string.Format(Loc["Vm.❌ Not sent — {0}"], result.ErrorMessage);
            else
                StatusMessage = string.Format(Loc["Vm.❌ {0} day(s) sent, {1} NOT sent — {2}"],
                    result.SubmittedCount, result.FailedCount, result.ErrorMessage);

            await RefreshCalendarAsync();
            await RefreshCloneStateAsync();
        }
        catch (Exception ex) { StatusMessage = $"❌ {ex.Message}"; }
        finally { IsBulkSubmitting = false; }
    }

    /// <summary>Overtime (hours over 8) of ALL employees for the shown month, sent together:
    /// one Ergani request per branch, so one protocol for all of it.</summary>
    [RelayCommand]
    private async Task PrepareSubmitAllOvertimesAsync()
    {
        if (_session?.CompanyId is not int companyId)
        {
            StatusMessage = Loc["Vm.Select a company first."];
            return;
        }

        try
        {
            _bulkHolidayMode = false;
            IsBulkSubmitting = true;
            StatusMessage = Loc["Vm.Checking pending schedules…"];
            var year = Year;
            var month = Month;
            var first = new DateOnly(year, month, 1);
            var last = first.AddMonths(1).AddDays(-1);

            // Overtime that was already sent for the same hours is not sent again.
            var existing = await _overtimeService.GetByCompanyAsync(companyId, first, last);
            var alreadySent = existing
                .Where(o => o.SubmittedToErgani && !o.IsCancelled)
                .Select(o => (o.EmployeeId, o.OvertimeDate, o.StartTime, o.EndTime))
                .ToHashSet();

            var days = new List<ScheduleDayDto>();
            var employeeCount = 0;
            foreach (var employee in AvailableEmployees)
            {
                var employeeDays = await _scheduleService.GetMonthAsync(employee.Id, year, month);
                var withOvertime = employeeDays
                    .Where(d => d.WorkType is AppWorkType.Office or AppWorkType.Home)
                    .Where(d => ErganiManager.Core.Models.ScheduleHours.GetOvertimeRange(d.StartTime, d.EndTime) is { } r &&
                                !alreadySent.Contains((d.EmployeeId, d.ScheduleDate, r.From, r.To)))
                    .OrderBy(d => d.ScheduleDate)
                    .ToList();
                if (withOvertime.Count == 0) continue;
                days.AddRange(withOvertime);
                employeeCount++;
            }

            if (days.Count == 0)
            {
                IsBulkSubmitDialogOpen = false;
                StatusMessage = string.Format(Loc["Vm.No unsent overtime (over 8 hours) found for {0}."],
                    first.ToString("MMMM yyyy"));
                return;
            }

            _pendingOvertimeDays = days;
            _bulkOvertimeMode = true;
            BulkSubmitSummary = string.Format(
                Loc["Vm.This will send the overtime (over 8 hours) of {0} day(s) for {1} employee(s) in {2} in one request per branch, so you get one protocol."],
                days.Count, employeeCount, first.ToString("MMMM yyyy"));
            IsBulkSubmitDialogOpen = true;
            StatusMessage = string.Empty;
        }
        catch (Exception ex)
        {
            StatusMessage = string.Format(Loc["Vm.Could not check pending schedules: {0}"], ex.Message);
        }
        finally
        {
            IsBulkSubmitting = false;
        }
    }

    private async Task ConfirmSubmitAllOvertimesAsync(int companyId)
    {
        var days = _pendingOvertimeDays.ToList();
        _pendingOvertimeDays.Clear();
        _bulkOvertimeMode = false;
        IsBulkSubmitDialogOpen = false;
        if (days.Count == 0)
        {
            StatusMessage = Loc["Vm.Nothing to submit."];
            return;
        }

        IsBulkSubmitting = true;
        try
        {
            StatusMessage = string.Format(
                Loc["Vm.Sending the overtime of {0} day(s) to Ergani, all employees in one request per branch…"], days.Count);
            var result = await _scheduleSubmitter.SubmitOvertimeFromScheduleAsync(companyId, days, ErganiManager.Data.Entities.OvertimeJustification.ExceptionalWorkload.ToString());

            var protocol = result.Days.Select(d => d.OvertimeProtocol).FirstOrDefault(p => !string.IsNullOrEmpty(p));
            var message = string.Format(
                Loc["Vm.Overtime sent: {0} record(s) submitted, {1} failed."], result.OvertimeSubmitted, result.OvertimeFailed);
            if (protocol != null)
                message += " " + string.Format(Loc["Vm.Protocol: {0}."], protocol);
            if (result.OvertimeFailed > 0)
            {
                var bad = result.Days.FirstOrDefault(d => d.OvertimeFrom.HasValue && !d.OvertimeSubmitted);
                if (bad != null)
                    message += " " + string.Format(Loc["Vm.{0}: {1} (the unsent ones are saved on the Overtime page)."],
                        bad.Date.ToString("dd/MM"), bad.OvertimeError);
            }

            StatusMessage = (result.OvertimeFailed == 0 ? "✅ " : "⚠ ") + message;
            await RefreshCalendarAsync();
            await RefreshCloneStateAsync();
        }
        catch (Exception ex) { StatusMessage = $"❌ {ex.Message}"; }
        finally { IsBulkSubmitting = false; }
    }

    [RelayCommand]
    private async Task ConfirmSubmitAllPendingSchedulesAsync()
    {
        if (_session?.CompanyId is not int companyId || IsBulkSubmitting)
            return;

        if (_bulkOvertimeMode)
        {
            await ConfirmSubmitAllOvertimesAsync(companyId);
            return;
        }
        if (_bulkHolidayMode)
        {
            await ConfirmSubmitAllHolidaysAsync(companyId);
            return;
        }

        var batch = _pendingBulkSubmit.ToList();
        IsBulkSubmitDialogOpen = false;
        if (batch.Count == 0)
        {
            StatusMessage = Loc["Vm.Nothing to submit."];
            return;
        }

        IsBulkSubmitting = true;
        var submitted = 0;
        var failed = 0;
        var overtimeSubmitted = 0;
        var overtimeFailed = 0;
        var overtimePending = 0;
        var employeesWithFailures = 0;
        var failureDetails = new List<string>();

        try
        {
            // 1) Re-read every employee's month immediately before sending, so rows submitted or
            //    changed since the confirmation was opened are not blindly resent.
            var toSendByEmployee = new List<(EmployeeDto Employee, List<ScheduleDayDto> Days)>();
            for (var i = 0; i < batch.Count; i++)
            {
                var item = batch[i];
                StatusMessage = string.Format(
                    Loc["Vm.Preparing employee {0} of {1}: {2}…"],
                    i + 1, batch.Count, item.Employee.FullName);

                try
                {
                    var latest = await _scheduleService.GetMonthAsync(item.Employee.Id, _pendingBulkSubmitYear, _pendingBulkSubmitMonth);
                    var latestByDate = latest.ToDictionary(x => x.ScheduleDate);
                    var toSubmit = item.Days
                        .Where(old => latestByDate.TryGetValue(old.ScheduleDate, out var current) &&
                                      !current.SubmittedToErgani &&
                                      (current.WorkType is AppWorkType.Office or AppWorkType.Home))
                        .Select(old => latestByDate[old.ScheduleDate])
                        .OrderBy(day => day.ScheduleDate)
                        .ToList();

                    if (toSubmit.Count > 0)
                        toSendByEmployee.Add((item.Employee, toSubmit));
                }
                catch (Exception ex)
                {
                    failed += item.Days.Count;
                    employeesWithFailures++;
                    failureDetails.Add($"{item.Employee.FullName}: {ex.Message}");
                }
            }

            // 2) ONE call for all employees: the submitter sends a single Ergani file per branch and
            //    date that contains every employee of that date (instead of one file per employee).
            if (toSendByEmployee.Count > 0)
            {
                var allDays = toSendByEmployee.SelectMany(x => x.Days).ToList();
                StatusMessage = string.Format(
                    Loc["Vm.Sending {0} day(s) of {1} employee(s) to Ergani, all employees of a date in one file…"],
                    allDays.Count, toSendByEmployee.Count);

                try
                {
                    var result = await _scheduleSubmitter.SubmitScheduleDaysAsync(companyId, allDays, ErganiManager.Data.Entities.OvertimeJustification.ExceptionalWorkload.ToString());
                    submitted += result.Submitted;
                    failed += result.Error != null ? allDays.Count : result.Failed;
                    overtimeSubmitted += result.OvertimeSubmitted;
                    overtimeFailed += result.OvertimeFailed;
                    overtimePending += result.Days.Count(d => d.Success && d.OvertimeFrom.HasValue && !d.OvertimeSubmitted && string.IsNullOrWhiteSpace(d.OvertimeError));

                    foreach (var (employee, _) in toSendByEmployee)
                    {
                        var mine = result.Days.Where(d => d.EmployeeId == employee.Id).ToList();
                        var firstBad = mine.FirstOrDefault(d => !d.Success);
                        if (result.Error == null && firstBad == null) continue;

                        employeesWithFailures++;
                        var details = result.Error ?? firstBad?.Error ?? firstBad?.OvertimeError;
                        failureDetails.Add($"{employee.FullName}: {details ?? Loc["Vm.Some schedule entries failed."]}");
                    }
                }
                catch (Exception ex)
                {
                    failed += allDays.Count;
                    foreach (var (employee, _) in toSendByEmployee)
                    {
                        employeesWithFailures++;
                        failureDetails.Add($"{employee.FullName}: {ex.Message}");
                    }
                }
            }

            var resultMessage = string.Format(
                Loc["Vm.Bulk submission finished: {0} submitted, {1} failed across {2} employee(s)."],
                submitted, failed, batch.Count);
            if (overtimeSubmitted > 0 || overtimeFailed > 0)
                resultMessage += " " + string.Format(
                    Loc["Vm.Overtime records: {0} submitted, {1} failed."], overtimeSubmitted, overtimeFailed);
            if (overtimePending > 0)
                resultMessage += " " + string.Format(
                    Loc["Vm.Overtime saved as pending: {0} record(s). Submit them from the Overtime page."], overtimePending);
            if (employeesWithFailures > 0)
            {
                var detail = string.Join(" | ", failureDetails.Take(5));
                if (failureDetails.Count > 5)
                    detail += " | " + string.Format(Loc["Vm.And {0} more employee error(s)."], failureDetails.Count - 5);
                resultMessage += " " + string.Format(Loc["Vm.Errors: {0}"], detail);
            }

            StatusMessage = (failed == 0 && overtimeFailed == 0 ? "✅ " : "⚠ ") + resultMessage;
            _pendingBulkSubmit.Clear();
            await RefreshCalendarAsync();
            await RefreshCloneStateAsync();
        }
        finally
        {
            IsBulkSubmitting = false;
        }
    }

    /// <summary>Submits the selected days when any are selected; with no selection it submits
    /// the whole displayed month (the previous behaviour).</summary>
    [RelayCommand]
    private async Task SubmitScheduleToErganiAsync()
    {
        if (SelectedEmployee == null || _session?.CompanyId is not int companyId)
        { StatusMessage = Loc["Vm.Select an employee first."]; return; }
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
        if (alreadySent > 0) notes.Add(string.Format(Loc["Vm.{0} already submitted"], alreadySent));
        if (restAbsent  > 0) notes.Add(string.Format(Loc["Vm.{0} rest/absent (not sent to Ergani)"], restAbsent));
        if (noSchedule  > 0) notes.Add(string.Format(Loc["Vm.{0} without a schedule"], noSchedule));
        var skipped = notes.Count > 0 ? string.Format(Loc["Vm. Skipped: {0}."], string.Join(", ", notes)) : string.Empty;

        if (toSubmit.Count == 0)
        { StatusMessage = Loc["Vm.Nothing to submit."] + skipped; return; }

        StatusMessage = string.Format(Loc["Vm.Submitting {0} day(s) to Ergani…"], toSubmit.Count);

        var result = await _scheduleSubmitter.SubmitScheduleDaysAsync(companyId, toSubmit, ErganiManager.Data.Entities.OvertimeJustification.ExceptionalWorkload.ToString());

        // Schedule-generated overtime remains pending for review/submission from the Overtime page.
        var overtime = string.Empty;
        var pendingOvertime = result.Days.Count(d => d.Success && d.OvertimeFrom.HasValue && !d.OvertimeSubmitted && string.IsNullOrWhiteSpace(d.OvertimeError));
        if (pendingOvertime > 0)
            overtime = " " + string.Format(
                Loc["Vm.Overtime saved as pending: {0} record(s). Submit them from the Overtime page."], pendingOvertime);
        if (result.OvertimeSubmitted > 0 || result.OvertimeFailed > 0)
        {
            overtime += " " + string.Format(Loc["Vm. Overtime (hours over 8): {0} sent"], result.OvertimeSubmitted);
            if (result.OvertimeFailed > 0)
            {
                var bad = result.Days.First(d => d.OvertimeFrom.HasValue && !d.OvertimeSubmitted && !string.IsNullOrWhiteSpace(d.OvertimeError));
                overtime += string.Format(Loc["Vm., {0} NOT sent — {1}: {2} (saved on the Overtime page)"], result.OvertimeFailed, bad.Date.ToString("dd/MM"), bad.OvertimeError);
            }
            overtime += ".";
        }

        if (result.Error != null)
            StatusMessage = $"❌ {result.Error}";
        else if (result.Failed == 0)
            StatusMessage = (result.OvertimeFailed == 0 ? "✅" : "⚠") + " " + string.Format(Loc["Vm.Submitted {0} day(s) to Ergani."], result.Submitted) + overtime + skipped;
        else
        {
            var first = result.Days.First(d => !d.Success);
            StatusMessage = string.Format(Loc["Vm.❌ {0} submitted, {1} failed — {2}: {3}. Open 📜 Log for the details."], result.Submitted, result.Failed, first.Date.ToString("dd/MM"), first.Error) + overtime + skipped;
        }

        await RefreshCalendarAsync();
        await RefreshCloneStateAsync();
    }

    // ── Submission log: Ergani responses + PDF ────────────────────────────────

    /// <summary>Opens the holidays form. If the selected calendar days form one continuous block,
    /// the form starts with those dates.</summary>
    [RelayCommand]
    private async Task OpenHolidaysAsync()
    {
        if (SelectedEmployee == null || _session?.CompanyId is not int companyId)
        { StatusMessage = Loc["Vm.Select an employee first."]; return; }
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

    private static List<int> ParseEmployeeIds(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new List<int>();
        try
        {
            return JsonSerializer.Deserialize<List<int>>(json)?.Distinct().ToList() ?? new List<int>();
        }
        catch
        {
            return new List<int>();
        }
    }

    [RelayCommand]
    private async Task OpenLogAsync()
    {
        if (SelectedEmployee == null || _session?.CompanyId is not int companyId)
        { StatusMessage = Loc["Vm.Select an employee first."]; return; }
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
                    ResponseRawJson = e.ResponseRawJson,
                    SubmissionType = "DailySchedule"
                });

            // Include company-wide DailySchedule API batches that contain the selected
            // employee. These are not represented by the per-employee schedule history.
            await using (var db = new AppDbContext(_connectionState.GetDbOptions()))
            {
                var batchLogs = await db.ApiSubmissionLogs
                    .Where(l => l.CompanyId == companyId && l.SubmissionType == "DailySchedule")
                    .OrderByDescending(l => l.SubmissionDate)
                    .ThenByDescending(l => l.Id)
                    .Take(500)
                    .ToListAsync();

                var employeeById = AvailableEmployees.ToDictionary(e => e.Id);
                foreach (var log in batchLogs)
                {
                    var ids = ParseEmployeeIds(log.EmployeeIdsJson);
                    if (ids.Count == 0 && !string.IsNullOrWhiteSpace(log.RequestPayloadJson))
                    {
                        ids = AvailableEmployees
                            .Where(e => !string.IsNullOrWhiteSpace(e.TaxId) &&
                                log.RequestPayloadJson.Contains(e.TaxId, StringComparison.Ordinal))
                            .Select(e => e.Id)
                            .Distinct()
                            .ToList();
                    }

                    // This popup is already scoped to one employee. Show only multi-employee
                    // submissions that included that employee, not ordinary single submissions.
                    if (ids.Count < 2 || !ids.Contains(SelectedEmployee.Id))
                        continue;

                    var names = ids
                        .Where(employeeById.ContainsKey)
                        .Select(id => employeeById[id].FullName)
                        .Distinct()
                        .ToList();

                    LogRows.Add(new ScheduleLogRow
                    {
                        Id = -log.Id,
                        SubmissionDate = log.SubmissionDate,
                        Success = log.Success,
                        Protocol = log.Protocol,
                        ErrorMessage = log.ErrorMessage,
                        HttpStatusCode = log.HttpStatusCode,
                        ResponseRawJson = log.ResponseRawJson,
                        EmployeeNames = string.Join(", ", names),
                        IsBatch = true,
                        SubmissionType = "DailySchedule"
                    });
                }
            }

            // Merge per-employee history and batch history into one newest-first list.
            var orderedRows = LogRows.OrderByDescending(r => r.SubmissionDate)
                .ThenByDescending(r => r.Id).ToList();
            LogRows.Clear();
            foreach (var row in orderedRows)
                LogRows.Add(row);

            HasLogRows = LogRows.Count > 0;
            LogTitle   = string.Format(Loc["Vm.📜 Schedule log — {0}"], SelectedEmployee.FullName);
            LogEmptyText = Loc["Vm.No schedule submission history for this employee."];
            LogStatus  = HasLogRows ? string.Format(Loc["Vm.{0} submission(s), newest first."], LogRows.Count) : string.Empty;
            IsLogOpen  = true;
        }
        catch (Exception ex) { StatusMessage = $"❌ {ex.Message}"; }
    }

    [RelayCommand]
    private async Task OpenOvertimeLogAsync() => await OpenApiSubmissionLogAsync("Overtime", Loc[L.OvertimeLog]);

    [RelayCommand]
    private async Task OpenHolidaysLogAsync() => await OpenApiSubmissionLogAsync("Leave", Loc[L.HolidaysLog]);

    private async Task OpenApiSubmissionLogAsync(string submissionType, string displayName)
    {
        if (SelectedEmployee == null || _session?.CompanyId is not int companyId)
        { StatusMessage = Loc["Vm.Select an employee first."]; return; }

        try
        {
            await using var db = new AppDbContext(_connectionState.GetDbOptions());
            var entries = await db.ApiSubmissionLogs
                .Where(l => l.CompanyId == companyId && l.SubmissionType == submissionType)
                .OrderByDescending(l => l.SubmissionDate)
                .ThenByDescending(l => l.Id)
                .Take(1000)
                .ToListAsync();

            var employeeById = AvailableEmployees.ToDictionary(e => e.Id);
            LogRows.Clear();
            foreach (var entry in entries)
            {
                var ids = ParseEmployeeIds(entry.EmployeeIdsJson);
                if (ids.Count == 0 && entry.EmployeeId.HasValue)
                    ids.Add(entry.EmployeeId.Value);
                if (ids.Count == 0 && !string.IsNullOrWhiteSpace(entry.RequestPayloadJson))
                {
                    ids = AvailableEmployees
                        .Where(e => !string.IsNullOrWhiteSpace(e.TaxId) &&
                            entry.RequestPayloadJson.Contains(e.TaxId, StringComparison.Ordinal))
                        .Select(e => e.Id).Distinct().ToList();
                }

                if (!ids.Contains(SelectedEmployee.Id) && entry.EmployeeId != SelectedEmployee.Id)
                    continue;

                var names = ids.Where(employeeById.ContainsKey)
                    .Select(id => employeeById[id].FullName).Distinct().ToList();
                if (names.Count == 0 && entry.EmployeeId == SelectedEmployee.Id)
                    names.Add(SelectedEmployee.FullName);

                LogRows.Add(new ScheduleLogRow
                {
                    Id = -entry.Id,
                    ScheduleDate = entry.ScheduleDate,
                    SubmissionDate = entry.SubmissionDate,
                    Success = entry.Success,
                    Protocol = entry.Protocol,
                    ErrorMessage = entry.ErrorMessage,
                    HttpStatusCode = entry.HttpStatusCode,
                    ResponseRawJson = entry.ResponseRawJson,
                    EmployeeNames = string.Join(", ", names),
                    IsBatch = ids.Distinct().Count() > 1,
                    SubmissionType = submissionType
                });
            }

            HasLogRows = LogRows.Count > 0;
            LogTitle = $"{displayName} — {SelectedEmployee.FullName}";
            LogEmptyText = Loc["Vm.No matching submission history for this employee."];
            LogStatus = HasLogRows
                ? string.Format(Loc["Vm.{0} submission(s), newest first."], LogRows.Count)
                : string.Empty;
            IsLogOpen = true;
        }
        catch (Exception ex) { StatusMessage = $"❌ {ex.Message}"; }
    }

    [RelayCommand]
    private void CloseLog() => IsLogOpen = false;

    [RelayCommand]
    private void ShowResponse(ScheduleLogRow row)
    {
        // A failure with no HTTP response (e.g. no connection) has only the error text.
        var text = row.IsPending
            ? Tr.Loc["Vm.⏳ Pending — generated from schedule; submit from Overtime page"]
            : !string.IsNullOrWhiteSpace(row.ResponseRawJson) ? row.ResponseRawJson : row.ErrorMessage;
        if (string.IsNullOrWhiteSpace(text))
        { LogStatus = Loc["Vm.No stored Ergani response for this entry."]; return; }

        ResponseTitle = string.Format(Loc["Vm.Ergani response — {0} ({1})"], row.ScheduleDateText, row.SubmittedAtText);
        ResponseText  = text;
        IsResponseOpen = true;
    }

    [RelayCommand]
    private void CloseResponse() => IsResponseOpen = false;

    [RelayCommand]
    private async Task DownloadPdfAsync(ScheduleLogRow row)
    {
        if (_session?.CompanyId is not int companyId || !row.HasProtocol)
        { LogStatus = Loc["Vm.A protocol is required before downloading the PDF."]; return; }
        try
        {
            var submittedDate = DateOnly.FromDateTime(row.SubmissionDate.ToLocalTime());
            var bytes = await _documentService.DownloadPdfAsync(
                companyId, row.DocumentCode, row.Protocol!, submittedDate);

            var folder   = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            // Keep batch filenames short even when many employees were included.
            var who      = row.IsBatch ? "batch" : SelectedEmployee?.FullName ?? "employee";
            var safeName = string.Join("_", who.Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries));
            var day      = (row.ScheduleDate ?? DateOnly.FromDateTime(row.SubmissionDate.ToLocalTime())).ToString("yyyyMMdd");
            var kind = row.SubmissionType switch
            {
                "Overtime" => "Overtime",
                "Leave" => "Holiday",
                _ => "Schedule"
            };
            var path     = Path.Combine(folder, $"Ergani_{kind}_{safeName}_{day}_{row.Protocol}.pdf");

            await File.WriteAllBytesAsync(path, bytes);
            LogStatus = string.Format(Loc["Vm.✅ PDF saved to Desktop: {0}"], Path.GetFileName(path));
        }
        catch (Exception ex) { LogStatus = string.Format(Loc["Vm.❌ PDF download failed: {0}"], ex.Message); }
    }

    [RelayCommand]
    private async Task SubmitOvertimesAsync()
    {
        if (SelectedEmployee == null || _session?.CompanyId is not int companyId)
        { StatusMessage = Loc["Vm.Select an employee first."]; return; }
        try
        {
            var picked = _selectedDates.ToHashSet();
            var days   = await LoadScopeDaysAsync(SelectedEmployee.Id, picked);

            StatusMessage = Loc["Vm.Detecting overtime and submitting…"];
            var result = await _scheduleSubmitter.SubmitOvertimeFromScheduleAsync(companyId, days, ErganiManager.Data.Entities.OvertimeJustification.ExceptionalWorkload.ToString());

            if (result.Days.Count == 0)
            {
                StatusMessage = picked.Count > 0
                    ? Loc["Vm.No overtime (over 8 hours) on the selected day(s)."]
                    : Loc["Vm.No overtime days found this month (over 8 hours)."];
                return;
            }

            if (result.OvertimeFailed == 0)
                StatusMessage = string.Format(Loc["Vm.✅ Submitted {0} overtime record(s) to Ergani."], result.OvertimeSubmitted);
            else
            {
                var bad = result.Days.First(d => !d.OvertimeSubmitted);
                StatusMessage = string.Format(Loc["Vm.❌ {0} overtime sent, {1} NOT sent — {2}: {3} (the unsent ones are saved on the Overtime page)."], result.OvertimeSubmitted, result.OvertimeFailed, bad.Date.ToString("dd/MM"), bad.OvertimeError);
            }

            await RefreshCalendarAsync();
            await RefreshCloneStateAsync();
        }
        catch (Exception ex) { StatusMessage = $"❌ {ex.Message}"; }
    }

    /// <summary>Opens the Ergani web portal in the browser, logged in with the company's saved credentials.</summary>
    [RelayCommand]
    private async Task OpenErganiPortalAsync()
    {
        if (_session?.CompanyId is not int companyId) { StatusMessage = Loc["Vm.Select a company first."]; return; }
        StatusMessage = Loc["Vm.Opening the Ergani portal…"];
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
        { StatusMessage = Loc["Vm.Select an employee first."]; return; }
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
                    ? Loc["Vm.All holidays on the selected day(s) are already submitted."]
                    : picked.Count > 0
                        ? Loc["Vm.No saved holidays on the selected day(s). Use 🏖 Holidays to add them."]
                        : Loc["Vm.No saved holidays this month. Use 🏖 Holidays to add them."];
                return;
            }

            StatusMessage = string.Format(Loc["Vm.Submitting {0} holiday day(s) to Ergani…"], unsent.Count);
            var result = await _leaveSubmitter.SubmitAsync(companyId, unsent.Select(l => l.Id).ToList());

            if (result.FailedCount == 0 && result.SubmittedCount > 0)
                StatusMessage = string.Format(Loc["Vm.✅ Submitted {0} holiday day(s) to Ergani."], result.SubmittedCount);
            else if (result.SubmittedCount == 0)
                StatusMessage = string.Format(Loc["Vm.❌ Not sent — {0}"], result.ErrorMessage);
            else
                StatusMessage = string.Format(Loc["Vm.❌ {0} day(s) sent, {1} NOT sent — {2}"], result.SubmittedCount, result.FailedCount, result.ErrorMessage);

            await RefreshCalendarAsync();
            await RefreshCloneStateAsync();
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

    /// <summary>Raised when the checkbox is ticked or unticked (the dialog refreshes its warning).</summary>
    public event EventHandler? SelectionChanged;
    partial void OnIsSelectedChanged(bool value) => SelectionChanged?.Invoke(this, EventArgs.Empty);
}

/// <summary>One line of the clone history shown in the "Undo last clone" dialog.</summary>
public class CloneHistoryRow
{
    public string StatusIcon { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string Detail { get; init; } = string.Empty;
}
