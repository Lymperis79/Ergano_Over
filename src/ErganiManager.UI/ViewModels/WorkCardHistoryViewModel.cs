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
using ErganiManager.Data;
using ErganiManager.ErganiApi.Services;
using Microsoft.EntityFrameworkCore;

namespace ErganiManager.UI.ViewModels;

public partial class WorkCardHistoryViewModel : ViewModelBase, IAdminSectionViewModel
{
    private readonly IWorkCardHistoryService _historyService;
    private readonly IEmployeeService _employeeService;
    private readonly IBranchService _branchService;
    private readonly IErganiDocumentService _documentService;
    private readonly ErganiRetryService _retryService;
    private readonly WorkCardLogRetryService _logRetryService;
    private readonly IErganiPortalService _portalService;
    private readonly IConnectionStateService _connectionState;
    private UserSession? _session;

    public ObservableCollection<WorkCardHistoryDto> Records { get; } = new();

    public ObservableCollection<EmployeeDto> AvailableEmployees { get; } = new();

    public ObservableCollection<BranchDto> AvailableBranches { get; } = new();

    [ObservableProperty]
    private bool _hasActiveCompany;

    [ObservableProperty]
    private string _noCompanyMessage = string.Empty;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty] private bool _isResponseOpen;
    [ObservableProperty] private string _responseTitle = string.Empty;
    [ObservableProperty] private string _responseText = string.Empty;

    // --------------------------------------------------------------------
    // Filters
    //
    // Avalonia DatePicker.SelectedDate uses DateTimeOffset?.
    // Keep these properties as DateTimeOffset? so no converter is required.
    // --------------------------------------------------------------------

    [ObservableProperty]
    private DateTimeOffset? _filterFromDate =
        new DateTimeOffset(DateTime.Today.AddDays(-30));

    [ObservableProperty]
    private DateTimeOffset? _filterToDate =
        new DateTimeOffset(DateTime.Today);

    [ObservableProperty]
    private EmployeeDto? _filterEmployee;

    [ObservableProperty]
    private BranchDto? _filterBranch;

    [ObservableProperty]
    private bool _filterEarlyDepartureOnly;

    public WorkCardHistoryViewModel(
        IWorkCardHistoryService historyService,
        IEmployeeService employeeService,
        IBranchService branchService,
        IErganiDocumentService documentService,
        IConnectionStateService connectionState,
        ErganiRetryService retryService,
        WorkCardLogRetryService logRetryService,
        IErganiPortalService portalService)
    {
        _historyService = historyService;
        _employeeService = employeeService;
        _branchService = branchService;
        _documentService = documentService;
        _connectionState = connectionState;
        _retryService = retryService;
        _logRetryService = logRetryService;
        _portalService = portalService;
        _portalService.Progress += message =>
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() => StatusMessage = message);
        };
        _retryService.QueuedScanUpdated += OnQueuedScanUpdated;
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

    /// <summary>Opens the Ergani Work Cards page, logs in, fills today's date in both fields and searches.</summary>
    [RelayCommand]
    private async Task OpenErganiWorkCardsPortalAsync()
    {
        if (_session?.CompanyId is not int companyId)
        {
            StatusMessage = "Select a company first.";
            return;
        }

        StatusMessage = "Opening the Ergani Work Cards portal…";
        try
        {
            StatusMessage = await _portalService.OpenWorkCardsAsync(companyId, DateOnly.FromDateTime(DateTime.Today));
        }
        catch (Exception ex)
        {
            StatusMessage = $"❌ {ex.Message}";
        }
    }

    private async Task LoadFiltersAsync()
    {
        if (_session?.CompanyId is not int companyId)
            return;

        var employees =
            await _employeeService
                .GetByCompanyAsync(companyId, activeOnly: false)
                .ConfigureAwait(false);

        AvailableEmployees.Clear();

        AvailableEmployees.Add(
            new EmployeeDto
            {
                Id = 0,
                FirstName = "All",
                LastName = "Employees"
            });

        foreach (var employee in employees)
            AvailableEmployees.Add(employee);

        var branches =
            await _branchService
                .GetByCompanyAsync(companyId)
                .ConfigureAwait(false);

        AvailableBranches.Clear();

        AvailableBranches.Add(
            new BranchDto
            {
                Id = 0,
                Name = "All Branches"
            });

        foreach (var branch in branches)
            AvailableBranches.Add(branch);

        FilterEmployee = AvailableEmployees[0];
        FilterBranch = AvailableBranches[0];

        StatusMessage =
            "Set filters and press Load to view records.";

        // Do not automatically load records.
        // Records are loaded only when the user presses Load.
    }

    [RelayCommand]
    private void ExportToExcel()
    {
        if (!Records.Any())
        {
            StatusMessage =
                "No records to export — apply filters and load first.";

            return;
        }

        try
        {
            var folder =
                Environment.GetFolderPath(
                    Environment.SpecialFolder.Desktop);

            var filePath =
                ExcelImportExportService.ExportWorkCardHistory(
                    Records.ToList(),
                    folder);

            StatusMessage =
                $"✅ Exported to Desktop: {Path.GetFileName(filePath)}";
        }
        catch (Exception ex)
        {
            StatusMessage =
                $"❌ Export failed: {ex.Message}";
        }
    }

    [RelayCommand]
    private void ShowResponseFor(WorkCardHistoryDto record)
    {
        if (string.IsNullOrWhiteSpace(record.ResponseRawJson)) { StatusMessage = "No stored Ergani response is available for this record."; return; }
        ResponseTitle = $"Ergani Response — {record.EmployeeFullName}"; ResponseText = record.ResponseRawJson; IsResponseOpen = true;
    }

    [RelayCommand] private void CloseResponse() => IsResponseOpen = false;

    [RelayCommand]
    private async Task DownloadPdfAsync(WorkCardHistoryDto record)
    {
        if (_session?.CompanyId is not int companyId || string.IsNullOrWhiteSpace(record.Protocol)) { StatusMessage = "A protocol is required before downloading the PDF."; return; }
        try
        {
            var bytes = await _documentService.DownloadPdfAsync(companyId, "Documents/WRKCardSE", record.Protocol, record.SubmissionDate);
            var folder = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            var safeName = string.Join("_", record.EmployeeFullName.Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries));
            var path = Path.Combine(folder, $"Ergani_WorkCard_{safeName}_{record.MovementDateTime:yyyyMMdd_HHmmss}_{record.Protocol}.pdf");
            await File.WriteAllBytesAsync(path, bytes); StatusMessage = $"✅ PDF saved to Desktop: {Path.GetFileName(path)}";
        }
        catch (Exception ex) { StatusMessage = $"❌ PDF download failed: {ex.Message}"; }
    }


    [RelayCommand]
    private void SelectAllRetryable()
    {
        var retryable = Records.Where(r => r.CanRetry).ToList();
        if (retryable.Count == 0)
            return;

        var selectAll = retryable.Any(r => !r.IsSelected);
        foreach (var row in retryable)
            row.IsSelected = selectAll;

        StatusMessage = selectAll
            ? $"Selected {retryable.Count} retryable work card(s)."
            : "Retry selection cleared.";
    }

    [RelayCommand]
    private async Task RetrySelectedAsync()
    {
        if (_session?.CompanyId is not int companyId)
            return;

        var selected = Records.Where(r => r.IsSelected && r.CanRetry).ToList();
        if (selected.Count == 0)
        {
            StatusMessage = "Select one or more failed/waiting work cards first.";
            return;
        }

        StatusMessage = $"Retry sending {selected.Count} work card(s)...";
        var success = 0;
        var failed = 0;

        try
        {
            // Current offline scans live in PendingSubmissions. Retry exactly the
            // rows selected by the operator rather than waking the entire queue.
            var pendingIds = selected
                .Where(r => r.LocalPendingId.HasValue)
                .Select(r => r.LocalPendingId!.Value)
                .ToList();

            if (pendingIds.Count > 0)
            {
                var pendingResults = await _retryService.RetryPendingAsync(pendingIds);
                foreach (var result in pendingResults)
                {
                    if (result.Success)
                    {
                        success++;
                        StatusMessage = string.IsNullOrWhiteSpace(result.Protocol)
                            ? $"Successfully sent pending scan #{result.PendingId}."
                            : $"Successfully sent pending scan #{result.PendingId} — Protocol: {result.Protocol}";
                    }
                    else
                    {
                        failed++;
                        StatusMessage = $"Retry failed for pending scan #{result.PendingId}: {result.Error}";
                    }
                }
            }

            // WorkCards that exist but were not accepted by Ergani are retried using
            // the same in-place API-log retry mechanism used by Submission Log.
            var workCardIds = selected
                .Where(r => r.Id > 0 && !r.SubmittedToErgani && !r.IsLocalPending)
                .Select(r => r.Id)
                .ToList();

            if (workCardIds.Count > 0)
            {
                await using var db = new AppDbContext(_retryConnectionOptions());
                var logIds = await db.ApiSubmissionLogs
                    .Where(l => l.CompanyId == companyId &&
                                l.WorkCardId.HasValue &&
                                workCardIds.Contains(l.WorkCardId.Value) &&
                                l.SubmissionType == "WorkCard" &&
                                !l.Success)
                    .Select(l => l.Id)
                    .ToListAsync();

                if (logIds.Count > 0)
                {
                    var results = await _logRetryService.RetryAsync(logIds);
                    success += results.Count(r => r.Status == LogRetryStatus.Succeeded);
                    failed += results.Count(r => r.Status == LogRetryStatus.Failed);
                }
            }

            // Legacy FailedSubmission rows are still retried by the background worker.
            // Request an immediate run when any of those rows were selected.
            if (selected.Any(r => r.IsLocalFailed))
            {
                _retryService.Start();
                _retryService.TriggerNow();
                StatusMessage = "Retry sending requested for failed queued work cards...";
            }

            await LoadAsync();
            foreach (var row in Records)
                row.IsSelected = false;

            if (success > 0 || failed > 0)
                StatusMessage = $"Retry finished: {success} successfully sent, {failed} failed.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"❌ Retry failed: {ex.Message}";
        }
    }

    private DbContextOptions<AppDbContext> _retryConnectionOptions() => _historyDbOptions();

    private DbContextOptions<AppDbContext> _historyDbOptions()
    {
        // Use the same configured provider/options as the history service.
        // IConnectionStateService is intentionally not exposed by that service,
        // so resolve it from the current app connection state via the constructor below.
        return _connectionState.GetDbOptions();
    }

    private async Task RefreshAfterQueuedSendAsync(string employeeName, string? protocol)
    {
        await LoadAsync();
        StatusMessage = string.IsNullOrWhiteSpace(protocol)
            ? $"✅ Successfully sent {employeeName}."
            : $"✅ Successfully sent {employeeName} — Protocol: {protocol}";
    }

    private void OnQueuedScanUpdated(object? sender, QueuedScanUpdate update)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            var row = Records.FirstOrDefault(r => r.LocalPendingId == update.PendingId);
            if (row == null)
                return;

            row.RetryAttempts = update.Attempts;
            row.RetryError = update.Error;

            if (update.Status == QueuedScanStatus.Sent)
            {
                row.IsLocalPending = false;
                row.SubmittedToErgani = true;
                row.Protocol = update.Protocol;
                _ = RefreshAfterQueuedSendAsync(row.EmployeeFullName, update.Protocol);
            }
            else if (update.Status == QueuedScanStatus.Waiting)
            {
                StatusMessage = $"⏳ Retry sending {row.EmployeeFullName} — attempt {update.Attempts}" +
                                (string.IsNullOrWhiteSpace(update.Error) ? "..." : $": {update.Error}");
            }
            else
            {
                row.IsLocalPending = false;
                row.IsLocalFailed = true;
                StatusMessage = $"❌ Ergani rejected {row.EmployeeFullName}: {update.Error}";
            }
        });
    }

    [RelayCommand]
    private async Task LoadAsync()
    {
        if (_session?.CompanyId is not int companyId)
            return;

        IsLoading = true;
        StatusMessage = string.Empty;

        try
        {
            /*
             * DatePicker returns DateTimeOffset?.
             *
             * We are interested in the calendar date selected by
             * the user, not the UTC timestamp.
             */
            var fromDate =
                (FilterFromDate ?? DateTimeOffset.Now.AddDays(-30))
                .Date;

            var toDate =
                (FilterToDate ?? DateTimeOffset.Now)
                .Date;

            /*
             * Protect against an inverted date range.
             */
            if (toDate < fromDate)
            {
                toDate = fromDate;
            }

            var filter = new WorkCardHistoryFilter
            {
                FromDate = DateOnly.FromDateTime(fromDate),

                ToDate = DateOnly.FromDateTime(toDate),

                EmployeeId =
                    FilterEmployee?.Id == 0
                        ? null
                        : FilterEmployee?.Id,

                BranchId =
                    FilterBranch?.Id == 0
                        ? null
                        : FilterBranch?.Id,

                EarlyDepartureOnly =
                    FilterEarlyDepartureOnly
                        ? true
                        : null
            };

            var results =
                await _historyService
                    .GetAsync(companyId, filter)
                    .ConfigureAwait(false);

            await Avalonia.Threading.Dispatcher.UIThread
                .InvokeAsync(() =>
                {
                    Records.Clear();

                    foreach (var record in results)
                        Records.Add(record);
                });

            StatusMessage =
                results.Count == 1000
                    ? "Showing first 1,000 records — narrow the date range for more precision."
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