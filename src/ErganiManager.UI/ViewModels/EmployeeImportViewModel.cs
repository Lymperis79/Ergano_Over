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
            StatusMessage = $"✅ Template saved to Desktop: {Path.GetFileName(path)}";
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
        StatusMessage = "Parsing file...";
        ImportDone = false;

        try
        {
            var result = ExcelImportExportService.ParseEmployeeImportFile(filePath);
            foreach (var r in result.ValidRows) ValidRows.Add(r);
            foreach (var r in result.InvalidRows) InvalidRows.Add(r);
            HasPreview = true;
            StatusMessage = $"{result.ValidRows.Count} valid row(s), {result.InvalidRows.Count} with errors.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"❌ Failed to parse file: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task ImportValidRowsAsync()
    {
        if (!ValidRows.Any())
        {
            StatusMessage = "No valid rows to import.";
            return;
        }

        IsImporting = true;
        ImportedCount = 0;
        StatusMessage = "Importing...";

        try
        {
            // Resolve branches by name once upfront
            var branches = await _branchService.GetByCompanyAsync(_companyId);
            var branchByName = branches.ToDictionary(
                b => b.Name?.ToLowerInvariant() ?? b.Address.ToLowerInvariant());

            int succeeded = 0;
            var errors = new System.Collections.Generic.List<string>();

            foreach (var row in ValidRows)
            {
                try
                {
                    // Match branch by name (case-insensitive)
                    var branchKey = row.BranchName.ToLowerInvariant();
                    if (!branchByName.TryGetValue(branchKey, out var branch))
                    {
                        errors.Add($"Row {row.RowNumber}: branch '{row.BranchName}' not found.");
                        continue;
                    }

                    // Skip if barcode is already taken (idempotent re-import)
                    var taken = await _employeeService.IsBarcodeTakenAsync(_companyId, row.BarcodeId);
                    if (taken)
                    {
                        errors.Add($"Row {row.RowNumber}: barcode '{row.BarcodeId}' already exists — skipped.");
                        continue;
                    }

                    await _employeeService.CreateAsync(new EmployeeDto
                    {
                        CompanyId = _companyId,
                        BranchId = branch.Id,
                        FirstName = row.FirstName,
                        LastName = row.LastName,
                        TaxId = row.TaxId,
                        SocialSecurityNumber = row.SocialSecurityNumber,
                        BarcodeId = row.BarcodeId,
                        ProfessionCode = row.ProfessionCode,
                        WeeklyWorkdays = row.WeeklyWorkdays,
                        IsActive = true
                    });

                    succeeded++;
                }
                catch (Exception ex)
                {
                    errors.Add($"Row {row.RowNumber}: {ex.Message}");
                }
            }

            ImportedCount = succeeded;
            ImportDone = true;

            StatusMessage = errors.Count == 0
                ? $"✅ {succeeded} employee(s) imported successfully."
                : $"✅ {succeeded} imported. ⚠️ {errors.Count} skipped:\n" + string.Join("\n", errors);

            if (succeeded > 0)
                ImportCompleted?.Invoke(this, EventArgs.Empty);
        }
        finally
        {
            IsImporting = false;
        }
    }
}
