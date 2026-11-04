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
    private double _formNormalDailyHours = 8;

    [ObservableProperty]
    private string _formFatherName = string.Empty;

    [ObservableProperty]
    private string _formMotherName = string.Empty;

    [ObservableProperty]
    private DateTimeOffset? _formBirthDate = null;

    [ObservableProperty]
    private string _formSex = string.Empty;

    [ObservableProperty]
    private string _formNationality = string.Empty;

    [ObservableProperty]
    private string _formMaritalStatus = string.Empty;

    [ObservableProperty]
    private int? _formNumberOfChildren = null;

    [ObservableProperty]
    private string _formIdentityDocumentType = string.Empty;

    [ObservableProperty]
    private string _formIdentityDocumentNumber = string.Empty;

    [ObservableProperty]
    private DateTimeOffset? _formEmploymentStartDate = null;

    [ObservableProperty]
    private string _formEmploymentClassification = string.Empty;

    [ObservableProperty]
    private string _formExperienceYears = string.Empty;

    [ObservableProperty]
    private string _formEmploymentRelationship = string.Empty;

    [ObservableProperty]
    private string _formEmploymentStatus = string.Empty;

    [ObservableProperty]
    private double? _formWeeklyHours = null;

    [ObservableProperty]
    private string _formWorkScheduleDescription = string.Empty;

    [ObservableProperty]
    private string _formBreakDescription = string.Empty;

    [ObservableProperty]
    private string _formEducationLevel = string.Empty;

    [ObservableProperty]
    private string _formDigitalTimeOrganization = string.Empty;

    [ObservableProperty]
    private double? _formFullEmploymentHours = null;

    [ObservableProperty]
    private int? _formBreakMinutes = null;

    [ObservableProperty]
    private string _formBreakWithinWorkingHours = string.Empty;

    [ObservableProperty]
    private string _formUsesDigitalWorkCard = string.Empty;

    [ObservableProperty]
    private int? _formFlexibleScheduleMinutes = null;

    [ObservableProperty]
    private DateTimeOffset? _formLastModifiedDate = null;

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
                : Loc["Vm.Select a company first."];

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
                Loc["Vm.❌ No active company selected."];

            return;
        }

        try
        {
            StatusMessage =
                Loc["Vm.Loading branches and employees..."];

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
                    ? Loc["Vm.⚠ No branches found for this company. Import branches from Ergani first."]
                    : string.Format(Loc["Vm.Loaded {0} branch(es) and {1} employee(s)."], AvailableBranches.Count, Employees.Count);
        }
        catch (Exception ex)
        {
            StatusMessage =
                string.Format(Loc["Vm.❌ Error loading employees/branches: {0}"], ex.Message);
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
        FormNormalDailyHours = 8;
        FormFatherName = string.Empty;
        FormMotherName = string.Empty;
        FormBirthDate = null;
        FormSex = string.Empty;
        FormNationality = string.Empty;
        FormMaritalStatus = string.Empty;
        FormNumberOfChildren = null;
        FormIdentityDocumentType = string.Empty;
        FormIdentityDocumentNumber = string.Empty;
        FormEmploymentStartDate = null;
        FormEmploymentClassification = string.Empty;
        FormExperienceYears = string.Empty;
        FormEmploymentRelationship = string.Empty;
        FormEmploymentStatus = string.Empty;
        FormWeeklyHours = null;
        FormWorkScheduleDescription = string.Empty;
        FormBreakDescription = string.Empty;
        FormEducationLevel = string.Empty;
        FormDigitalTimeOrganization = string.Empty;
        FormFullEmploymentHours = null;
        FormBreakMinutes = null;
        FormBreakWithinWorkingHours = string.Empty;
        FormUsesDigitalWorkCard = string.Empty;
        FormFlexibleScheduleMinutes = null;
        FormLastModifiedDate = null;

        FormBranch =
            AvailableBranches.FirstOrDefault();

        FormIsActive = true;

        StatusMessage =
            AvailableBranches.Count == 0
                ? Loc["Vm.⚠ No branches are available. Import branches from Ergani first."]
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

        FormNormalDailyHours = employee.NormalDailyHours;
        FormFatherName = employee.FatherName ?? string.Empty;
        FormMotherName = employee.MotherName ?? string.Empty;
        FormBirthDate = employee.BirthDate;
        FormSex = employee.Sex ?? string.Empty;
        FormNationality = employee.Nationality ?? string.Empty;
        FormMaritalStatus = employee.MaritalStatus ?? string.Empty;
        FormNumberOfChildren = employee.NumberOfChildren;
        FormIdentityDocumentType = employee.IdentityDocumentType ?? string.Empty;
        FormIdentityDocumentNumber = employee.IdentityDocumentNumber ?? string.Empty;
        FormEmploymentStartDate = employee.EmploymentStartDate;
        FormEmploymentClassification = employee.EmploymentClassification ?? string.Empty;
        FormExperienceYears = employee.ExperienceYears ?? string.Empty;
        FormEmploymentRelationship = employee.EmploymentRelationship ?? string.Empty;
        FormEmploymentStatus = employee.EmploymentStatus ?? string.Empty;
        FormWeeklyHours = employee.WeeklyHours;
        FormWorkScheduleDescription = employee.WorkScheduleDescription ?? string.Empty;
        FormBreakDescription = employee.BreakDescription ?? string.Empty;
        FormEducationLevel = employee.EducationLevel ?? string.Empty;
        FormDigitalTimeOrganization = employee.DigitalTimeOrganization ?? string.Empty;
        FormFullEmploymentHours = employee.FullEmploymentHours;
        FormBreakMinutes = employee.BreakMinutes;
        FormBreakWithinWorkingHours = employee.BreakWithinWorkingHours ?? string.Empty;
        FormUsesDigitalWorkCard = employee.UsesDigitalWorkCard ?? string.Empty;
        FormFlexibleScheduleMinutes = employee.FlexibleScheduleMinutes;
        FormLastModifiedDate = employee.LastModifiedDate;

        FormBranch =
            AvailableBranches.FirstOrDefault(
                b => b.Id == employee.BranchId);

        FormIsActive =
            employee.IsActive;

        StatusMessage =
            FormBranch == null
                ? Loc["Vm.⚠ The employee's branch is not present in the current branch list."]
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
                Loc["Vm.❌ No active company selected."];

            return;
        }

        if (FormBranch == null)
        {
            StatusMessage =
                Loc["Vm.Please select a branch."];

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

                FatherName = FormFatherName.Trim(),
                MotherName = FormMotherName.Trim(),
                BirthDate = FormBirthDate,
                Sex = FormSex.Trim(),
                Nationality = FormNationality.Trim(),
                MaritalStatus = FormMaritalStatus.Trim(),
                NumberOfChildren = FormNumberOfChildren,
                IdentityDocumentType = FormIdentityDocumentType.Trim(),
                IdentityDocumentNumber = FormIdentityDocumentNumber.Trim(),
                EmploymentStartDate = FormEmploymentStartDate,
                EmploymentClassification = FormEmploymentClassification.Trim(),
                ExperienceYears = FormExperienceYears.Trim(),
                EmploymentRelationship = FormEmploymentRelationship.Trim(),
                EmploymentStatus = FormEmploymentStatus.Trim(),
                WeeklyHours = FormWeeklyHours,
                WorkScheduleDescription = FormWorkScheduleDescription.Trim(),
                BreakDescription = FormBreakDescription.Trim(),
                EducationLevel = FormEducationLevel.Trim(),
                DigitalTimeOrganization = FormDigitalTimeOrganization.Trim(),
                FullEmploymentHours = FormFullEmploymentHours,
                BreakMinutes = FormBreakMinutes,
                BreakWithinWorkingHours = FormBreakWithinWorkingHours.Trim(),
                UsesDigitalWorkCard = FormUsesDigitalWorkCard.Trim(),
                FlexibleScheduleMinutes = FormFlexibleScheduleMinutes,
                LastModifiedDate = FormLastModifiedDate,
                NormalDailyHours = FormNormalDailyHours,

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
                Loc["Vm.No employees to export."];

            return;
        }

        try
        {
            var branchNames =
                AvailableBranches
                    .ToDictionary(
                        b => b.Id,
                        b => b.Name ?? string.Format(Loc["Vm.Branch {0}"], b.BranchNumber));

            var folder =
                Environment.GetFolderPath(
                    Environment.SpecialFolder.Desktop);

            var filePath =
                ExcelImportExportService.ExportEmployees(
                    Employees.ToList(),
                    branchNames,
                    folder);

            StatusMessage =
                string.Format(Loc["Vm.✅ Exported: {0}"], System.IO.Path.GetFileName(filePath));
        }
        catch (Exception ex)
        {
            StatusMessage =
                $"❌ {ex.Message}";
        }
    }
}