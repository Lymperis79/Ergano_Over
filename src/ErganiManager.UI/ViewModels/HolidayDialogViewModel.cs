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

/// <summary>One holiday day in the holidays list.</summary>
public class HolidayRow
{
    public int Id { get; init; }
    public DateOnly LeaveDate { get; init; }
    public string LeaveTypeCode { get; init; } = string.Empty;
    public TimeOnly? StartTime { get; init; }
    public TimeOnly? EndTime { get; init; }
    public bool SubmittedToErgani { get; init; }
    public string? Protocol { get; init; }
    public DateOnly? SubmittedDate { get; init; }
    public string? ResponseRawJson { get; init; }
    public string? LastError { get; init; }

    public string StatusIcon => SubmittedToErgani ? "✅" : (string.IsNullOrWhiteSpace(LastError) ? "🕓" : "❌");
    public string DateText => LeaveDate.ToString("dd/MM/yyyy");
    public string TypeText => LeaveTypes.Label(LeaveTypeCode);
    public string HoursText => StartTime.HasValue && EndTime.HasValue
        ? $"{StartTime:HH:mm}–{EndTime:HH:mm}"
        : "Whole day";
    public bool IsUnsent => !SubmittedToErgani;
    public bool HasProtocol => SubmittedToErgani && !string.IsNullOrWhiteSpace(Protocol) && SubmittedDate.HasValue;
    public string Summary => SubmittedToErgani
        ? (string.IsNullOrWhiteSpace(Protocol) ? "Accepted by Ergani" : $"Protocol: {Protocol}")
        : (string.IsNullOrWhiteSpace(LastError) ? "Not submitted yet" : LastError!);
}

/// <summary>The holidays form: pick a holiday type and dates, save, submit to Ergani, and review
/// what was sent (response + PDF) — the same flow as overtime, opened from the Schedules page.</summary>
public partial class HolidayDialogViewModel : ObservableObject
{
    /// <summary>Raised after holiday records have been changed so the schedule calendar can refresh.</summary>
    public event EventHandler? HolidaysChanged;

    private readonly ILeaveService _leaveService;
    private readonly ILeaveSubmitter _leaveSubmitter;
    private readonly IErganiDocumentService _documentService;

    private int _companyId;
    private EmployeeDto? _employee;

    public HolidayDialogViewModel(
        ILeaveService leaveService, ILeaveSubmitter leaveSubmitter, IErganiDocumentService documentService)
    {
        _leaveService     = leaveService;
        _leaveSubmitter   = leaveSubmitter;
        _documentService  = documentService;
    }

    public IReadOnlyList<LeaveTypeInfo> LeaveTypeOptions => LeaveTypes.All;
    public ObservableCollection<BranchDto> Branches { get; } = new();
    public ObservableCollection<HolidayRow> Rows { get; } = new();

    [ObservableProperty] private bool _isOpen;
    [ObservableProperty] private string _title = string.Empty;

    // ── Form ──
    [ObservableProperty] private LeaveTypeInfo? _selectedType;
    [ObservableProperty] private BranchDto? _selectedBranch;
    [ObservableProperty] private DateTimeOffset? _fromDate = DateTimeOffset.Now;
    [ObservableProperty] private DateTimeOffset? _toDate = DateTimeOffset.Now;
    [ObservableProperty] private bool _skipWeekends = true;
    [ObservableProperty] private TimeSpan? _startTime = new TimeSpan(9, 0, 0);
    [ObservableProperty] private TimeSpan? _endTime = new TimeSpan(13, 0, 0);
    [ObservableProperty] private string _referenceYear = string.Empty;
    [ObservableProperty] private string _entitledDays = string.Empty;
    [ObservableProperty] private string _comments = string.Empty;

    [ObservableProperty] private string _statusMessage = string.Empty;
    [ObservableProperty] private bool _hasRows;

    [ObservableProperty] private bool _isResponseOpen;
    [ObservableProperty] private string _responseTitle = string.Empty;
    [ObservableProperty] private string _responseText = string.Empty;

    public bool IsHourlyType => SelectedType?.IsHourly == true;
    public int UnsentCount => Rows.Count(r => r.IsUnsent);
    public string SubmitUnsentText => $"📤 Submit unsent ({UnsentCount})";

    partial void OnSelectedTypeChanged(LeaveTypeInfo? value) => OnPropertyChanged(nameof(IsHourlyType));

    /// <summary>Opens the form for the employee. When days were selected on the calendar and they form
    /// one continuous block, the dates are pre-filled with that block.</summary>
    public async Task OpenAsync(
        int companyId, EmployeeDto employee, IEnumerable<BranchDto> branches,
        DateOnly? prefillFrom, DateOnly? prefillTo)
    {
        _companyId = companyId;
        _employee  = employee;

        Branches.Clear();
        foreach (var b in branches) Branches.Add(b);
        SelectedBranch = Branches.FirstOrDefault(b => b.Id == employee.BranchId) ?? Branches.FirstOrDefault();

        Title          = $"🏖 Holidays — {employee.FullName}";
        SelectedType   = null;
        ReferenceYear  = string.Empty;
        EntitledDays   = string.Empty;
        Comments       = string.Empty;
        StatusMessage  = string.Empty;
        IsResponseOpen = false;

        if (prefillFrom.HasValue && prefillTo.HasValue)
        {
            FromDate     = new DateTimeOffset(prefillFrom.Value.ToDateTime(TimeOnly.MinValue));
            ToDate       = new DateTimeOffset(prefillTo.Value.ToDateTime(TimeOnly.MinValue));
            SkipWeekends = false;   // the days were picked explicitly
        }
        else
        {
            FromDate     = DateTimeOffset.Now;
            ToDate       = DateTimeOffset.Now;
            SkipWeekends = true;
        }

        await ReloadAsync();
        IsOpen = true;
    }

    public Task<List<LeaveDto>> GetCalendarLeavesAsync(int companyId, int employeeId, DateOnly from, DateOnly to) =>
        _leaveService.GetByEmployeeDateRangeAsync(companyId, employeeId, from, to);

    [RelayCommand]
    private void Close() => IsOpen = false;

    private async Task ReloadAsync()
    {
        if (_employee == null) return;

        var list = await _leaveService.GetByEmployeeAsync(_companyId, _employee.Id);

        Rows.Clear();
        foreach (var l in list)
            Rows.Add(new HolidayRow
            {
                Id                = l.Id,
                LeaveDate         = l.LeaveDate,
                LeaveTypeCode     = l.LeaveTypeCode,
                StartTime         = l.StartTime,
                EndTime           = l.EndTime,
                SubmittedToErgani = l.SubmittedToErgani,
                Protocol          = l.Protocol,
                SubmittedDate     = l.SubmittedDate,
                ResponseRawJson   = l.ResponseRawJson,
                LastError         = l.LastError
            });

        HasRows = Rows.Count > 0;
        OnPropertyChanged(nameof(UnsentCount));
        OnPropertyChanged(nameof(SubmitUnsentText));
    }

    // ── Save / submit ─────────────────────────────────────────────────────────

    [RelayCommand]
    private Task SaveAsync() => SaveCoreAsync(submit: false);

    [RelayCommand]
    private Task SaveAndSubmitAsync() => SaveCoreAsync(submit: true);

    private async Task SaveCoreAsync(bool submit)
    {
        if (_employee == null) return;

        if (SelectedType == null)   { StatusMessage = "❌ Select the holiday type."; return; }
        if (SelectedBranch == null) { StatusMessage = "❌ Select the branch."; return; }
        if (FromDate == null || ToDate == null) { StatusMessage = "❌ Enter the from and to dates (dd/mm/yyyy)."; return; }

        int? year = null;
        if (!string.IsNullOrWhiteSpace(ReferenceYear))
        {
            if (!int.TryParse(ReferenceYear.Trim(), out var y) || y < 1900 || y > 2100)
            { StatusMessage = "❌ The reference year must be a 4-digit year."; return; }
            year = y;
        }

        int? entitled = null;
        if (!string.IsNullOrWhiteSpace(EntitledDays))
        {
            if (!int.TryParse(EntitledDays.Trim(), out var d) || d < 0 || d > 999)
            { StatusMessage = "❌ Entitled days must be a number from 0 to 999."; return; }
            entitled = d;
        }

        if (Comments.Length > 200) { StatusMessage = "❌ Comments can be at most 200 characters."; return; }

        try
        {
            var template = new LeaveDto
            {
                EmployeeId    = _employee.Id,
                BranchId      = SelectedBranch.Id,
                LeaveTypeCode = SelectedType.Code,
                StartTime     = SelectedType.IsHourly && StartTime.HasValue ? TimeOnly.FromTimeSpan(StartTime.Value) : null,
                EndTime       = SelectedType.IsHourly && EndTime.HasValue   ? TimeOnly.FromTimeSpan(EndTime.Value)   : null,
                ReferenceYear = year,
                EntitledDays  = entitled,
                Comments      = Comments
            };

            var from = DateOnly.FromDateTime(FromDate.Value.LocalDateTime);
            var to   = DateOnly.FromDateTime(ToDate.Value.LocalDateTime);

            var created = await _leaveService.CreateRangeAsync(template, from, to, SkipWeekends);
            await ReloadAsync();
            HolidaysChanged?.Invoke(this, EventArgs.Empty);

            var dupNote = created.Duplicates > 0 ? $" ({created.Duplicates} day(s) already existed)" : string.Empty;

            if (created.Created == 0)
            {
                StatusMessage = created.Duplicates > 0
                    ? $"Nothing new to save — those days already exist.{dupNote}"
                    : "No days to save in that range (weekends are skipped).";
                return;
            }

            if (!submit)
            {
                StatusMessage = $"✅ Saved {created.Created} day(s). Use Submit to send them to Ergani.{dupNote}";
                return;
            }

            await SubmitIdsAsync(created.CreatedIds);
        }
        catch (Exception ex) { StatusMessage = $"❌ {ex.Message}"; }
    }

    [RelayCommand]
    private async Task SubmitUnsentAsync()
    {
        var ids = Rows.Where(r => r.IsUnsent).Select(r => r.Id).ToList();
        if (ids.Count == 0) { StatusMessage = "There are no unsent holidays."; return; }
        await SubmitIdsAsync(ids);
    }

    private async Task SubmitIdsAsync(IReadOnlyList<int> ids)
    {
        try
        {
            StatusMessage = $"Submitting {ids.Count} holiday day(s) to Ergani…";
            var result = await _leaveSubmitter.SubmitAsync(_companyId, ids);
            await ReloadAsync();

            if (result.FailedCount == 0 && result.SubmittedCount > 0)
                StatusMessage = $"✅ Submitted {result.SubmittedCount} holiday day(s) to Ergani.";
            else if (result.SubmittedCount == 0)
                StatusMessage = $"❌ {result.ErrorMessage ?? "Nothing was submitted."}";
            else
                StatusMessage = $"❌ {result.SubmittedCount} day(s) sent, {result.FailedCount} NOT sent — {result.ErrorMessage}";
        }
        catch (Exception ex) { StatusMessage = $"❌ {ex.Message}"; }
    }

    // ── List actions ──────────────────────────────────────────────────────────

    [RelayCommand]
    private async Task DeleteRowAsync(HolidayRow row)
    {
        try
        {
            if (!await _leaveService.DeleteUnsentAsync(row.Id))
            { StatusMessage = "A holiday that was already sent to Ergani cannot be deleted here."; return; }

            await ReloadAsync();
            StatusMessage = $"Deleted the holiday of {row.DateText}.";
        }
        catch (Exception ex) { StatusMessage = $"❌ {ex.Message}"; }
    }

    [RelayCommand]
    private void ShowResponse(HolidayRow row)
    {
        // A failure with no HTTP response (e.g. no connection) has only the error text.
        var text = !string.IsNullOrWhiteSpace(row.ResponseRawJson) ? row.ResponseRawJson : row.LastError;
        if (string.IsNullOrWhiteSpace(text))
        { StatusMessage = "This holiday has not been submitted yet — there is no response."; return; }

        ResponseTitle  = $"Ergani response — {row.DateText}";
        ResponseText   = text;
        IsResponseOpen = true;
    }

    [RelayCommand]
    private void CloseResponse() => IsResponseOpen = false;

    [RelayCommand]
    private async Task DownloadPdfAsync(HolidayRow row)
    {
        if (!row.HasProtocol)
        { StatusMessage = "A protocol is required before downloading the PDF."; return; }
        try
        {
            var bytes = await _documentService.DownloadPdfAsync(
                _companyId, "Documents/WTOLeave", row.Protocol!, row.SubmittedDate!.Value);

            var folder   = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            var who      = _employee?.FullName ?? "employee";
            var safeName = string.Join("_", who.Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries));
            var path     = Path.Combine(folder, $"Ergani_Holiday_{safeName}_{row.LeaveDate:yyyyMMdd}_{row.Protocol}.pdf");

            await File.WriteAllBytesAsync(path, bytes);
            StatusMessage = $"✅ PDF saved to Desktop: {Path.GetFileName(path)}";
        }
        catch (Exception ex) { StatusMessage = $"❌ PDF download failed: {ex.Message}"; }
    }
}
