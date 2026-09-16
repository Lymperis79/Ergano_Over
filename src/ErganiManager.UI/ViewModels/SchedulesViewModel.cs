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
    [ObservableProperty] private bool _hasSchedule;
    [ObservableProperty] private bool _isToday;
    [ObservableProperty] private bool _isSelected;

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

public partial class SchedulesViewModel : ViewModelBase, IAdminSectionViewModel
{
    private readonly IScheduleService   _scheduleService;
    private readonly IEmployeeService   _employeeService;
    private readonly IBranchService     _branchService;
    private readonly IScheduleSubmitter _scheduleSubmitter;
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

    [ObservableProperty] private bool _hasActiveCompany;
    [ObservableProperty] private string _noCompanyMessage = string.Empty;
    [ObservableProperty] private string _statusMessage = string.Empty;

    // ── Multi-day selection ───────────────────────────────────────────────────
    private readonly HashSet<DateOnly> _selectedDates = new();
    public int SelectedDayCount => _selectedDates.Count;
    public bool HasSelection => _selectedDates.Count > 0;
    public string SelectionLabel => _selectedDates.Count == 0
        ? "No days selected"
        : $"{_selectedDates.Count} day(s) selected";

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

    public SchedulesViewModel(IScheduleService scheduleService,
        IEmployeeService employeeService, IBranchService branchService,
        IScheduleSubmitter scheduleSubmitter)
    {
        _scheduleService   = scheduleService;
        _employeeService   = employeeService;
        _branchService     = branchService;
        _scheduleSubmitter = scheduleSubmitter;
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

    private async Task BuildMonthCalendarAsync()
    {
        var schedules = await _scheduleService
            .GetMonthAsync(SelectedEmployee!.Id, Year, Month).ConfigureAwait(false);
        var byDate = schedules.ToDictionary(s => s.ScheduleDate);
        var today  = DateOnly.FromDateTime(DateTime.Today);
        var first  = new DateOnly(Year, Month, 1);
        var days   = DateTime.DaysInMonth(Year, Month);
        int startDow = ((int)first.DayOfWeek + 6) % 7; // Mon=0

        var cells = new List<CalendarCellViewModel>();
        for (int i = 0; i < startDow; i++)
            cells.Add(new CalendarCellViewModel { IsInMonth = false });

        for (int d = 1; d <= days; d++)
        {
            var date = new DateOnly(Year, Month, d);
            byDate.TryGetValue(date, out var sched);
            cells.Add(new CalendarCellViewModel
            {
                IsInMonth     = true,
                Date          = date,
                HasSchedule   = sched != null,
                WorkType      = sched != null ? (AppWorkType?)sched.WorkType : null,
                TimeRangeText = sched is { StartTime: not null, EndTime: not null }
                    ? $"{sched.StartTime:HH:mm}–{sched.EndTime:HH:mm}" : "",
                IsToday       = date == today
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
        var today  = DateOnly.FromDateTime(DateTime.Today);
        var cells  = new List<CalendarCellViewModel>();

        for (var d = from; d <= to; d = d.AddDays(1))
        {
            byDate.TryGetValue(d, out var sched);
            cells.Add(new CalendarCellViewModel
            {
                IsInMonth     = true,
                Date          = d,
                HasSchedule   = sched != null,
                WorkType      = sched != null ? (AppWorkType?)sched.WorkType : null,
                TimeRangeText = sched is { StartTime: not null, EndTime: not null }
                    ? $"{sched.StartTime:HH:mm}–{sched.EndTime:HH:mm}" : "",
                IsToday       = d == today
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
            OpenDayDialog(cell);
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
    }

    [RelayCommand]
    private void ClearSelection()
    {
        _selectedDates.Clear();
        foreach (var c in Cells) c.IsSelected = false;
        NotifySelectionChanged();
    }

    // ── Single day dialog ─────────────────────────────────────────────────────

    private void OpenDayDialog(CalendarCellViewModel cell)
    {
        if (cell.Date == null) return;
        EditingDate      = cell.Date.Value;
        EditingScheduleId = 0;
        EditingWorkType  = cell.WorkType ?? AppWorkType.Office;
        EditingStartTime = new TimeSpan(9, 0, 0);
        EditingEndTime   = new TimeSpan(17, 0, 0);
        EditingComments  = string.Empty;
        EditingActualText     = string.Empty;
        EditingSubmissionText = string.Empty;
        EditingBranch = AvailableBranches.FirstOrDefault();
        IsDayDialogOpen = true;
    }

    [RelayCommand]
    private void CloseDayDialog() => IsDayDialogOpen = false;

    partial void OnEditingWorkTypeChanged(AppWorkType value) =>
        OnPropertyChanged(nameof(ShowTimeFields));

    [RelayCommand]
    private async Task SaveDayAsync()
    {
        if (SelectedEmployee == null || EditingBranch == null) return;
        try
        {
            await _scheduleService.UpsertDayAsync(new ScheduleDayDto
            {
                Id           = EditingScheduleId,
                EmployeeId   = SelectedEmployee.Id,
                BranchId     = EditingBranch.Id,
                ScheduleDate = EditingDate,
                WorkType     = EditingWorkType,
                StartTime    = EditingStartTime.HasValue ? TimeOnly.FromTimeSpan(EditingStartTime.Value) : null,
                EndTime      = EditingEndTime.HasValue   ? TimeOnly.FromTimeSpan(EditingEndTime.Value)   : null,
                Comments     = EditingComments
            });
            IsDayDialogOpen = false;
            await RefreshCalendarAsync();
        }
        catch (Exception ex) { StatusMessage = $"❌ {ex.Message}"; }
    }

    [RelayCommand]
    private async Task DeleteDayAsync()
    {
        if (EditingScheduleId == 0) return;
        await _scheduleService.DeleteDayAsync(EditingScheduleId);
        IsDayDialogOpen = false;
        await RefreshCalendarAsync();
    }

    // ── Bulk apply (multi-selected days or week) ──────────────────────────────

    [RelayCommand]
    private void OpenBulkDialog()
    {
        if (SelectedEmployee == null) return;
        // Pre-fill from / to based on current view and selection
        IsBulkDialogOpen = true;
        BulkWorkType  = AppWorkType.Office;
        BulkStartTime = new TimeSpan(9, 0, 0);
        BulkEndTime   = new TimeSpan(17, 0, 0);
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

    // ── Import ────────────────────────────────────────────────────────────────

    public event EventHandler? ImportRequested;

    [RelayCommand]
    private void RequestImport() => ImportRequested?.Invoke(this, EventArgs.Empty);

    public async Task ImportFromFileAsync(string filePath)
    {
        if (SelectedEmployee == null || EditingBranch == null)
        { StatusMessage = "Select an employee and branch first."; return; }
        try
        {
            StatusMessage = "Parsing file…";
            var rows = ExcelImportExportService.ParseScheduleImportFile(filePath);
            if (rows.Count == 0) { StatusMessage = "No valid rows found."; return; }
            int saved = 0;
            foreach (var row in rows)
            {
                await _scheduleService.UpsertDayAsync(new ScheduleDayDto
                {
                    EmployeeId   = SelectedEmployee.Id,
                    BranchId     = EditingBranch.Id,
                    ScheduleDate = row.Date,
                    WorkType     = row.WorkType,
                    StartTime    = row.StartTime,
                    EndTime      = row.EndTime,
                    Comments     = row.Comments
                }).ConfigureAwait(false);
                saved++;
            }
            StatusMessage = $"✅ Imported {saved} day(s).";
            await RefreshCalendarAsync();
        }
        catch (Exception ex) { StatusMessage = $"❌ {ex.Message}"; }
    }

    // ── Submit to Ergani ──────────────────────────────────────────────────────

    [RelayCommand]
    private async Task SubmitScheduleToErganiAsync()
    {
        if (SelectedEmployee == null || _session?.CompanyId is not int companyId)
        { StatusMessage = "Select an employee first."; return; }
        try
        {
            StatusMessage = "Submitting schedule to Ergani…";
            var schedules = await _scheduleService
                .GetMonthAsync(SelectedEmployee.Id, Year, Month).ConfigureAwait(false);
            var toSubmit = schedules
                .Where(s => !s.SubmittedToErgani
                         && s.WorkType != AppWorkType.Rest
                         && s.WorkType != AppWorkType.Absent)
                .ToList();
            if (toSubmit.Count == 0)
            { StatusMessage = "All schedules already submitted for this month."; return; }

            var (count, error) = await _scheduleSubmitter
                .SubmitScheduleDaysAsync(companyId, toSubmit).ConfigureAwait(false);
            StatusMessage = error != null
                ? $"❌ {error} ({count} submitted before error)"
                : $"✅ Submitted {count} schedule entry(ies) to Ergani.";
            await RefreshCalendarAsync();
        }
        catch (Exception ex) { StatusMessage = $"❌ {ex.Message}"; }
    }

    [RelayCommand]
    private async Task SubmitOvertimesAsync()
    {
        if (SelectedEmployee == null || _session?.CompanyId is not int companyId)
        { StatusMessage = "Select an employee first."; return; }
        try
        {
            StatusMessage = "Detecting overtime and submitting…";
            var schedules = await _scheduleService
                .GetMonthAsync(SelectedEmployee.Id, Year, Month).ConfigureAwait(false);
            var overtimeDays = schedules
                .Where(s => s.ActualArrival.HasValue && s.ActualDeparture.HasValue
                    && (s.ActualDeparture.Value - s.ActualArrival.Value).TotalHours > 8)
                .ToList();
            if (overtimeDays.Count == 0)
            { StatusMessage = "No overtime days detected this month (work > 8h)."; return; }

            var (count, error) = await _scheduleSubmitter
                .SubmitOvertimeDaysAsync(companyId, overtimeDays).ConfigureAwait(false);
            StatusMessage = error != null
                ? $"❌ {error} ({count} submitted before error)"
                : $"✅ Submitted {count} overtime record(s) to Ergani.";
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
