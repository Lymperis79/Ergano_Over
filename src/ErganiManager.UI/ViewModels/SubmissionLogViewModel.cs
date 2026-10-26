using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.IO;
using System.Text.Json;
using System.Threading;
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

    public string EmployeeNames { get; set; } = string.Empty;
    public bool HasPdf => Success && !string.IsNullOrWhiteSpace(Protocol);

    public int RetryCount { get; set; }

    public DateTime? LastRetryAt { get; set; }

    /// <summary>Tick box state for "Retry selected".</summary>
    [ObservableProperty]
    private bool _isSelected;

    /// <summary>Only failed work card uploads can be retried.</summary>
    public bool CanRetry =>
        !Success &&
        SubmissionType == "WorkCard" &&
        !string.Equals(
            ErrorMessage,
            "Waiting for internet connection; saved locally and queued for automatic submission.",
            StringComparison.Ordinal);

    public bool HasRetries => RetryCount > 0;

    public string RetryText => $"↻ {RetryCount}";

    public string LastRetryText =>
        LastRetryAt?.ToLocalTime().ToString("dd/MM/yyyy HH:mm:ss") ?? string.Empty;

    public string RetrySummary =>
        HasRetries ? string.Format(Tr.Loc["Vm.Retries: {0}  •  last: {1}"], RetryCount, LastRetryText) : string.Empty;

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
    [ObservableProperty] private string _statusText = Tr.Loc["Vm.Waiting to send"];
    [ObservableProperty] private string? _protocol;
    [ObservableProperty] private bool _isPending;

    public string ScanTimeText =>
        OriginalScannedAt.ToString("dd/MM/yyyy HH:mm:ss");

    public string LastRetryText =>
        LastRetryAt?.ToString("dd/MM/yyyy HH:mm") ?? Tr.Loc["Vm.Never"];

    public string MovementIcon =>
        MovementType == "Arrival" ? "🟢" : "🔴";
}

public partial class SubmissionLogViewModel
    : ViewModelBase, IAdminSectionViewModel
{
    private readonly IConnectionStateService _connectionState;
    private readonly ErganiRetryService _retryService;
    private readonly WorkCardLogRetryService _logRetryService;
    private readonly IErganiDocumentService _documentService;

    private UserSession? _session;

    public ObservableCollection<SubmissionLogRow> Rows { get; } = new();
    public ObservableCollection<EmployeeFilterOption> EmployeeOptions { get; } = new();

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
    private int? _selectedEmployeeId = 0;

    [ObservableProperty]
    private SubmissionLogRow? _selectedRow;

    public SubmissionLogViewModel(
        IConnectionStateService connectionState,
        ErganiRetryService retryService,
        WorkCardLogRetryService logRetryService,
        IErganiDocumentService documentService)
    {
        _connectionState = connectionState;
        _retryService = retryService;
        _logRetryService = logRetryService;
        _documentService = documentService;
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
                StatusText = f.Resolved ? Loc["Vm.Resolved"] : Loc["Vm.Failed — retry pending"],
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
                FailureReason = Loc["Vm.Waiting to send"],
                ErrorDescription = p.LastSyncError,
                RetryCount = p.SyncAttempts,
                StatusText = string.IsNullOrWhiteSpace(p.LastSyncError) ? Loc["Vm.Waiting to send"] : Loc["Vm.Retry waiting"],
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
                    row.StatusText = string.Format(Loc["Vm.Retrying... (attempt {0})"], Math.Max(update.Attempts, 1));
                    row.RetryCount = update.Attempts;
                    row.ErrorDescription = update.Error;
                }
                StatusMessage = string.Format(Loc["Vm.Retrying queued work card #{0}..."], update.PendingId);
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
                        ? Loc["Vm.Successfully sent"]
                        : string.Format(Loc["Vm.Successfully sent — Protocol: {0}"], update.Protocol),
                    QueuedScanStatus.Rejected => Loc["Vm.Rejected by Ergani"],
                    _ => Loc["Vm.Waiting to send"]
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
                    ? string.Format(Loc["Vm.Successfully sent API log entry #{0}."], update.LogId)
                    : string.Format(Loc["Vm.Successfully sent API log entry #{0} — Protocol: {1}"], update.LogId, update.Protocol),
                LogRetryStatus.Failed when update.Message == "Retrying..."
                    => string.Format(Loc["Vm.Retrying API log entry #{0}..."], update.LogId),
                LogRetryStatus.Failed => string.Format(Loc["Vm.Retry failed for API log entry #{0}: {1}"], update.LogId, update.Message),
                _ => string.Format(Loc["Vm.Retry skipped for API log entry #{0}: {1}"], update.LogId, update.Message)
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
        StatusMessage = Loc["Vm.Triggering manual retry..."];

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
                Loc["Vm.Manual retry triggered."];
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
        StatusMessage = string.Format(Loc["Vm.Retrying {0} work card(s)..."], ids.Count);

        try
        {
            var results = await _logRetryService.RetryAsync(ids, SelectedLateJustification);

            var ok      = results.Count(r => r.Status == LogRetryStatus.Succeeded);
            var failed  = results.Count(r => r.Status == LogRetryStatus.Failed);
            var skipped = results.Count(r => r.Status == LogRetryStatus.Skipped);

            // Rows were updated in place in the database — reload to show the new state.
            await LoadAsync();
            SelectedRow = Rows.FirstOrDefault(r => r.Id == keepSelectedId);

            StatusMessage = string.Format(Loc["Vm.Retry finished: {0} succeeded, {1} failed"], ok, failed) +
                            (skipped > 0 ? string.Format(Loc["Vm., {0} skipped."], skipped) : ".");
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

            // The date pickers represent local calendar dates, while SubmissionDate
            // is stored in UTC. Convert each local midnight boundary to UTC before
            // querying; comparing local midnight directly with UTC timestamps hides
            // submissions around the start/end of the day.
            var fromLocalDate = (FilterFromDate ?? DateTimeOffset.Now).Date;
            var toLocalDate = (FilterToDate ?? DateTimeOffset.Now).Date;

            if (toLocalDate < fromLocalDate)
                toLocalDate = fromLocalDate;

            var fromDateUtc = new DateTimeOffset(
                fromLocalDate,
                TimeZoneInfo.Local.GetUtcOffset(fromLocalDate)).UtcDateTime;

            var toLocalDateExclusive = toLocalDate.AddDays(1);
            var toDateExclusiveUtc = new DateTimeOffset(
                toLocalDateExclusive,
                TimeZoneInfo.Local.GetUtcOffset(toLocalDateExclusive)).UtcDateTime;

            var employees = await db.Employees
                .Where(e => e.CompanyId == companyId)
                .OrderBy(e => e.LastName).ThenBy(e => e.FirstName)
                .Select(e => new EmployeeFilterOption { Id = e.Id, Name = e.LastName + " " + e.FirstName, TaxId = e.TaxId })
                .ToListAsync();

            EmployeeOptions.Clear();
            EmployeeOptions.Add(new EmployeeFilterOption { Id = 0, Name = Loc[L.AllEmployees], TaxId = string.Empty });
            foreach (var employee in employees) EmployeeOptions.Add(employee);

            var query = db.ApiSubmissionLogs
                .Where(l =>
                    l.CompanyId == companyId &&
                    l.SubmissionDate >= fromDateUtc &&
                    l.SubmissionDate < toDateExclusiveUtc);

            if (FilterFailuresOnly)
            {
                query = query.Where(l => !l.Success);
            }

            var results = await query
                .OrderByDescending(l => l.SubmissionDate)
                .ThenByDescending(l => l.Id)
                .Take(500)
                .ToListAsync();

            // Backfill older batch rows once: derive employee IDs from their saved AFMs,
            // then persist the multi-employee list so it is available directly in the database.
            var backfilledAny = false;
            foreach (var log in results.Where(l => string.IsNullOrWhiteSpace(l.EmployeeIdsJson)))
            {
                var employeeIds = employees
                    .Where(e => !string.IsNullOrWhiteSpace(e.TaxId) &&
                        (log.RequestPayloadJson?.Contains(e.TaxId, StringComparison.Ordinal) == true ||
                         log.ResponseRawJson?.Contains(e.TaxId, StringComparison.Ordinal) == true))
                    .Select(e => e.Id)
                    .Distinct()
                    .ToList();

                if (employeeIds.Count == 0)
                    continue;

                log.EmployeeIdsJson = JsonSerializer.Serialize(employeeIds);
                if (employeeIds.Count == 1 && log.EmployeeId is null)
                    log.EmployeeId = employeeIds[0];
                backfilledAny = true;
            }

            if (backfilledAny)
                await db.SaveChangesAsync();

            if (SelectedEmployeeId is > 0)
            {
                var employee = employees.FirstOrDefault(e => e.Id == SelectedEmployeeId.Value);
                results = results.Where(log =>
                    log.EmployeeId == SelectedEmployeeId.Value ||
                    ContainsEmployeeId(log.EmployeeIdsJson, SelectedEmployeeId.Value) ||
                    (!string.IsNullOrWhiteSpace(employee?.TaxId) &&
                     (log.RequestPayloadJson?.Contains(employee.TaxId, StringComparison.Ordinal) == true ||
                      log.ResponseRawJson?.Contains(employee.TaxId, StringComparison.Ordinal) == true)))
                    .ToList();
            }

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
                    EmployeeNames = GetEmployeeNames(r.RequestPayloadJson, employees),
                    RetryCount = r.RetryCount,
                    LastRetryAt = r.LastRetryAt
                };

                row.PropertyChanged += OnRowPropertyChanged;
                Rows.Add(row);
            }

            UpdateSelectedRetryCount();

            StatusMessage = results.Count == 500
                ? Loc["Vm.Showing latest 500 — narrow date range for more."]
                : string.Format(Loc["Vm.{0} log entries."], results.Count);
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

    private static bool ContainsEmployeeId(string? employeeIdsJson, int employeeId)
    {
        if (string.IsNullOrWhiteSpace(employeeIdsJson))
            return false;

        try
        {
            using var document = JsonDocument.Parse(employeeIdsJson);
            return document.RootElement.ValueKind == JsonValueKind.Array &&
                   document.RootElement.EnumerateArray().Any(value =>
                       value.ValueKind == JsonValueKind.Number &&
                       value.TryGetInt32(out var id) && id == employeeId);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static string GetEmployeeNames(string? payload, System.Collections.Generic.List<EmployeeFilterOption> employees)
    {
        if (string.IsNullOrWhiteSpace(payload)) return string.Empty;
        try
        {
            using var doc = JsonDocument.Parse(payload);
            var afms = new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal);
            void Visit(JsonElement element)
            {
                if (element.ValueKind == JsonValueKind.Object)
                {
                    foreach (var property in element.EnumerateObject())
                    {
                        if (property.NameEquals("f_afm") && property.Value.ValueKind == JsonValueKind.String)
                            afms.Add(property.Value.GetString() ?? string.Empty);
                        else Visit(property.Value);
                    }
                }
                else if (element.ValueKind == JsonValueKind.Array)
                    foreach (var child in element.EnumerateArray()) Visit(child);
            }
            Visit(doc.RootElement);
            var names = employees.Where(e => !string.IsNullOrWhiteSpace(e.TaxId) && afms.Contains(e.TaxId))
                .Select(e => e.Name).Distinct().ToList();
            return names.Count == 0 ? string.Empty : string.Join(", ", names);
        }
        catch { return string.Empty; }
    }

    public async Task SaveSelectedPdfAsync(string filePath, CancellationToken ct = default)
    {
        var row = SelectedRow;
        if (row == null || !row.HasPdf || _session?.CompanyId is not int companyId)
            throw new InvalidOperationException(Loc[L.DownloadPdf]);

        // Ergani's document endpoint expects the full Documents/<code> path.
        // The Schedules page already uses this format successfully.
        var documentCode = row.SubmissionType switch
        {
            "WorkCard" => "Documents/WRKCardSE",
            "DailySchedule" => "Documents/WTODaily",
            "WeeklySchedule" => "Documents/WTOWeek",
            "Overtime" => "Documents/WTOOv",
            "Leave" => "Documents/WTOLeave",
            _ => throw new InvalidOperationException("PDF retrieval is not configured for submission type '" + row.SubmissionType + "'.")
        };

        var bytes = await _documentService.DownloadPdfAsync(
            companyId, documentCode, row.Protocol!,
            DateOnly.FromDateTime(row.SubmissionDate.ToLocalTime()), ct);
        await File.WriteAllBytesAsync(filePath, bytes, ct);
        StatusMessage = Loc[L.SuccessPrefix] + Loc[L.DownloadPdf] + ": " + filePath;
    }
}

public sealed class EmployeeFilterOption
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string TaxId { get; set; } = string.Empty;
}

