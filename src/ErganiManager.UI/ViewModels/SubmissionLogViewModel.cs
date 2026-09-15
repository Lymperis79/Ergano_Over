using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ErganiManager.Core.Interfaces;
using ErganiManager.Core.Models;
using ErganiManager.Data;
using ErganiManager.ErganiApi.Services;
using ErganiManager.LocalCache;
using Microsoft.EntityFrameworkCore;

namespace ErganiManager.UI.ViewModels;

public class SubmissionLogRow
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

    public string StatusIcon => Success ? "✅" : "❌";
    public string DurationText => $"{DurationMs} ms";
    public string DateText => SubmissionDate.ToString("dd/MM/yyyy HH:mm:ss");
}

public class FailedSubmissionRow
{
    public int Id { get; set; }
    public string EmployeeId { get; set; } = string.Empty;
    public string MovementType { get; set; } = string.Empty;
    public DateTime OriginalScannedAt { get; set; }
    public string FailureReason { get; set; } = string.Empty;
    public string? ErrorDescription { get; set; }
    public int RetryCount { get; set; }
    public DateTime? LastRetryAt { get; set; }
    public string? LastRetryError { get; set; }

    public string ScanTimeText => OriginalScannedAt.ToString("dd/MM/yyyy HH:mm:ss");
    public string LastRetryText => LastRetryAt?.ToString("dd/MM/yyyy HH:mm") ?? "Never";
    public string MovementIcon => MovementType == "Arrival" ? "🟢" : "🔴";
}

public partial class SubmissionLogViewModel : ViewModelBase, IAdminSectionViewModel
{
    private readonly IConnectionStateService _connectionState;
    private readonly ErganiRetryService _retryService;
    private UserSession? _session;

    public ObservableCollection<SubmissionLogRow> Rows { get; } = new();
    public ObservableCollection<FailedSubmissionRow> FailedSubmissions { get; } = new();

    [ObservableProperty] private bool _hasActiveCompany;
    [ObservableProperty] private string _noCompanyMessage = string.Empty;
    [ObservableProperty] private string _statusMessage = string.Empty;
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private bool _isRetrying;
    [ObservableProperty] private string _failedCountText = string.Empty;

    [ObservableProperty] private DateTimeOffset? _filterFrom = DateTimeOffset.Now.AddDays(-7);
    [ObservableProperty] private DateTimeOffset? _filterTo = DateTimeOffset.Now;
    [ObservableProperty] private bool _filterFailuresOnly;

    [ObservableProperty] private SubmissionLogRow? _selectedRow;

    public SubmissionLogViewModel(IConnectionStateService connectionState, ErganiRetryService retryService)
    {
        _connectionState = connectionState;
        _retryService = retryService;
    }

    public void Initialize(UserSession session)
    {
        _session = session;
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
        if (_session?.CompanyId is not int companyId) return;

        using var cache = LocalCacheDbContextFactory.Create();
        var items = await cache.FailedSubmissions
            .Where(f => f.CompanyId == companyId && !f.Resolved)
            .OrderBy(f => f.OriginalScannedAt)
            .ToListAsync();

        FailedSubmissions.Clear();
        foreach (var f in items)
        {
            FailedSubmissions.Add(new FailedSubmissionRow
            {
                Id                = f.Id,
                EmployeeId        = f.EmployeeId.ToString(),
                MovementType      = f.MovementType,
                OriginalScannedAt = f.OriginalScannedAt,
                FailureReason     = f.FailureReason,
                ErrorDescription  = f.ErrorDescription,
                RetryCount        = f.RetryCount,
                LastRetryAt       = f.LastRetryAt,
                LastRetryError    = f.LastRetryError
            });
        }

        FailedCountText = FailedSubmissions.Count == 0
            ? Loc[L.NoFailedPending]
            : string.Format(Loc[L.PendingRetry], FailedSubmissions.Count);
    }

    [RelayCommand]
    private async Task ManualRetryNowAsync()
    {
        if (_session?.CompanyId is null) return;

        IsRetrying = true;
        StatusMessage = "Triggering manual retry...";
        try
        {
            _retryService.Start();
            await Task.Delay(TimeSpan.FromSeconds(3));
            await LoadFailedSubmissionsAsync();
            StatusMessage = Loc[L.SuccessPrefix] + "Manual retry triggered.";
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

    [RelayCommand]
    private async Task LoadAsync()
    {
        if (_session?.CompanyId is not int companyId) return;

        IsLoading = true;
        StatusMessage = string.Empty;

        try
        {
            await using var db = new AppDbContext(_connectionState.GetDbOptions());

            var fromDt = (FilterFrom ?? DateTimeOffset.Now.AddDays(-7)).UtcDateTime;
            var toDt   = (FilterTo ?? DateTimeOffset.Now).UtcDateTime;

            var query = db.ApiSubmissionLogs
                .Where(l => l.CompanyId == companyId
                         && l.SubmissionDate >= fromDt
                         && l.SubmissionDate <= toDt);

            if (FilterFailuresOnly)
                query = query.Where(l => !l.Success);

            var results = await query
                .OrderByDescending(l => l.SubmissionDate)
                .Take(500)
                .ToListAsync();

            Rows.Clear();
            foreach (var r in results)
            {
                Rows.Add(new SubmissionLogRow
                {
                    Id                 = r.Id,
                    SubmissionType     = r.SubmissionType,
                    SubmissionDate     = r.SubmissionDate,
                    Success            = r.Success,
                    HttpStatusCode     = r.HttpStatusCode,
                    Protocol           = r.Protocol,
                    ErrorMessage       = r.ErrorMessage,
                    DurationMs         = r.DurationMs,
                    RequestPayloadJson = r.RequestPayloadJson,
                    ResponseRawJson    = r.ResponseRawJson
                });
            }

            StatusMessage = results.Count == 500
                ? "Showing latest 500 — narrow date range for more."
                : $"{results.Count} log entries.";
        }
        catch (Exception ex)
        {
            StatusMessage = Loc[L.ErrorPrefix] + ex.Message;
        }
        finally
        {
            IsLoading = false;
        }
    }
}
