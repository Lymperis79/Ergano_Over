using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ErganiManager.Core.Interfaces;
using ErganiManager.Core.Models;
using ErganiManager.UI.Services;

namespace ErganiManager.UI.ViewModels;

public partial class EmployeesViewModel : ViewModelBase, IAdminSectionViewModel
{
    private readonly IEmployeeService _employeeService;
    private readonly IBranchService _branchService;

    private UserSession? _session;

    public ObservableCollection<EmployeeDto> Employees { get; } = new();

    public ObservableCollection<BranchDto> AvailableBranches { get; } = new();

    [ObservableProperty]
    private bool _isEditing;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private bool _hasActiveCompany;

    [ObservableProperty]
    private string _noCompanyMessage = string.Empty;

    [ObservableProperty]
    private int _formId;

    [ObservableProperty]
    private string _formFirstName = string.Empty;

    [ObservableProperty]
    private string _formLastName = string.Empty;

    [ObservableProperty]
    private string _formTaxId = string.Empty;

    [ObservableProperty]
    private string _formSocialSecurityNumber = string.Empty;

    [ObservableProperty]
    private string _formBarcodeId = string.Empty;

    [ObservableProperty]
    private string _formProfessionCode = string.Empty;

    [ObservableProperty]
    private int _formWeeklyWorkdays = 5;

    [ObservableProperty]
    private BranchDto? _formBranch;

    [ObservableProperty]
    private bool _formIsActive = true;

    public event EventHandler? ImportRequested;

    public EmployeesViewModel(
        IEmployeeService employeeService,
        IBranchService branchService)
    {
        _employeeService = employeeService;
        _branchService = branchService;
    }

    public void Initialize(UserSession session)
    {
        _session = session;

        HasActiveCompany =
            session.CompanyId.HasValue;

        NoCompanyMessage =
            session.CompanyId.HasValue
                ? string.Empty
                : "Select a company first.";

        if (HasActiveCompany)
        {
            _ = LoadAsync();
        }
    }

    [RelayCommand]
    private async Task LoadAsync()
    {
        if (_session?.CompanyId is not int companyId)
        {
            StatusMessage =
                "❌ No active company selected.";

            return;
        }

        try
        {
            StatusMessage =
                "Loading branches and employees...";

            // =====================================================
            // LOAD BRANCHES FIRST
            // =====================================================

            var branches =
                await _branchService
                    .GetByCompanyAsync(companyId)
                    .ConfigureAwait(false);

            await Avalonia.Threading.Dispatcher.UIThread
                .InvokeAsync(() =>
                {
                    AvailableBranches.Clear();

                    foreach (var branch in branches
                             .Where(b => b.IsActive)
                             .OrderBy(b => b.BranchNumber))
                    {
                        AvailableBranches.Add(branch);
                    }
                });

            // =====================================================
            // LOAD EMPLOYEES
            // =====================================================

            var employees =
                await _employeeService
                    .GetByCompanyAsync(companyId)
                    .ConfigureAwait(false);

            await Avalonia.Threading.Dispatcher.UIThread
                .InvokeAsync(() =>
                {
                    Employees.Clear();

                    foreach (var employee in employees)
                    {
                        Employees.Add(employee);
                    }
                });

            StatusMessage =
                AvailableBranches.Count == 0
                    ? "⚠ No branches found for this company. Import branches from Ergani first."
                    : $"Loaded {AvailableBranches.Count} branch(es) and {Employees.Count} employee(s).";
        }
        catch (Exception ex)
        {
            StatusMessage =
                $"❌ Error loading employees/branches: {ex.Message}";
        }
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

        FormBranch =
            AvailableBranches.FirstOrDefault();

        FormIsActive = true;

        StatusMessage =
            AvailableBranches.Count == 0
                ? "⚠ No branches are available. Import branches from Ergani first."
                : string.Empty;

        IsEditing = true;
    }

    [RelayCommand]
    private void StartEdit(EmployeeDto employee)
    {
        FormId =
            employee.Id;

        FormFirstName =
            employee.FirstName;

        FormLastName =
            employee.LastName;

        FormTaxId =
            employee.TaxId;

        FormSocialSecurityNumber =
            employee.SocialSecurityNumber;

        FormBarcodeId =
            employee.BarcodeId;

        FormProfessionCode =
            employee.ProfessionCode;

        FormWeeklyWorkdays =
            employee.WeeklyWorkdays;

        FormBranch =
            AvailableBranches.FirstOrDefault(
                b => b.Id == employee.BranchId);

        FormIsActive =
            employee.IsActive;

        StatusMessage =
            FormBranch == null
                ? "⚠ The employee's branch is not present in the current branch list."
                : string.Empty;

        IsEditing = true;
    }

    [RelayCommand]
    private void RequestImport()
    {
        ImportRequested?.Invoke(
            this,
            EventArgs.Empty);
    }

    [RelayCommand]
    private void CancelEdit()
    {
        IsEditing = false;

        StatusMessage =
            string.Empty;
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (_session?.CompanyId is not int companyId)
        {
            StatusMessage =
                "❌ No active company selected.";

            return;
        }

        if (FormBranch == null)
        {
            StatusMessage =
                "Please select a branch.";

            return;
        }

        var dto =
            new EmployeeDto
            {
                Id =
                    FormId,

                CompanyId =
                    companyId,

                BranchId =
                    FormBranch.Id,

                FirstName =
                    FormFirstName,

                LastName =
                    FormLastName,

                TaxId =
                    FormTaxId,

                SocialSecurityNumber =
                    FormSocialSecurityNumber,

                BarcodeId =
                    FormBarcodeId,

                ProfessionCode =
                    FormProfessionCode,

                WeeklyWorkdays =
                    FormWeeklyWorkdays,

                IsActive =
                    FormIsActive
            };

        try
        {
            if (FormId == 0)
            {
                await _employeeService
                    .CreateAsync(dto);
            }
            else
            {
                await _employeeService
                    .UpdateAsync(dto);
            }

            IsEditing = false;

            await LoadAsync();
        }
        catch (Exception ex)
        {
            StatusMessage =
                $"❌ {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task ToggleActiveAsync(
        EmployeeDto employee)
    {
        await _employeeService
            .SetActiveAsync(
                employee.Id,
                !employee.IsActive);

        await LoadAsync();
    }

    [RelayCommand]
    private async Task DeleteAsync(
        EmployeeDto item)
    {
        if (!await ConfirmDeleteAsync(item))
            return;

        try
        {
            await _employeeService
                .DeleteAsync(item.Id);

            await LoadCommand
                .ExecuteAsync(null);
        }
        catch (Exception ex)
        {
            StatusMessage =
                $"❌ {ex.Message}";
        }
    }

    private static Task<bool> ConfirmDeleteAsync(
        object item)
    {
        return Task.FromResult(true);
    }

    [RelayCommand]
    private async Task ExportAsync()
    {
        if (!Employees.Any())
        {
            StatusMessage =
                "No employees to export.";

            return;
        }

        try
        {
            var branchNames =
                AvailableBranches
                    .ToDictionary(
                        b => b.Id,
                        b => b.Name ?? $"Branch {b.BranchNumber}");

            var folder =
                Environment.GetFolderPath(
                    Environment.SpecialFolder.Desktop);

            var filePath =
                ExcelImportExportService.ExportEmployees(
                    Employees.ToList(),
                    branchNames,
                    folder);

            StatusMessage =
                $"✅ Exported: {System.IO.Path.GetFileName(filePath)}";
        }
        catch (Exception ex)
        {
            StatusMessage =
                $"❌ {ex.Message}";
        }
    }
}