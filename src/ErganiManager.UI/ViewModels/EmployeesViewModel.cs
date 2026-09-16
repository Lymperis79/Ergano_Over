using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ErganiManager.Core.Interfaces;
using ErganiManager.Core.Models;

namespace ErganiManager.UI.ViewModels;

public partial class EmployeesViewModel : ViewModelBase, IAdminSectionViewModel
{
    private readonly IEmployeeService _employeeService;
    private readonly IBranchService _branchService;
    private UserSession? _session;

    public ObservableCollection<EmployeeDto> Employees { get; } = new();
    public ObservableCollection<BranchDto> AvailableBranches { get; } = new();

    [ObservableProperty] private bool _isEditing;
    [ObservableProperty] private string _statusMessage = string.Empty;
    [ObservableProperty] private bool _hasActiveCompany;
    [ObservableProperty] private string _noCompanyMessage = string.Empty;

    /// <summary>Raised when the import window should be shown. Handled by code-behind
    /// since opening a window from a ViewModel directly breaks cross-platform.</summary>
    public event EventHandler? ImportRequested;

    [ObservableProperty] private int _formId;
    [ObservableProperty] private string _formFirstName = string.Empty;
    [ObservableProperty] private string _formLastName = string.Empty;
    [ObservableProperty] private string _formTaxId = string.Empty;
    [ObservableProperty] private string _formSocialSecurityNumber = string.Empty;
    [ObservableProperty] private string _formBarcodeId = string.Empty;
    [ObservableProperty] private string _formProfessionCode = string.Empty;
    [ObservableProperty] private int _formWeeklyWorkdays = 5;
    [ObservableProperty] private BranchDto? _formBranch;
    [ObservableProperty] private bool _formIsActive = true;

    public EmployeesViewModel(IEmployeeService employeeService, IBranchService branchService)
    {
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
            _ = LoadAsync();
    }

    [RelayCommand]
    private async Task LoadAsync()
    {
        if (_session?.CompanyId is not int companyId)
            return;

        var branches = await _branchService.GetByCompanyAsync(companyId).ConfigureAwait(false);
        await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
        {
            AvailableBranches.Clear();
            foreach (var b in branches)
                AvailableBranches.Add(b);
        });

        var list = await _employeeService.GetByCompanyAsync(companyId).ConfigureAwait(false);
        await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
        {
            Employees.Clear();
            foreach (var e in list)
                Employees.Add(e);
        });
    }

    [RelayCommand]
    private void StartCreate()
    {
        FormId = 0;
        FormFirstName = string.Empty;
        FormLastName = string.Empty;
        FormTaxId = string.Empty;
        FormSocialSecurityNumber = string.Empty;
        FormBarcodeId = string.Empty;
        FormProfessionCode = string.Empty;
        FormWeeklyWorkdays = 5;
        FormBranch = AvailableBranches.Count > 0 ? AvailableBranches[0] : null;
        FormIsActive = true;
        StatusMessage = string.Empty;
        IsEditing = true;
    }

    [RelayCommand]
    private void StartEdit(EmployeeDto employee)
    {
        FormId = employee.Id;
        FormFirstName = employee.FirstName;
        FormLastName = employee.LastName;
        FormTaxId = employee.TaxId;
        FormSocialSecurityNumber = employee.SocialSecurityNumber;
        FormBarcodeId = employee.BarcodeId;
        FormProfessionCode = employee.ProfessionCode;
        FormWeeklyWorkdays = employee.WeeklyWorkdays;
        FormBranch = AvailableBranches.FirstOrDefaultById(employee.BranchId);
        FormIsActive = employee.IsActive;
        StatusMessage = string.Empty;
        IsEditing = true;
    }

    [RelayCommand]
    private void RequestImport() => ImportRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private void CancelEdit() => IsEditing = false;

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (_session?.CompanyId is not int companyId)
            return;

        if (FormBranch == null)
        {
            StatusMessage = "Please select a branch. (Create a branch first if none exist.)";
            return;
        }

        var dto = new EmployeeDto
        {
            Id = FormId,
            CompanyId = companyId,
            BranchId = FormBranch.Id,
            FirstName = FormFirstName,
            LastName = FormLastName,
            TaxId = FormTaxId,
            SocialSecurityNumber = FormSocialSecurityNumber,
            BarcodeId = FormBarcodeId,
            ProfessionCode = FormProfessionCode,
            WeeklyWorkdays = FormWeeklyWorkdays,
            IsActive = FormIsActive
        };

        try
        {
            if (FormId == 0)
                await _employeeService.CreateAsync(dto);
            else
                await _employeeService.UpdateAsync(dto);

            IsEditing = false;
            await LoadAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = $"❌ {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task ToggleActiveAsync(EmployeeDto employee)
    {
        await _employeeService.SetActiveAsync(employee.Id, !employee.IsActive);
        await LoadAsync();
    }

    [RelayCommand]
    private async Task DeleteAsync(EmployeeDto item)
    {
        if (!await ConfirmDeleteAsync(item)) return;
        try
        {
            await _employeeService.DeleteAsync(item.Id);
            await LoadCommand.ExecuteAsync(null);
        }
        catch (Exception ex)
        {
            StatusMessage = $"\u274c {ex.Message}";
        }
    }

    private static Task<bool> ConfirmDeleteAsync(object item)
        => Task.FromResult(true);
}

internal static class BranchLookupExtensions
{
    public static BranchDto? FirstOrDefaultById(this ObservableCollection<BranchDto> branches, int id) =>
        branches.FirstOrDefault(b => b.Id == id);

    private static BranchDto? FirstOrDefault(this ObservableCollection<BranchDto> branches, Func<BranchDto, bool> predicate)
    {
        foreach (var b in branches)
            if (predicate(b))
                return b;
        return null;
    }

}