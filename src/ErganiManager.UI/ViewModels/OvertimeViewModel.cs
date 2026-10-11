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

namespace ErganiManager.UI.ViewModels;

public partial class OvertimeViewModel : ViewModelBase, IAdminSectionViewModel
{
    private readonly IOvertimeService _overtimeService;
    private readonly IOvertimeSubmitter _overtimeSubmitter;
    private readonly IErganiDocumentService _documentService;
    private readonly IEmployeeService _employeeService;
    private readonly IBranchService _branchService;
    private readonly HashSet<int> _selectedIds = new();
    private UserSession? _session;

    public ObservableCollection<OvertimeDto> Records { get; } = new();
    public ObservableCollection<EmployeeDto> AvailableEmployees { get; } = new();
    public ObservableCollection<BranchDto> AvailableBranches { get; } = new();
    public ObservableCollection<AppOvertimeJustification> Justifications { get; } = new(Enum.GetValues<AppOvertimeJustification>());

    [ObservableProperty] private bool _hasActiveCompany;
    [ObservableProperty] private string _noCompanyMessage = string.Empty;
    [ObservableProperty] private string _statusMessage = string.Empty;
    [ObservableProperty] private bool _isCreating;
    [ObservableProperty] private bool _isResponseOpen;
    [ObservableProperty] private string _responseTitle = string.Empty;
    [ObservableProperty] private string _responseText = string.Empty;

    [ObservableProperty] private DateTimeOffset? _filterFrom = DateTimeOffset.Now.AddMonths(-1);
    [ObservableProperty] private DateTimeOffset? _filterTo = DateTimeOffset.Now;
    [ObservableProperty] private EmployeeDto? _formEmployee;
    [ObservableProperty] private BranchDto? _formBranch;
    [ObservableProperty] private DateTimeOffset? _formDate = DateTimeOffset.Now;
    [ObservableProperty] private TimeSpan? _formStartTime = new(17, 0, 0);
    [ObservableProperty] private TimeSpan? _formEndTime = new(20, 0, 0);
    [ObservableProperty] private AppOvertimeJustification _formJustification = AppOvertimeJustification.ExceptionalWorkload;
    [ObservableProperty] private int _formWeeklyWorkdays = 5;
    [ObservableProperty] private string _formAseeApproval = string.Empty;

    public bool HasSelection => _selectedIds.Count > 0;

    public OvertimeViewModel(IOvertimeService overtimeService, IOvertimeSubmitter overtimeSubmitter, IErganiDocumentService documentService, IEmployeeService employeeService, IBranchService branchService)
    {
        _overtimeService = overtimeService; _overtimeSubmitter = overtimeSubmitter; _documentService = documentService; _employeeService = employeeService; _branchService = branchService;
    }

    public void Initialize(UserSession session)
    {
        _session = session; HasActiveCompany = session.CompanyId.HasValue;
        NoCompanyMessage = session.CompanyId.HasValue ? string.Empty : "Select a company first.";
        if (HasActiveCompany) _ = LoadDataAsync();
    }

    private async Task LoadDataAsync()
    {
        if (_session?.CompanyId is not int companyId) return;
        var employees = await _employeeService.GetByCompanyAsync(companyId, activeOnly: true).ConfigureAwait(false);
        await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => { AvailableEmployees.Clear(); foreach (var e in employees) AvailableEmployees.Add(e); });
        var branches = await _branchService.GetByCompanyAsync(companyId).ConfigureAwait(false);
        await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => { AvailableBranches.Clear(); foreach (var b in branches) AvailableBranches.Add(b); });
        FormEmployee = AvailableEmployees.FirstOrDefault(); FormBranch = AvailableBranches.FirstOrDefault();
        await LoadRecordsAsync();
    }

    [RelayCommand]
    private async Task LoadRecordsAsync()
    {
        if (_session?.CompanyId is not int companyId) return;
        var list = await _overtimeService.GetByCompanyAsync(companyId, DateOnly.FromDateTime((FilterFrom ?? DateTimeOffset.Now.AddMonths(-1)).LocalDateTime), DateOnly.FromDateTime((FilterTo ?? DateTimeOffset.Now).LocalDateTime));
        await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => { Records.Clear(); _selectedIds.Clear(); foreach (var r in list) Records.Add(r); OnPropertyChanged(nameof(HasSelection)); });
        StatusMessage = $"{Records.Count} record(s).";
    }

    public void SetSelected(OvertimeDto record, bool selected)
    { if (selected) _selectedIds.Add(record.Id); else _selectedIds.Remove(record.Id); OnPropertyChanged(nameof(HasSelection)); }

    [RelayCommand]
    private async Task SubmitSelectedAsync()
    {
        if (_session?.CompanyId is not int companyId) return;
        var ids = _selectedIds.ToList();
        if (ids.Count == 0) { StatusMessage = "Select at least one unsent overtime record."; return; }
        try
        {
            var result = await _overtimeSubmitter.SubmitAsync(companyId, ids);
            StatusMessage = result.Success ? $"✅ Submitted {result.SubmittedCount} overtime record(s) in one request per branch. Protocol: {result.Protocol}" : result.SubmittedCount > 0 ? $"⚠ {result.SubmittedCount} sent, {result.Errors.Count} NOT sent — {result.ErrorMessage}" : $"❌ {result.ErrorMessage}";
            _selectedIds.Clear(); OnPropertyChanged(nameof(HasSelection)); await LoadRecordsAsync();
            if (!string.IsNullOrWhiteSpace(result.ResponseRawJson)) ShowResponse("Ergani Overtime Response", result.ResponseRawJson);
        }
        catch (Exception ex) { StatusMessage = $"❌ {ex.Message}"; }
    }

    [RelayCommand]
    private void ShowResponseFor(OvertimeDto record)
    {
        if (string.IsNullOrWhiteSpace(record.ResponseRawJson)) { StatusMessage = "No stored Ergani response is available for this record."; return; }
        ShowResponse($"Ergani Response — {record.EmployeeFullName}", record.ResponseRawJson);
    }

    private void ShowResponse(string title, string response)
    { ResponseTitle = title; ResponseText = response; IsResponseOpen = true; }

    [RelayCommand] private void CloseResponse() => IsResponseOpen = false;

    [RelayCommand]
    private async Task DownloadPdfAsync(OvertimeDto record)
    {
        if (_session?.CompanyId is not int companyId || string.IsNullOrWhiteSpace(record.Protocol)) { StatusMessage = "A protocol is required before downloading the PDF."; return; }
        try
        {
            var date = record.SubmittedDate ?? record.OvertimeDate;
            var bytes = await _documentService.DownloadPdfAsync(companyId, "Documents/WTOOv", record.Protocol, date);
            var folder = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            var safeName = string.Join("_", record.EmployeeFullName.Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries));
            var path = Path.Combine(folder, $"Ergani_Overtime_{safeName}_{record.OvertimeDate:yyyyMMdd}_{record.Protocol}.pdf");
            await File.WriteAllBytesAsync(path, bytes); StatusMessage = $"✅ PDF saved to Desktop: {Path.GetFileName(path)}";
        }
        catch (Exception ex) { StatusMessage = $"❌ PDF download failed: {ex.Message}"; }
    }

    [RelayCommand] private void StartCreate()
    { FormEmployee=AvailableEmployees.FirstOrDefault(); FormBranch=AvailableBranches.FirstOrDefault(); FormDate=DateTimeOffset.Now; FormStartTime=new(17,0,0); FormEndTime=new(20,0,0); FormJustification=AppOvertimeJustification.ExceptionalWorkload; FormWeeklyWorkdays=5; FormAseeApproval=string.Empty; StatusMessage=string.Empty; IsCreating=true; }
    [RelayCommand] private void CancelCreate() => IsCreating=false;

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (_session?.CompanyId is not int companyId) return;
        if (FormEmployee == null || FormBranch == null) { StatusMessage="Employee and branch are required."; return; }
        if (!FormStartTime.HasValue || !FormEndTime.HasValue) { StatusMessage="Start and end time are required."; return; }
        try
        {
            await _overtimeService.CreateAsync(new OvertimeDto { EmployeeId=FormEmployee.Id, BranchId=FormBranch.Id, OvertimeDate=DateOnly.FromDateTime((FormDate ?? DateTimeOffset.Now).LocalDateTime), StartTime=TimeOnly.FromTimeSpan(FormStartTime.Value), EndTime=TimeOnly.FromTimeSpan(FormEndTime.Value), Justification=FormJustification, WeeklyWorkdaysNumber=FormWeeklyWorkdays, AseeApproval=string.IsNullOrWhiteSpace(FormAseeApproval)?null:FormAseeApproval }, companyId);
            IsCreating=false; await LoadRecordsAsync();
        }
        catch (Exception ex) { StatusMessage=$"❌ {ex.Message}"; }
    }

    [RelayCommand] private async Task CancelOvertimeAsync(OvertimeDto record) { try { await _overtimeService.CancelAsync(record.Id); await LoadRecordsAsync(); } catch(Exception ex){ StatusMessage=$"❌ {ex.Message}"; } }
    [RelayCommand] private async Task DeleteAsync(OvertimeDto record) { await _overtimeService.DeleteAsync(record.Id); await LoadRecordsAsync(); }
}
