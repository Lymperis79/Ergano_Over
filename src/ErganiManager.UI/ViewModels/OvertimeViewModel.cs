using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ErganiManager.Core.Interfaces;
using ErganiManager.Core.Models;

namespace ErganiManager.UI.ViewModels;

public partial class OvertimeViewModel : ViewModelBase, IAdminSectionViewModel
{
    private readonly IOvertimeService _overtimeService;
    private readonly IEmployeeService _employeeService;
    private readonly IBranchService _branchService;
    private UserSession? _session;

    public ObservableCollection<OvertimeDto> Records { get; } = new();
    public ObservableCollection<EmployeeDto> AvailableEmployees { get; } = new();
    public ObservableCollection<BranchDto> AvailableBranches { get; } = new();
    public ObservableCollection<AppOvertimeJustification> Justifications { get; } =
        new(Enum.GetValues<AppOvertimeJustification>());

    [ObservableProperty] private bool _hasActiveCompany;
    [ObservableProperty] private string _noCompanyMessage = string.Empty;
    [ObservableProperty] private string _statusMessage = string.Empty;
    [ObservableProperty] private bool _isCreating;

    [ObservableProperty] private DateTimeOffset? _filterFrom = DateTimeOffset.Now.AddMonths(-1);
    [ObservableProperty] private DateTimeOffset? _filterTo = DateTimeOffset.Now;

    [ObservableProperty] private EmployeeDto? _formEmployee;
    [ObservableProperty] private BranchDto? _formBranch;
    [ObservableProperty] private DateTimeOffset _formDate = DateTimeOffset.Now;
    [ObservableProperty] private TimeSpan? _formStartTime = new TimeSpan(17, 0, 0);
    [ObservableProperty] private TimeSpan? _formEndTime = new TimeSpan(20, 0, 0);
    [ObservableProperty] private AppOvertimeJustification _formJustification = AppOvertimeJustification.ExceptionalWorkload;
    [ObservableProperty] private int _formWeeklyWorkdays = 5;
    [ObservableProperty] private string _formAseeApproval = string.Empty;

    public OvertimeViewModel(
        IOvertimeService overtimeService,
        IEmployeeService employeeService,
        IBranchService branchService)
    {
        _overtimeService = overtimeService;
        _employeeService = employeeService;
        _branchService = branchService;
    }

    public void Initialize(UserSession session)
    {
        _session = session;
        HasActiveCompany = session.CompanyId.HasValue;
        NoCompanyMessage = session.CompanyId.HasValue
            ? string.Empty
            : "Select a company first.";

        if (HasActiveCompany)
            _ = LoadDataAsync();
    }

    private async Task LoadDataAsync()
    {
        if (_session?.CompanyId is not int companyId) return;

        var employees = await _employeeService.GetByCompanyAsync(companyId, activeOnly: true).ConfigureAwait(false);
        await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
        {
            AvailableEmployees.Clear();
            foreach (var e in employees) AvailableEmployees.Add(e);
        });

        var branches = await _branchService.GetByCompanyAsync(companyId).ConfigureAwait(false);
        await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
        {
            AvailableBranches.Clear();
            foreach (var b in branches) AvailableBranches.Add(b);
        });

        FormEmployee = AvailableEmployees.FirstOrDefault();
        FormBranch = AvailableBranches.FirstOrDefault();

        await LoadRecordsAsync();
    }

    [RelayCommand]
    private async Task LoadRecordsAsync()
    {
        if (_session?.CompanyId is not int companyId) return;

        var list = await _overtimeService.GetByCompanyAsync(
            companyId,
            DateOnly.FromDateTime((FilterFrom ?? DateTimeOffset.Now.AddMonths(-1)).LocalDateTime),
            DateOnly.FromDateTime((FilterTo ?? DateTimeOffset.Now).LocalDateTime));

        await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
        {
            Records.Clear();
            foreach (var r in list) Records.Add(r);
        });
        StatusMessage = $"{Records.Count} record(s).";
    }

    [RelayCommand]
    private void StartCreate()
    {
        FormEmployee      = AvailableEmployees.FirstOrDefault();
        FormBranch        = AvailableBranches.FirstOrDefault();
        FormDate          = DateTimeOffset.Now;
        FormStartTime     = new TimeSpan(17, 0, 0);
        FormEndTime       = new TimeSpan(20, 0, 0);
        FormJustification = AppOvertimeJustification.ExceptionalWorkload;
        FormWeeklyWorkdays = 5;
        FormAseeApproval  = string.Empty;
        StatusMessage     = string.Empty;
        IsCreating        = true;
    }

    [RelayCommand]
    private void CancelCreate() => IsCreating = false;

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (_session?.CompanyId is not int companyId) return;

        if (FormEmployee == null || FormBranch == null)
        { StatusMessage = "Employee and branch are required."; return; }

        if (!FormStartTime.HasValue || !FormEndTime.HasValue)
        { StatusMessage = "Start and end time are required."; return; }

        try
        {
            await _overtimeService.CreateAsync(new OvertimeDto
            {
                EmployeeId           = FormEmployee.Id,
                BranchId             = FormBranch.Id,
                OvertimeDate         = DateOnly.FromDateTime(FormDate.LocalDateTime),
                StartTime            = TimeOnly.FromTimeSpan(FormStartTime.Value),
                EndTime              = TimeOnly.FromTimeSpan(FormEndTime.Value),
                Justification        = FormJustification,
                WeeklyWorkdaysNumber = FormWeeklyWorkdays,
                AseeApproval         = string.IsNullOrWhiteSpace(FormAseeApproval) ? null : FormAseeApproval
            }, companyId);

            IsCreating = false;
            await LoadRecordsAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = $"❌ {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task CancelOvertimeAsync(OvertimeDto record)
    {
        try { await _overtimeService.CancelAsync(record.Id); await LoadRecordsAsync(); }
        catch (Exception ex) { StatusMessage = $"❌ {ex.Message}"; }
    }

    [RelayCommand]
    private async Task DeleteAsync(OvertimeDto record)
    {
        await _overtimeService.DeleteAsync(record.Id);
        await LoadRecordsAsync();
    }
}
