using System;
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

public partial class WorkCardHistoryViewModel : ViewModelBase, IAdminSectionViewModel
{
    private readonly IWorkCardHistoryService _historyService;
    private readonly IEmployeeService _employeeService;
    private readonly IBranchService _branchService;
    private UserSession? _session;

    public ObservableCollection<WorkCardHistoryDto> Records { get; } = new();
    public ObservableCollection<EmployeeDto> AvailableEmployees { get; } = new();
    public ObservableCollection<BranchDto> AvailableBranches { get; } = new();

    [ObservableProperty] private bool _hasActiveCompany;
    [ObservableProperty] private string _noCompanyMessage = string.Empty;
    [ObservableProperty] private string _statusMessage = string.Empty;
    [ObservableProperty] private bool _isLoading;

    // Filter fields
    [ObservableProperty] private DateTimeOffset? _filterFromDate = DateTimeOffset.Now.AddDays(-30);
    [ObservableProperty] private DateTimeOffset? _filterToDate = DateTimeOffset.Now;
    [ObservableProperty] private EmployeeDto? _filterEmployee;
    [ObservableProperty] private BranchDto? _filterBranch;
    [ObservableProperty] private bool _filterEarlyDepartureOnly;

    public WorkCardHistoryViewModel(
        IWorkCardHistoryService historyService,
        IEmployeeService employeeService,
        IBranchService branchService)
    {
        _historyService = historyService;
        _employeeService = employeeService;
        _branchService = branchService;
    }

    public void Initialize(UserSession session)
    {
        _session = session;
        HasActiveCompany = session.CompanyId.HasValue;
        NoCompanyMessage = session.CompanyId.HasValue
            ? string.Empty
            : "Select a company first (Super Admin: pick a company from the Companies tab).";

        if (HasActiveCompany)
            _ = LoadFiltersAsync();
    }

    private async Task LoadFiltersAsync()
    {
        if (_session?.CompanyId is not int companyId) return;

        var employees = await _employeeService.GetByCompanyAsync(companyId, activeOnly: false).ConfigureAwait(false);
        AvailableEmployees.Clear();
        AvailableEmployees.Add(new EmployeeDto { Id = 0, FirstName = "All", LastName = "Employees" });
        foreach (var e in employees) AvailableEmployees.Add(e);

        var branches = await _branchService.GetByCompanyAsync(companyId).ConfigureAwait(false);
        AvailableBranches.Clear();
        AvailableBranches.Add(new BranchDto { Id = 0, Name = "All Branches" });
        foreach (var b in branches) AvailableBranches.Add(b);

        FilterEmployee = AvailableEmployees[0];
        FilterBranch = AvailableBranches[0];
        StatusMessage = "Set filters and press Load to view records.";
        // Do NOT auto-load records — wait for explicit Load button press
        // to avoid blinking and unnecessary DB queries on navigation.
    }

    [RelayCommand]
    private void ExportToExcel()
    {
        if (!Records.Any())
        {
            StatusMessage = "No records to export — apply filters and load first.";
            return;
        }

        try
        {
            var folder = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            var filePath = ExcelImportExportService.ExportWorkCardHistory(Records.ToList(), folder);
            StatusMessage = $"✅ Exported to Desktop: {Path.GetFileName(filePath)}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"❌ Export failed: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task LoadAsync()
    {
        if (_session?.CompanyId is not int companyId) return;

        IsLoading = true;
        StatusMessage = string.Empty;

        try
        {
            var filter = new WorkCardHistoryFilter
            {
                FromDate = DateOnly.FromDateTime((FilterFromDate ?? DateTimeOffset.Now.AddDays(-30)).LocalDateTime),
                ToDate = DateOnly.FromDateTime((FilterToDate ?? DateTimeOffset.Now).LocalDateTime),
                EmployeeId = FilterEmployee?.Id == 0 ? null : FilterEmployee?.Id,
                BranchId = FilterBranch?.Id == 0 ? null : FilterBranch?.Id,
                EarlyDepartureOnly = FilterEarlyDepartureOnly ? true : null
            };

            var results = await _historyService.GetAsync(companyId, filter).ConfigureAwait(false);
            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
            {
                Records.Clear();
                foreach (var r in results) Records.Add(r);
            });

            StatusMessage = results.Count == 1000
                ? $"Showing first 1,000 records — narrow the date range for more precision."
                : $"{results.Count} record(s) found.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"❌ {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }
}
