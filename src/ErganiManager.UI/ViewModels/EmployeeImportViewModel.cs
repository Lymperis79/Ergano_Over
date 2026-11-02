using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ErganiManager.Core.Interfaces;
using ErganiManager.UI.Services;

namespace ErganiManager.UI.ViewModels;

public partial class EmployeeImportViewModel : ViewModelBase
{
    private readonly IEmployeeService _employeeService;
    private readonly IBranchService _branchService;
    private readonly int _companyId;

    public event EventHandler? ImportCompleted;

    public ObservableCollection<EmployeeImportRow> ValidRows { get; } = new();
    public ObservableCollection<EmployeeImportRow> InvalidRows { get; } = new();

    [ObservableProperty] private string _statusMessage = string.Empty;
    [ObservableProperty] private bool _hasPreview;
    [ObservableProperty] private bool _isImporting;
    [ObservableProperty] private string _selectedFilePath = string.Empty;
    [ObservableProperty] private int _importedCount;
    [ObservableProperty] private bool _importDone;

    public EmployeeImportViewModel(IEmployeeService employeeService, IBranchService branchService, int companyId)
    {
        _employeeService = employeeService;
        _branchService = branchService;
        _companyId = companyId;
    }

    [RelayCommand]
    private async Task DownloadTemplateAsync()
    {
        try
        {
            var folder = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            var path = Path.Combine(folder, "EmployeeImportTemplate.xlsx");
            ExcelImportExportService.GenerateEmployeeImportTemplate(path);
            StatusMessage = string.Format(Loc["Vm.✅ Template saved to Desktop: {0}"], Path.GetFileName(path));
        }
        catch (Exception ex)
        {
            StatusMessage = $"❌ {ex.Message}";
        }
        await Task.CompletedTask;
    }

    public void LoadFile(string filePath)
    {
        SelectedFilePath = filePath;
        ValidRows.Clear();
        InvalidRows.Clear();
        HasPreview = false;
        StatusMessage = Loc["Vm.Parsing file..."];
        ImportDone = false;

        try
        {
            var result = ExcelImportExportService.ParseEmployeeImportFile(filePath);
            foreach (var r in result.ValidRows) ValidRows.Add(r);
            foreach (var r in result.InvalidRows) InvalidRows.Add(r);
            HasPreview = true;
            StatusMessage = string.Format(Loc["Vm.ImportPreviewSummary"], result.ValidRows.Count, result.InvalidRows.Count);
        }
        catch (Exception ex)
        {
            StatusMessage = string.Format(Loc["Vm.FailedParseFile"], ex.Message);
        }
    }

    [RelayCommand]
    private async Task ImportValidRowsAsync()
    {
        if (!ValidRows.Any())
        {
            StatusMessage = Loc["Vm.No valid rows to import."];
            return;
        }

        IsImporting = true;
        ImportedCount = 0;
        StatusMessage = Loc["Vm.Importing..."];

        try
        {
            var branches = await _branchService.GetByCompanyAsync(_companyId);
            var employees = await _employeeService.GetByCompanyAsync(_companyId);
            int created = 0;
            int updated = 0;
            var errors = new System.Collections.Generic.List<string>();

            foreach (var row in ValidRows)
            {
                try
                {
                    var branch = branches.FirstOrDefault(b =>
                        NormalizeMatch(b.Name) == NormalizeMatch(row.BranchName) ||
                        NormalizeMatch(b.Address) == NormalizeMatch(row.BranchName));

                    if (branch == null)
                    {
                        errors.Add(string.Format(Loc["Vm.Row {0}: branch '{1}' not found."], row.RowNumber, row.BranchName));
                        continue;
                    }

                    var afm = NormalizeTaxId(row.TaxId);
                    var afmMatches = employees.Where(e => NormalizeTaxId(e.TaxId) == afm).ToList();
                    var existing = afmMatches.FirstOrDefault(e =>
                        NormalizeMatch(e.FirstName) == NormalizeMatch(row.FirstName) &&
                        NormalizeMatch(e.LastName) == NormalizeMatch(row.LastName));

                    if (existing == null && afmMatches.Count > 0)
                    {
                        errors.Add(string.Format(
                            Loc["Vm.Row {0}: AFM matches an existing employee, but first name or surname differs. Skipped."],
                            row.RowNumber));
                        continue;
                    }

                    if (await _employeeService.IsBarcodeTakenAsync(
                            _companyId, row.BarcodeId, existing?.Id))
                    {
                        errors.Add(string.Format(
                            Loc["Vm.Row {0}: barcode '{1}' already belongs to another employee. Skipped."],
                            row.RowNumber, row.BarcodeId));
                        continue;
                    }

                    var dto = existing == null
                        ? new EmployeeDto
                        {
                            CompanyId = _companyId,
                            BranchId = branch.Id,
                            FirstName = row.FirstName,
                            LastName = row.LastName,
                            TaxId = afm,
                            SocialSecurityNumber = row.SocialSecurityNumber,
                            BarcodeId = row.BarcodeId,
                            ProfessionCode = row.ProfessionCode,
                            WeeklyWorkdays = row.WeeklyWorkdays,
                            NormalDailyHours = row.NormalDailyHours ?? 8,
                            IsActive = row.IsActive ?? true
                        }
                        : CopyEmployee(existing);

                    // Required identity and operational values come from the spreadsheet.
                    dto.BranchId = branch.Id;
                    dto.FirstName = row.FirstName;
                    dto.LastName = row.LastName;
                    dto.TaxId = afm;
                    dto.SocialSecurityNumber = row.SocialSecurityNumber;
                    dto.BarcodeId = row.BarcodeId;
                    if (!string.IsNullOrWhiteSpace(row.ProfessionCode)) dto.ProfessionCode = row.ProfessionCode;
                    dto.WeeklyWorkdays = row.WeeklyWorkdays;
                    if (row.NormalDailyHours.HasValue) dto.NormalDailyHours = row.NormalDailyHours.Value;
                    if (row.IsActive.HasValue) dto.IsActive = row.IsActive.Value;

                    // Empty optional cells mean "leave the existing value unchanged".
                    SetIfProvided(row.FatherName, value => dto.FatherName = value);
                    SetIfProvided(row.MotherName, value => dto.MotherName = value);
                    if (row.BirthDate.HasValue) dto.BirthDate = row.BirthDate;
                    SetIfProvided(row.Sex, value => dto.Sex = value);
                    SetIfProvided(row.Nationality, value => dto.Nationality = value);
                    SetIfProvided(row.MaritalStatus, value => dto.MaritalStatus = value);
                    if (row.NumberOfChildren.HasValue) dto.NumberOfChildren = row.NumberOfChildren;
                    SetIfProvided(row.IdentityDocumentType, value => dto.IdentityDocumentType = value);
                    SetIfProvided(row.IdentityDocumentNumber, value => dto.IdentityDocumentNumber = value);
                    if (row.EmploymentStartDate.HasValue) dto.EmploymentStartDate = row.EmploymentStartDate;
                    SetIfProvided(row.EmploymentClassification, value => dto.EmploymentClassification = value);
                    SetIfProvided(row.ExperienceYears, value => dto.ExperienceYears = value);
                    SetIfProvided(row.EmploymentRelationship, value => dto.EmploymentRelationship = value);
                    SetIfProvided(row.EmploymentStatus, value => dto.EmploymentStatus = value);
                    if (row.WeeklyHours.HasValue) dto.WeeklyHours = row.WeeklyHours;
                    SetIfProvided(row.WorkScheduleDescription, value => dto.WorkScheduleDescription = value);
                    SetIfProvided(row.BreakDescription, value => dto.BreakDescription = value);
                    SetIfProvided(row.EducationLevel, value => dto.EducationLevel = value);
                    SetIfProvided(row.DigitalTimeOrganization, value => dto.DigitalTimeOrganization = value);
                    if (row.FullEmploymentHours.HasValue) dto.FullEmploymentHours = row.FullEmploymentHours;
                    if (row.BreakMinutes.HasValue) dto.BreakMinutes = row.BreakMinutes;
                    SetIfProvided(row.BreakWithinWorkingHours, value => dto.BreakWithinWorkingHours = value);
                    SetIfProvided(row.UsesDigitalWorkCard, value => dto.UsesDigitalWorkCard = value);
                    if (row.FlexibleScheduleMinutes.HasValue) dto.FlexibleScheduleMinutes = row.FlexibleScheduleMinutes;
                    if (row.LastModifiedDate.HasValue) dto.LastModifiedDate = row.LastModifiedDate;

                    if (existing == null)
                    {
                        var newId = await _employeeService.CreateAsync(dto);
                        dto.Id = newId;
                        employees.Add(dto);
                        created++;
                    }
                    else
                    {
                        dto.Id = existing.Id;
                        await _employeeService.UpdateAsync(dto);
                        var index = employees.FindIndex(e => e.Id == existing.Id);
                        if (index >= 0) employees[index] = dto;
                        updated++;
                    }
                }
                catch (Exception ex)
                {
                    errors.Add(string.Format(Loc["Vm.Row {0}: {1}"], row.RowNumber, ex.Message));
                }
            }

            ImportedCount = created + updated;
            ImportDone = true;
            StatusMessage = errors.Count == 0
                ? string.Format(Loc["Vm.EmployeeImportSuccess"], created + updated) +
                  $" ({created} new, {updated} updated)"
                : $"{created} new, {updated} updated; {errors.Count} skipped:{Environment.NewLine}" +
                  string.Join(Environment.NewLine, errors);

            if (created + updated > 0)
                ImportCompleted?.Invoke(this, EventArgs.Empty);
        }
        finally
        {
            IsImporting = false;
        }
    }

    private static EmployeeDto CopyEmployee(EmployeeDto e) => new()
    {
        Id = e.Id,
        CompanyId = e.CompanyId,
        BranchId = e.BranchId,
        FirstName = e.FirstName,
        LastName = e.LastName,
        TaxId = e.TaxId,
        SocialSecurityNumber = e.SocialSecurityNumber,
        BarcodeId = e.BarcodeId,
        ProfessionCode = e.ProfessionCode,
        WeeklyWorkdays = e.WeeklyWorkdays,
        FatherName = e.FatherName,
        MotherName = e.MotherName,
        BirthDate = e.BirthDate,
        Sex = e.Sex,
        Nationality = e.Nationality,
        MaritalStatus = e.MaritalStatus,
        NumberOfChildren = e.NumberOfChildren,
        IdentityDocumentType = e.IdentityDocumentType,
        IdentityDocumentNumber = e.IdentityDocumentNumber,
        EmploymentStartDate = e.EmploymentStartDate,
        EmploymentClassification = e.EmploymentClassification,
        ExperienceYears = e.ExperienceYears,
        EmploymentRelationship = e.EmploymentRelationship,
        EmploymentStatus = e.EmploymentStatus,
        WeeklyHours = e.WeeklyHours,
        WorkScheduleDescription = e.WorkScheduleDescription,
        BreakDescription = e.BreakDescription,
        EducationLevel = e.EducationLevel,
        DigitalTimeOrganization = e.DigitalTimeOrganization,
        FullEmploymentHours = e.FullEmploymentHours,
        BreakMinutes = e.BreakMinutes,
        BreakWithinWorkingHours = e.BreakWithinWorkingHours,
        UsesDigitalWorkCard = e.UsesDigitalWorkCard,
        FlexibleScheduleMinutes = e.FlexibleScheduleMinutes,
        LastModifiedDate = e.LastModifiedDate,
        NormalDailyHours = e.NormalDailyHours,
        IsActive = e.IsActive
    };

    private static void SetIfProvided(string? value, Action<string?> setter)
    {
        if (!string.IsNullOrWhiteSpace(value))
            setter(value.Trim());
    }

    private static string NormalizeTaxId(string? value) =>
        new((value ?? string.Empty).Where(char.IsDigit).ToArray());

    private static string NormalizeMatch(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var decomposed = value.Trim().Normalize(System.Text.NormalizationForm.FormD);
        var builder = new System.Text.StringBuilder();
        foreach (var ch in decomposed)
        {
            if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(ch) ==
                    System.Globalization.UnicodeCategory.NonSpacingMark ||
                char.IsWhiteSpace(ch))
                continue;
            builder.Append(char.ToUpperInvariant(ch));
        }
        return builder.ToString().Normalize(System.Text.NormalizationForm.FormC);
    }

}
