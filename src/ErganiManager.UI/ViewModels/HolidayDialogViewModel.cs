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
    public int BranchId { get; init; }
    public DateOnly LeaveDate { get; init; }
    public string LeaveTypeCode { get; init; } = string.Empty;
    public TimeOnly? StartTime { get; init; }
    public TimeOnly? EndTime { get; init; }
    public TimeOnly? DeclaredStartTime { get; init; }
    public TimeOnly? DeclaredEndTime { get; init; }
    public bool SubmittedToErgani { get; init; }
    public string? Protocol { get; init; }
    public DateOnly? SubmittedDate { get; init; }
    public string? ResponseRawJson { get; init; }
    public string? LastError { get; init; }
    public int? ReferenceYear { get; init; }
    public int? EntitledDays { get; init; }
    public string? Comments { get; init; }

    public string StatusIcon => SubmittedToErgani ? "✅" : (string.IsNullOrWhiteSpace(LastError) ? "🕓" : "❌");
    public string DateText => LeaveDate.ToString("dd/MM/yyyy");
    public string TypeText => LeaveTypes.Label(LeaveTypeCode);
    public string HoursText => StartTime.HasValue && EndTime.HasValue
        ? $"{StartTime:HH:mm}–{EndTime:HH:mm}"
        : Tr.Loc["Vm.Whole day"];
    public bool IsUnsent => !SubmittedToErgani;
    public bool HasProtocol => SubmittedToErgani && !string.IsNullOrWhiteSpace(Protocol) && SubmittedDate.HasValue;
    public string Summary => SubmittedToErgani
        ? (string.IsNullOrWhiteSpace(Protocol) ? Tr.Loc["Vm.Accepted by Ergani"] : string.Format(Tr.Loc["Vm.Protocol: {0}"], Protocol))
        : (string.IsNullOrWhiteSpace(LastError) ? Tr.Loc["Vm.Not submitted yet"] : LastError!);
}

/// <summary>The holidays form: pick a holiday type and dates, save, submit to Ergani, and review
/// what was sent (response + PDF) — the same flow as overtime, opened from the Schedules page.</summary>
public partial class HolidayDialogViewModel : ViewModelBase
{
    /// <summary>Raised after holiday records have been changed so the schedule calendar can refresh.</summary>
    public event EventHandler? HolidaysChanged;

    private readonly ILeaveService _leaveService;
    private readonly ILeaveSubmitter _leaveSubmitter;
    private readonly IErganiDocumentService _documentService;

    private int _companyId;
    private EmployeeDto? _employee;
    private int? _editingLeaveId;

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
    [ObservableProperty] private TimeSpan? _declaredStartTime = new TimeSpan(9, 0, 0);
    [ObservableProperty] private TimeSpan? _declaredEndTime = new TimeSpan(17, 0, 0);
    [ObservableProperty] private string _referenceYear = string.Empty;
    [ObservableProperty] private string _entitledDays = string.Empty;
    [ObservableProperty] private bool _isEditingExisting;
    [ObservableProperty] private string _comments = string.Empty;

    [ObservableProperty] private string _statusMessage = string.Empty;
    [ObservableProperty] private bool _hasRows;

    [ObservableProperty] private bool _isResponseOpen;
    [ObservableProperty] private string _responseTitle = string.Empty;
    [ObservableProperty] private string _responseText = string.Empty;

    public bool IsHourlyType => SelectedType?.IsHourly == true;
    public int UnsentCount => Rows.Count(r => r.IsUnsent);
    public string SubmitUnsentText => string.Format(Loc["Vm.📤 Submit unsent ({0})"], UnsentCount);

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

        Title          = string.Format(Loc["Vm.🏖 Holidays — {0}"], employee.FullName);
        SelectedType   = null;
        _editingLeaveId = null;
        IsEditingExisting = false;
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
                BranchId          = l.BranchId,
                LeaveDate         = l.LeaveDate,
                LeaveTypeCode     = l.LeaveTypeCode,
                StartTime         = l.StartTime,
                EndTime           = l.EndTime,
                DeclaredStartTime  = l.DeclaredStartTime,
                DeclaredEndTime    = l.DeclaredEndTime,
                SubmittedToErgani = l.SubmittedToErgani,
                Protocol          = l.Protocol,
                SubmittedDate     = l.SubmittedDate,
                ResponseRawJson   = l.ResponseRawJson,
                LastError         = l.LastError,
                ReferenceYear     = l.ReferenceYear,
                EntitledDays      = l.EntitledDays,
                Comments          = l.Comments
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

        if (SelectedType == null)   { StatusMessage = Loc["Vm.❌ Select the holiday type."]; return; }
        if (SelectedBranch == null) { StatusMessage = Loc["Vm.❌ Select the branch."]; return; }
        if (FromDate == null || ToDate == null) { StatusMessage = Loc["Vm.❌ Enter the from and to dates (dd/mm/yyyy)."]; return; }

        if (string.IsNullOrWhiteSpace(ReferenceYear) ||
            !int.TryParse(ReferenceYear.Trim(), out var y) || y < 1900 || y > 2100)
        { StatusMessage = Loc["Vm.❌ The reference year is required and must be a 4-digit year."]; return; }
        int? year = y;

        if (string.IsNullOrWhiteSpace(EntitledDays) ||
            !int.TryParse(EntitledDays.Trim(), out var d) || d < 0 || d > 999)
        { StatusMessage = Loc["Vm.❌ Entitled leave days are required and must be a number from 0 to 999."]; return; }
        int? entitled = d;

        if (Comments.Length > 200) { StatusMessage = Loc["Vm.❌ Comments can be at most 200 characters."]; return; }

        try
        {
            var from = DateOnly.FromDateTime(FromDate.Value.LocalDateTime);
            var to   = DateOnly.FromDateTime(ToDate.Value.LocalDateTime);

            var template = new LeaveDto
            {
                Id            = _editingLeaveId ?? 0,
                EmployeeId    = _employee.Id,
                BranchId      = SelectedBranch.Id,
                LeaveDate     = from,
                LeaveTypeCode = SelectedType.Code,
                StartTime     = SelectedType.IsHourly && StartTime.HasValue ? TimeOnly.FromTimeSpan(StartTime.Value) : null,
                EndTime       = SelectedType.IsHourly && EndTime.HasValue   ? TimeOnly.FromTimeSpan(EndTime.Value)   : null,
                DeclaredStartTime = DeclaredStartTime.HasValue ? TimeOnly.FromTimeSpan(DeclaredStartTime.Value) : null,
                DeclaredEndTime = DeclaredEndTime.HasValue ? TimeOnly.FromTimeSpan(DeclaredEndTime.Value) : null,
                ReferenceYear = year,
                EntitledDays  = entitled,
                Comments      = Comments
            };

            if (_editingLeaveId.HasValue)
            {
                if (from != to)
                { StatusMessage = Loc["Vm.When editing a holiday, the from and to dates must be the same day."]; return; }

                var editedId = _editingLeaveId.Value;
                var updated = await _leaveService.UpdateUnsentAsync(_companyId, template);
                if (!updated)
                { StatusMessage = Loc["Vm.❌ Only holidays not yet submitted to Ergani can be edited."]; return; }

                _editingLeaveId = null;
                IsEditingExisting = false;
                await ReloadAsync();
                HolidaysChanged?.Invoke(this, EventArgs.Empty);

                if (!submit)
                {
                    StatusMessage = Loc["Vm.✅ Holiday updated. You can now submit it to Ergani."];
                    return;
                }

                await SubmitIdsAsync(new[] { editedId });
                return;
            }

            var created = await _leaveService.CreateRangeAsync(template, from, to, SkipWeekends);
            await ReloadAsync();
            HolidaysChanged?.Invoke(this, EventArgs.Empty);

            var dupNote = created.Duplicates > 0 ? string.Format(Loc["Vm. ({0} day(s) already existed)"], created.Duplicates) : string.Empty;

            if (created.Created == 0)
            {
                StatusMessage = created.Duplicates > 0
                    ? Loc["Vm.Nothing new to save — those days already exist."] + dupNote
                    : Loc["Vm.No days to save in that range (weekends are skipped)."];
                return;
            }

            if (!submit)
            {
                StatusMessage = string.Format(Loc["Vm.✅ Saved {0} day(s). Use Submit to send them to Ergani."], created.Created) + dupNote;
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
        if (ids.Count == 0) { StatusMessage = Loc["Vm.There are no unsent holidays."]; return; }
        await SubmitIdsAsync(ids);
    }

    private async Task SubmitIdsAsync(IReadOnlyList<int> ids)
    {
        try
        {
            StatusMessage = string.Format(Loc["Vm.Submitting {0} holiday day(s) to Ergani…"], ids.Count);
            var result = await _leaveSubmitter.SubmitAsync(_companyId, ids);
            await ReloadAsync();

            if (result.FailedCount == 0 && result.SubmittedCount > 0)
                StatusMessage = string.Format(Loc["Vm.✅ Submitted {0} holiday day(s) to Ergani."], result.SubmittedCount);
            else if (result.SubmittedCount == 0)
                StatusMessage = $"❌ {result.ErrorMessage ?? Loc["Vm.Nothing was submitted."]}";
            else
                StatusMessage = string.Format(Loc["Vm.❌ {0} day(s) sent, {1} NOT sent — {2}"], result.SubmittedCount, result.FailedCount, result.ErrorMessage);
        }
        catch (Exception ex) { StatusMessage = $"❌ {ex.Message}"; }
    }

    // ── List actions ──────────────────────────────────────────────────────────

    [RelayCommand]
    private void EditRow(HolidayRow row)
    {
        if (_employee == null || !row.IsUnsent)
        {
            StatusMessage = Loc["Vm.Only holidays not yet submitted to Ergani can be edited."];
            return;
        }

        _editingLeaveId = row.Id;
        IsEditingExisting = true;
        SelectedType = LeaveTypes.Find(row.LeaveTypeCode);
        SelectedBranch = Branches.FirstOrDefault(b => b.Id == row.BranchId);
        FromDate = new DateTimeOffset(row.LeaveDate.ToDateTime(TimeOnly.MinValue));
        ToDate = FromDate;
        SkipWeekends = false;
        StartTime = row.StartTime?.ToTimeSpan() ?? new TimeSpan(9, 0, 0);
        EndTime = row.EndTime?.ToTimeSpan() ?? new TimeSpan(13, 0, 0);
        DeclaredStartTime = row.DeclaredStartTime?.ToTimeSpan() ?? new TimeSpan(9, 0, 0);
        DeclaredEndTime = row.DeclaredEndTime?.ToTimeSpan() ?? new TimeSpan(17, 0, 0);
        ReferenceYear = row.ReferenceYear?.ToString() ?? string.Empty;
        EntitledDays = row.EntitledDays?.ToString() ?? string.Empty;
        Comments = row.Comments ?? string.Empty;
        StatusMessage = Loc["Vm.Editing holiday — update the required values and save."];
    }

    [RelayCommand]
    private void CancelEdit()
    {
        _editingLeaveId = null;
        IsEditingExisting = false;
        ReferenceYear = string.Empty;
        EntitledDays = string.Empty;
        Comments = string.Empty;
        StatusMessage = Loc["Vm.Edit cancelled."];
    }

    [RelayCommand]
    private async Task DeleteRowAsync(HolidayRow row)
    {
        try
        {
            if (!await _leaveService.DeleteUnsentAsync(row.Id))
            { StatusMessage = Loc["Vm.A holiday that was already sent to Ergani cannot be deleted here."]; return; }

            await ReloadAsync();
            StatusMessage = string.Format(Loc["Vm.Deleted the holiday of {0}."], row.DateText);
        }
        catch (Exception ex) { StatusMessage = $"❌ {ex.Message}"; }
    }

    [RelayCommand]
    private void ShowResponse(HolidayRow row)
    {
        // A failure with no HTTP response (e.g. no connection) has only the error text.
        var text = !string.IsNullOrWhiteSpace(row.ResponseRawJson) ? row.ResponseRawJson : row.LastError;
        if (string.IsNullOrWhiteSpace(text))
        { StatusMessage = Loc["Vm.This holiday has not been submitted yet — there is no response."]; return; }

        ResponseTitle  = string.Format(Loc["Vm.Ergani response — {0}"], row.DateText);
        ResponseText   = text;
        IsResponseOpen = true;
    }

    [RelayCommand]
    private void CloseResponse() => IsResponseOpen = false;

    [RelayCommand]
    private async Task DownloadPdfAsync(HolidayRow row)
    {
        if (!row.HasProtocol)
        { StatusMessage = Loc["Vm.A protocol is required before downloading the PDF."]; return; }
        try
        {
            var bytes = await _documentService.DownloadPdfAsync(
                _companyId, "Documents/WTOLeave", row.Protocol!, row.SubmittedDate!.Value);

            var folder   = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            var who      = _employee?.FullName ?? "employee";
            var safeName = string.Join("_", who.Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries));
            var path     = Path.Combine(folder, $"Ergani_Holiday_{safeName}_{row.LeaveDate:yyyyMMdd}_{row.Protocol}.pdf");

            await File.WriteAllBytesAsync(path, bytes);
            StatusMessage = string.Format(Loc["Vm.✅ PDF saved to Desktop: {0}"], Path.GetFileName(path));
        }
        catch (Exception ex) { StatusMessage = string.Format(Loc["Vm.❌ PDF download failed: {0}"], ex.Message); }
    }
}
