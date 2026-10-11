using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ErganiManager.Core.Interfaces;
using ErganiManager.Core.Models;
using ErganiManager.Data;
using ErganiManager.ErganiApi.Services;
using ErganiManager.LocalCache;
using Microsoft.EntityFrameworkCore;

namespace ErganiManager.UI.ViewModels;

public partial class SubmissionLogRow : ObservableObject
{
    public int Id { get; set; }

    public string SubmissionType { get; set; } = string.Empty;

    public DateTime SubmissionDate { get; set; }

    public bool Success { get; set; }

    public int? HttpStatusCode { get; set; }

    public string? Protocol { get; set; }

    public string? ErrorMessage { get; set; }

    public long DurationMs { get; set; }

    public string? RequestPayloadJson { get; set; }

    public string? ResponseRawJson { get; set; }

    public int RetryCount { get; set; }

    public DateTime? LastRetryAt { get; set; }

    /// <summary>Tick box state for "Retry selected".</summary>
    [ObservableProperty]
    private bool _isSelected;

    /// <summary>Only failed work card uploads can be retried.</summary>
    public bool CanRetry => !Success && SubmissionType == "WorkCard";

    public bool HasRetries => RetryCount > 0;

    public string RetryText => $"↻ {RetryCount}";

    public string LastRetryText =>
        LastRetryAt?.ToLocalTime().ToString("dd/MM/yyyy HH:mm:ss") ?? string.Empty;

    public string RetrySummary =>
        HasRetries ? $"Retries: {RetryCount}  •  last: {LastRetryText}" : string.Empty;

    public string StatusIcon => Success ? "✅" : "❌";

    public string DurationText => $"{DurationMs} ms";

    public string DateText =>
        SubmissionDate.ToString("dd/MM/yyyy HH:mm:ss");
}

public partial class FailedSubmissionRow : ObservableObject
{
    public int Id { get; set; }

    public string EmployeeId { get; set; } = string.Empty;

    public string MovementType { get; set; } = string.Empty;

    public DateTime OriginalScannedAt { get; set; }

    [ObservableProperty] private string _failureReason = string.Empty;
    [ObservableProperty] private string? _errorDescription;
    [ObservableProperty] private int _retryCount;
    [ObservableProperty] private DateTime? _lastRetryAt;
    [ObservableProperty] private string? _lastRetryError;
    [ObservableProperty] private string _statusText = "Waiting to send";
    [ObservableProperty] private string? _protocol;
    [ObservableProperty] private bool _isPending;

    public string ScanTimeText =>
        OriginalScannedAt.ToString("dd/MM/yyyy HH:mm:ss");

    public string LastRetryText =>
        LastRetryAt?.ToString("dd/MM/yyyy HH:mm") ?? "Never";

    public string MovementIcon =>
        MovementType == "Arrival" ? "🟢" : "🔴";
}

public partial class SubmissionLogViewModel
    : ViewModelBase, IAdminSectionViewModel
{
    private readonly IConnectionStateService _connectionState;
    private readonly ErganiRetryService _retryService;
    private readonly WorkCardLogRetryService _logRetryService;

    private UserSession? _session;

    public ObservableCollection<SubmissionLogRow> Rows { get; } = new();

    public ObservableCollection<FailedSubmissionRow> FailedSubmissions { get; } = new();

    [ObservableProperty]
    private bool _hasActiveCompany;

    [ObservableProperty]
    private string _noCompanyMessage = string.Empty;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RetrySelectedCommand))]
    private bool _isRetrying;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RetrySelectedCommand))]
    [NotifyPropertyChangedFor(nameof(RetrySelectedText))]
    private int _selectedRetryCount;

    /// <summary>
    /// f_aitiologia codes offered for retries made MORE than 10 minutes after the first
    /// attempt. Within 10 minutes the field is always sent empty.
    /// </summary>
    public ObservableCollection<LocalizedCodeOption> LateJustificationOptions { get; }

    [ObservableProperty]
    private string _selectedLateJustification = WorkCardLogRetryService.DefaultLateJustification;

    public string RetrySelectedText => $"{Loc[L.RetrySelected]} ({SelectedRetryCount})";

    [ObservableProperty]
    private string _failedCountText = string.Empty;

    /*
     * Avalonia DatePicker uses DateTimeOffset?.
     *
     * Keep the ViewModel type as DateTimeOffset? so the binding
     * does not require a converter.
     */
    [ObservableProperty]
    private DateTimeOffset? _filterFromDate =
        new DateTimeOffset(DateTime.Today.AddDays(-7));

    [ObservableProperty]
    private DateTimeOffset? _filterToDate =
        new DateTimeOffset(DateTime.Today);

    [ObservableProperty]
    private bool _filterFailuresOnly;

    [ObservableProperty]
    private SubmissionLogRow? _selectedRow;

    public SubmissionLogViewModel(
        IConnectionStateService connectionState,
        ErganiRetryService retryService,
        WorkCardLogRetryService logRetryService)
    {
        _connectionState = connectionState;
        _retryService = retryService;
        _logRetryService = logRetryService;
        LateJustificationOptions = AitiologiaOptions.Create(Loc);
    }

    public void Initialize(UserSession session)
    {
        _session = session;

        _retryService.QueuedScanUpdated -= OnQueuedScanUpdated;
        _retryService.QueuedScanUpdated += OnQueuedScanUpdated;
        _logRetryService.RetryUpdated -= OnLogRetryUpdated;
        _logRetryService.RetryUpdated += OnLogRetryUpdated;

        HasActiveCompany = session.CompanyId.HasValue;

        NoCompanyMessage = session.CompanyId.HasValue
            ? string.Empty
            : Loc[L.NavCompanies];

        if (HasActiveCompany)
        {
            _ = LoadAsync();
            _ = LoadFailedSubmissionsAsync();
        }
    }

    [RelayCommand]
    private async Task LoadFailedSubmissionsAsync()
    {
        if (_session?.CompanyId is not int companyId)
            return;

        using var cache = LocalCacheDbContextFactory.Create();

        var items = await cache.FailedSubmissions
            .Where(f =>
                f.CompanyId == companyId &&
                !f.Resolved)
            .OrderBy(f => f.OriginalScannedAt)
            .ToListAsync();

        FailedSubmissions.Clear();

        foreach (var f in items)
        {
            FailedSubmissions.Add(new FailedSubmissionRow
            {
                Id = f.Id,
                EmployeeId = f.EmployeeId.ToString(),
                MovementType = f.MovementType,
                OriginalScannedAt = f.OriginalScannedAt,
                FailureReason = f.FailureReason,
                ErrorDescription = f.ErrorDescription,
                RetryCount = f.RetryCount,
                LastRetryAt = f.LastRetryAt,
                LastRetryError = f.LastRetryError,
                StatusText = f.Resolved ? "Resolved" : "Failed — retry pending",
                IsPending = false
            });
        }

        // Scans saved locally and still waiting for the background sender (e.g. no connection).
        var waiting = await cache.PendingSubmissions
            .Where(p => p.CompanyId == companyId && !p.Synced)
            .OrderBy(p => p.ScannedAt)
            .ToListAsync();

        foreach (var p in waiting)
        {
            FailedSubmissions.Add(new FailedSubmissionRow
            {
                Id = p.Id,
                EmployeeId = p.EmployeeId.ToString(),
                MovementType = p.MovementType,
                OriginalScannedAt = p.ScannedAt,
                FailureReason = "Waiting to send",
                ErrorDescription = p.LastSyncError,
                RetryCount = p.SyncAttempts,
                StatusText = string.IsNullOrWhiteSpace(p.LastSyncError) ? "Waiting to send" : "Retry waiting",
                IsPending = true
            });
        }

        FailedCountText = FailedSubmissions.Count == 0
            ? Loc[L.NoFailedPending]
            : string.Format(
                Loc[L.PendingRetry],
                FailedSubmissions.Count);
    }

    private void OnQueuedScanUpdated(object? sender, QueuedScanUpdate update)
    {
        if (_session?.CompanyId is null) return;

        Dispatcher.UIThread.Post(async () =>
        {
            var row = FailedSubmissions.FirstOrDefault(r => r.IsPending && r.Id == update.PendingId);

            if (update.Status == QueuedScanStatus.Retrying)
            {
                if (row != null)
                {
                    row.StatusText = $"Retrying... (attempt {Math.Max(update.Attempts, 1)})";
                    row.RetryCount = update.Attempts;
                    row.ErrorDescription = update.Error;
                }
                StatusMessage = $"Retrying queued work card #{update.PendingId}...";
                return;
            }

            if (row != null)
            {
                row.RetryCount = update.Attempts;
                row.ErrorDescription = update.Error;
                row.Protocol = update.Protocol;
                row.StatusText = update.Status switch
                {
                    QueuedScanStatus.Sent => string.IsNullOrWhiteSpace(update.Protocol)
                        ? "Successfully sent"
                        : $"Successfully sent — Protocol: {update.Protocol}",
                    QueuedScanStatus.Rejected => "Rejected by Ergani",
                    _ => "Waiting to send"
                };
            }

            if (update.Status == QueuedScanStatus.Sent || update.Status == QueuedScanStatus.Rejected)
            {
                // The main ApiSubmissionLog is created/updated by the submitter. Reload both
                // sections so this page immediately agrees with WorkCard History.
                await LoadAsync();
                await LoadFailedSubmissionsAsync();
            }
            else
            {
                FailedCountText = FailedSubmissions.Count == 0
                    ? Loc[L.NoFailedPending]
                    : string.Format(Loc[L.PendingRetry], FailedSubmissions.Count);
            }
        });
    }

    private void OnLogRetryUpdated(object? sender, LogRetryUpdate update)
    {
        if (_session?.CompanyId is null) return;

        Dispatcher.UIThread.Post(async () =>
        {
            StatusMessage = update.Status switch
            {
                LogRetryStatus.Succeeded => string.IsNullOrWhiteSpace(update.Protocol)
                    ? $"Successfully sent API log entry #{update.LogId}."
                    : $"Successfully sent API log entry #{update.LogId} — Protocol: {update.Protocol}",
                LogRetryStatus.Failed when update.Message == "Retrying..."
                    => $"Retrying API log entry #{update.LogId}...",
                LogRetryStatus.Failed => $"Retry failed for API log entry #{update.LogId}: {update.Message}",
                _ => $"Retry skipped for API log entry #{update.LogId}: {update.Message}"
            };

            await LoadAsync();
            await LoadFailedSubmissionsAsync();
        });
    }

    [RelayCommand]
    private async Task ManualRetryNowAsync()
    {
        if (_session?.CompanyId is null)
            return;

        IsRetrying = true;
        StatusMessage = "Triggering manual retry...";

        try
        {
            _retryService.Start();      // no-op when already running
            _retryService.TriggerNow(); // send queued scans right now

            // The retry service now publishes state changes. Do one immediate refresh, then
            // let the event handler keep this page synchronized while the sender runs.
            await LoadAsync();
            await LoadFailedSubmissionsAsync();

            StatusMessage =
                Loc[L.SuccessPrefix] +
                "Manual retry triggered.";
        }
        catch (Exception ex)
        {
            StatusMessage =
                Loc[L.ErrorPrefix] +
                ex.Message;
        }
        finally
        {
            IsRetrying = false;
        }
    }

    // ── Retry selected failed work cards ─────────────────────────────────

    [RelayCommand]
    private void SelectAllFailed()
    {
        var retryable = Rows.Where(r => r.CanRetry).ToList();
        if (retryable.Count == 0) return;

        // Toggle: if anything is unticked tick everything, otherwise clear all.
        var tickAll = retryable.Any(r => !r.IsSelected);
        foreach (var row in retryable)
            row.IsSelected = tickAll;
    }

    private bool CanRetrySelected() => SelectedRetryCount > 0 && !IsRetrying;

    [RelayCommand(CanExecute = nameof(CanRetrySelected))]
    private async Task RetrySelectedAsync()
    {
        var ids = Rows.Where(r => r.IsSelected && r.CanRetry).Select(r => r.Id).ToList();
        if (ids.Count == 0) return;

        var keepSelectedId = SelectedRow?.Id;

        IsRetrying = true;
        StatusMessage = $"Retrying {ids.Count} work card(s)...";

        try
        {
            var results = await _logRetryService.RetryAsync(ids, SelectedLateJustification);

            var ok      = results.Count(r => r.Status == LogRetryStatus.Succeeded);
            var failed  = results.Count(r => r.Status == LogRetryStatus.Failed);
            var skipped = results.Count(r => r.Status == LogRetryStatus.Skipped);

            // Rows were updated in place in the database — reload to show the new state.
            await LoadAsync();
            SelectedRow = Rows.FirstOrDefault(r => r.Id == keepSelectedId);

            StatusMessage = $"Retry finished: {ok} succeeded, {failed} failed" +
                            (skipped > 0 ? $", {skipped} skipped." : ".");
        }
        catch (Exception ex)
        {
            StatusMessage = Loc[L.ErrorPrefix] + ex.Message;
        }
        finally
        {
            IsRetrying = false;
        }
    }

    private void OnRowPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SubmissionLogRow.IsSelected))
            UpdateSelectedRetryCount();
    }

    private void UpdateSelectedRetryCount() =>
        SelectedRetryCount = Rows.Count(r => r.IsSelected);

    [RelayCommand]
    private async Task LoadAsync()
    {
        if (_session?.CompanyId is not int companyId)
            return;

        IsLoading = true;
        StatusMessage = string.Empty;

        try
        {
            await using var db =
                new AppDbContext(_connectionState.GetDbOptions());

            /*
             * DatePicker gives us DateTimeOffset?.
             *
             * We only want the calendar date selected by the user.
             * Do NOT convert this to UTC.
             */
            var fromDate =
                (FilterFromDate ?? DateTimeOffset.Now).Date;

            var toDate =
                (FilterToDate ?? DateTimeOffset.Now).Date;

            /*
             * If the dates are reversed, use the same day as a
             * safe fallback rather than producing an empty result.
             */
            if (toDate < fromDate)
            {
                toDate = fromDate;
            }

            /*
             * End date is exclusive.
             *
             * Example:
             *
             * From = 01/10/2026
             * To   = 01/10/2026
             *
             * SQL range:
             *
             * >= 01/10/2026 00:00:00
             * <  02/10/2026 00:00:00
             *
             * This includes the complete selected day.
             */
            var toDateExclusive = toDate.AddDays(1);

            var query = db.ApiSubmissionLogs
                .Where(l =>
                    l.CompanyId == companyId &&
                    l.SubmissionDate >= fromDate &&
                    l.SubmissionDate < toDateExclusive);

            if (FilterFailuresOnly)
            {
                query = query.Where(l => !l.Success);
            }

            var results = await query
                .OrderByDescending(l => l.SubmissionDate)
                .Take(500)
                .ToListAsync();

            Rows.Clear();

            foreach (var r in results)
            {
                var row = new SubmissionLogRow
                {
                    Id = r.Id,
                    SubmissionType = r.SubmissionType,
                    SubmissionDate = r.SubmissionDate,
                    Success = r.Success,
                    HttpStatusCode = r.HttpStatusCode,
                    Protocol = r.Protocol,
                    ErrorMessage = r.ErrorMessage,
                    DurationMs = r.DurationMs,
                    RequestPayloadJson = r.RequestPayloadJson,
                    ResponseRawJson = r.ResponseRawJson,
                    RetryCount = r.RetryCount,
                    LastRetryAt = r.LastRetryAt
                };

                row.PropertyChanged += OnRowPropertyChanged;
                Rows.Add(row);
            }

            UpdateSelectedRetryCount();

            StatusMessage = results.Count == 500
                ? "Showing latest 500 — narrow date range for more."
                : $"{results.Count} log entries.";
        }
        catch (Exception ex)
        {
            StatusMessage =
                Loc[L.ErrorPrefix] +
                ex.Message;
        }
        finally
        {
            IsLoading = false;
        }
    }
}

