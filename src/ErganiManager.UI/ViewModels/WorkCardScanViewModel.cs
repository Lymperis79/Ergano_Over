using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ErganiManager.Core.Interfaces;
using ErganiManager.Core.Models;
using ErganiManager.ErganiApi.Services;
using ErganiManager.LocalCache;
using ErganiManager.LocalCache.Entities;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

namespace ErganiManager.UI.ViewModels;

public partial class ScanResultRow : ObservableObject
{
    public DateTime ScannedAt { get; init; }

    public string EmployeeName { get; init; } =
        string.Empty;

    public string MovementType { get; init; } =
        string.Empty;

    /// <summary>Id of the locally queued scan this row stands for (null if it was never queued).</summary>
    public int? PendingId { get; init; }

    /// <summary>True while the scan is still waiting to be delivered to Ergani.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusIcon))]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    private bool _isPending;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusIcon))]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    private bool _success;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    private string _protocol = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    private string _errorDescription = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    private int _retryAttempts;

    public string TimeText =>
        ScannedAt.ToString("HH:mm:ss");

    public string MovementIcon =>
        MovementType == "Arrival"
            ? "🟢"
            : "🔴";

    public string StatusIcon =>
        IsPending
            ? "⏳"
            : Success
                ? "✅"
                : "❌";

    public string StatusText =>
        IsPending
            ? (RetryAttempts > 0
                ? $"Retrying to Ergani — attempt {RetryAttempts}" +
                  (string.IsNullOrEmpty(ErrorDescription) ? "…" : $" ({ErrorDescription})")
                : (string.IsNullOrEmpty(ErrorDescription)
                    ? "Sending to Ergani in the background…"
                    : $"Waiting — will retry automatically ({ErrorDescription})"))
            : Success
                ? $"Protocol: {Protocol}"
                : ErrorDescription;
}

/// <summary>
/// Parsed data from an Ergani scanner string such as:
///
/// ergInm:XXXX;In:XXXX;afm:XXXX;id:XXXX
/// </summary>
public sealed class ErganiScanData
{
    public string? ErgInm { get; init; }

    public string? In { get; init; }

    public string? Afm { get; init; }

    public string? Id { get; init; }
}

public partial class WorkCardScanViewModel :
    ViewModelBase,
    IAdminSectionViewModel
{
    private readonly IWorkCardSubmitter _workCardSubmitter;
    private readonly ErganiRetryService _retryService;
    private readonly IConnectionStateService _connectionState;
    private readonly ICompanyService _companyService;
    private readonly ICompanyContext _companyContext;

    private UserSession? _session;

    /*
     * The last failed submission is kept here so that the retry
     * dialog can submit exactly the same employee/movement again.
     */
    private WorkCardSubmissionRequest? _retryRequest;

    private static readonly TimeSpan ScanCooldown =
        TimeSpan.FromSeconds(20);

    private readonly Dictionary<string, DateTime> _lastScanTime =
        new();

    public ObservableCollection<ScanResultRow> RecentScans { get; } =
        new();

    public ObservableCollection<CompanyDto> AvailableCompanies { get; } =
        new();

    [ObservableProperty]
    private CompanyDto? _selectedCompany;

    /*
     * The main AdminShell is the source of truth for the company.
     *
     * Therefore the scan window displays the selected company but
     * does not independently switch companies.
     */
    [ObservableProperty]
    private bool _canChangeCompany;

    /*
     * Ergani f_aitiologia codes.
     *
     * 001 = Problem with electricity / telecommunications
     * 002 = Problem with employer systems
     * 003 = Problem connecting to ERGANI
     */
    public ObservableCollection<LocalizedCodeOption> AvailableAitiologiai { get; }

    [ObservableProperty] private bool _hasActiveCompany;
    [ObservableProperty] private string _noCompanyMessage = string.Empty;
    [ObservableProperty] private string _barcodeInput = string.Empty;
    [ObservableProperty] private bool _isArrival = true;
    [ObservableProperty] private bool _autoDetect = true;
    [ObservableProperty] private bool _isProcessing;

    /*
     * Manual date/time override.
     * Only used when AutoDetect = false.
     */
    [ObservableProperty] private DateTimeOffset? _movementDate = DateTimeOffset.Now;

    [ObservableProperty] private TimeSpan? _movementTime = DateTime.Now.TimeOfDay;

    /*
     * Normal response
     */
    [ObservableProperty] private string _responseTitle = string.Empty;
    [ObservableProperty] private string _responseDetail = string.Empty;
    [ObservableProperty] private bool _responseSuccess;
    [ObservableProperty] private bool _hasResponse;

    /*
     * Retry dialog
     */
    [ObservableProperty] private bool _isRetryDialogOpen;

    [ObservableProperty] private string _retryEmployeeName = string.Empty;

    [ObservableProperty] private string _retryMovementType = string.Empty;

    [ObservableProperty] private DateTime _retryDateTime;

    [ObservableProperty] private string? _retryAitiologia;

    public WorkCardScanViewModel(
        IWorkCardSubmitter workCardSubmitter,
        ErganiRetryService retryService,
        IConnectionStateService connectionState,
        ICompanyService companyService,
        ICompanyContext companyContext)
    {
        _workCardSubmitter = workCardSubmitter;
        _retryService = retryService;
        _retryService.QueuedScanUpdated += OnQueuedScanUpdated;
        _connectionState = connectionState;
        _companyService = companyService;
        _companyContext = companyContext;
        AvailableAitiologiai = AitiologiaOptions.Create(Loc);

        /*
         * If the company is changed from the main AdminShell,
         * immediately update the company displayed in this window.
         */
        _companyContext.CompanyChanged +=
            OnCompanyContextChanged;
    }

    public void Initialize(UserSession session)
    {
        /*
         * The AdminShell passes a company-scoped session here.
         */
        _session = session;

        var companyId =
            session.CompanyId ??
            _companyContext.ActiveCompanyId;

        HasActiveCompany =
            companyId.HasValue;

        NoCompanyMessage =
            companyId.HasValue
                ? string.Empty
                : "Select a company first.";

        /*
         * The company ComboBox is a display of the main
         * window's selected company.
         */
        CanChangeCompany = false;

        /*
         * Load the company list asynchronously.
         */
        _ = LoadCompaniesAsync(companyId);
    }

    private async Task LoadCompaniesAsync(
        int? selectedCompanyId)
    {
        try
        {
            var companies =
                await _companyService
                    .GetAllAsync()
                    .ConfigureAwait(false);

            var activeCompanies =
                companies
                    .Where(c => c.IsActive)
                    .ToList();

            await Avalonia.Threading.Dispatcher.UIThread
                .InvokeAsync(() =>
                {
                    AvailableCompanies.Clear();

                    foreach (var company in activeCompanies)
                    {
                        AvailableCompanies.Add(company);
                    }

                    /*
                     * First priority:
                     * company passed by the AdminShell.
                     */
                    if (selectedCompanyId.HasValue)
                    {
                        SelectedCompany =
                            AvailableCompanies.FirstOrDefault(
                                c => c.Id ==
                                     selectedCompanyId.Value);
                    }

                    /*
                     * Second priority:
                     * currently active company in ICompanyContext.
                     */
                    if (SelectedCompany == null)
                    {
                        var activeId =
                            _companyContext.ActiveCompanyId;

                        if (activeId.HasValue)
                        {
                            SelectedCompany =
                                AvailableCompanies.FirstOrDefault(
                                    c => c.Id == activeId.Value);
                        }
                    }

                    /*
                     * If there is exactly one active company,
                     * show it automatically.
                     */
                    if (SelectedCompany == null &&
                        AvailableCompanies.Count == 1)
                    {
                        SelectedCompany =
                            AvailableCompanies[0];
                    }

                    HasActiveCompany =
                        SelectedCompany != null;

                    NoCompanyMessage =
                        SelectedCompany != null
                            ? string.Empty
                            : "Select a company first.";
                });
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(
                ex,
                "Failed to load companies for barcode scan window");

            await Avalonia.Threading.Dispatcher.UIThread
                .InvokeAsync(() =>
                {
                    AvailableCompanies.Clear();
                    SelectedCompany = null;
                    HasActiveCompany = false;

                    NoCompanyMessage =
                        "Unable to load companies.";
                });
        }
    }

    private void OnCompanyContextChanged(
        object? sender,
        EventArgs e)
    {
        var companyId =
            _companyContext.ActiveCompanyId;

        if (!companyId.HasValue)
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                SelectedCompany = null;
                HasActiveCompany = false;
                NoCompanyMessage =
                    "Select a company first.";
            });

            return;
        }

        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            var company =
                AvailableCompanies.FirstOrDefault(
                    c => c.Id == companyId.Value);

            if (company == null)
            {
                /*
                 * The company list may still be loading.
                 * Load it again.
                 */
                _ = LoadCompaniesAsync(companyId);
                return;
            }

            SelectedCompany = company;
            HasActiveCompany = true;
            NoCompanyMessage = string.Empty;

            /*
             * Keep the session used by this window synchronized
             * with the main window.
             */
            if (_session != null)
            {
                _session =
                    new UserSession
                    {
                        UserId = _session.UserId,
                        Username = _session.Username,
                        Role = _session.Role,

                        CompanyId = company.Id,
                        CompanyName = company.Name,

                        BranchId = null,
                        BranchName = null,

                        IsOfflineSession =
                            _session.IsOfflineSession
                    };
            }
        });
    }

    partial void OnSelectedCompanyChanged(
        CompanyDto? value)
    {
        HasActiveCompany =
            value != null;

        NoCompanyMessage =
            value != null
                ? string.Empty
                : "Select a company first.";
    }

    // ────────────────────────────────────────────────────────────────────────
    // SCAN PARSING
    // ────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Accepts:
    ///
    /// xxxx
    ///
    /// or:
    ///
    /// ergInm:xxxx;In:xxxx;afm:xxxx;id:xxxx
    ///
    /// Returns the normalized AFM when the input represents an AFM.
    /// </summary>
    private static string? ExtractAfm(
        string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return null;

        var value =
            input.Trim();

        /*
         * Plain AFM.
         */
        if (IsValidAfm(value))
            return value;

        /*
         * Ergani scanner format.
         */
        var parsed =
            ParseErganiScanString(value);

        if (parsed == null)
            return null;

        if (IsValidAfm(parsed.Afm))
            return parsed.Afm;

        return null;
    }

    private static bool IsValidAfm(
        string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var digits =
            new string(
                value
                    .Where(char.IsDigit)
                    .ToArray());

        return digits.Length == 9;
    }

    /// <summary>
    /// Parses the scanner string:
    ///
    /// ergInm:xxxx;
    /// In:xxxx;
    /// afm:xxxx;
    /// id:xxxx
    ///
    /// into a small strongly typed object.
    /// </summary>
    private static ErganiScanData? ParseErganiScanString(
        string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return null;

        if (!input.Contains(':'))
            return null;

        var values =
            new Dictionary<string, string>(
                StringComparer.OrdinalIgnoreCase);

        var parts =
            input.Split(
                ';',
                StringSplitOptions.RemoveEmptyEntries);

        foreach (var part in parts)
        {
            var separator =
                part.IndexOf(':');

            if (separator <= 0)
                continue;

            var key =
                part[..separator]
                    .Trim();

            var value =
                part[(separator + 1)..]
                    .Trim();

            if (string.IsNullOrWhiteSpace(key))
                continue;

            values[key] = value;
        }

        if (values.Count == 0)
            return null;

        values.TryGetValue(
            "ergInm",
            out var ergInm);

        values.TryGetValue(
            "In",
            out var firstName);

        values.TryGetValue(
            "afm",
            out var afm);

        values.TryGetValue(
            "id",
            out var id);

        /*
         * If there is no AFM at all, this is not an Ergani
         * employee string for our purposes.
         */
        if (string.IsNullOrWhiteSpace(afm))
            return null;

        return new ErganiScanData
        {
            ErgInm = ergInm,
            In = firstName,
            Afm = NormalizeAfm(afm),
            Id = id
        };
    }

    private static string? NormalizeAfm(
        string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var digits =
            new string(
                value
                    .Where(char.IsDigit)
                    .ToArray());

        if (digits.Length != 9)
            return null;

        /*
         * Preserve leading zeroes.
         */
        return digits.PadLeft(9, '0');
    }

    

    // ────────────────────────────────────────────────────────────────────────
    // SCAN SUBMISSION
    // ────────────────────────────────────────────────────────────────────────

    [RelayCommand]
    private async Task SubmitScanAsync()
    {
        /*
         * Always use the company selected in the main shell/context.
         */
        var companyId =
            SelectedCompany?.Id ??
            _companyContext.ActiveCompanyId;

        if (!companyId.HasValue)
        {
            ShowResponse(
                false,
                "No Company",
                "Select a company before submitting a work card.");

            return;
        }

        var scanValue =
            BarcodeInput.Trim();

        if (string.IsNullOrWhiteSpace(scanValue))
            return;

        /*
         * Determine whether the input is:
         *
         * 1. plain AFM
         * 2. Ergani serialized string containing AFM
         * 3. barcode
         */
        var afm =
            ExtractAfm(scanValue);

        var isAfmScan =
            afm != null;

        /*
         * Use the normalized AFM as the cooldown key.
         *
         * This means:
         *
         * XXXX
         *
         * and
         *
         * ergInm:...;afm:XXXX;...
         *
         * are considered the same employee scan.
         */
        var cooldownKey =
            isAfmScan
                ? $"AFM:{afm}"
                : $"BARCODE:{scanValue}";

        /*
         * Prevent accidental duplicate scans.
         */
        if (_lastScanTime.TryGetValue(
                cooldownKey,
                out var lastTime))
        {
            var elapsed =
                DateTime.Now - lastTime;

            if (elapsed < ScanCooldown)
            {
                var remaining =
                    (int)Math.Ceiling(
                        (ScanCooldown - elapsed)
                            .TotalSeconds);

                ShowResponse(
                    false,
                    "⏳ Already Scanned",
                    $"This employee was scanned " +
                    $"{(int)elapsed.TotalSeconds}s ago.\n" +
                    $"Please wait {remaining} more second(s).");

                BarcodeInput = string.Empty;

                return;
            }
        }

        IsProcessing = true;
        HasResponse = false;

        try
        {
            CachedEmployee? employee = null;
            using var cache = LocalCacheDbContextFactory.Create();
            

            // Restore the original barcode lookup.
            // This preserves the scan behavior that was working before.
            employee = await cache.CachedEmployees
                .FirstOrDefaultAsync(e =>
                    e.CompanyId == companyId.Value &&
                    e.TaxId == afm &&
                    e.IsActive);

            if (employee == null)
            {
                if (isAfmScan)
                {
                    ShowResponse(
                        false,
                        "Unknown Employee",
                        $"No active employee found " +
                        $"with AFM '{afm}' " +
                        $"for the selected company.");
                }
                else
                {
                    ShowResponse(
                        false,
                        "Unknown Badge",
                        $"No active employee found " +
                        $"with barcode '{scanValue}'.");
                }

                return;
            }

            /*
             * Determine movement.
             */
            string movement;
            DateTime scanTime;

            if (AutoDetect)
            {
                var lastPending =
                    cache.PendingSubmissions
                        .Where(
                            p =>
                                p.EmployeeId ==
                                    employee.Id)
                        .OrderByDescending(
                            p => p.ScannedAt)
                        .FirstOrDefault();

                movement =
                    lastPending?.MovementType ==
                        "Arrival"
                        ? "Departure"
                        : "Arrival";

                scanTime =
                    DateTime.Now;
            }
            else
            {
                movement =
                    IsArrival
                        ? "Arrival"
                        : "Departure";

                var date =
                    (
                        MovementDate ??
                        DateTimeOffset.Now
                    )
                    .LocalDateTime
                    .Date;

                var time =
                    MovementTime ??
                    DateTime.Now.TimeOfDay;

                scanTime =
                    date + time;
            }

            /*
             * Build request once.
             *
             * This same request is reused when Retry is selected.
             */
            var request =
                new WorkCardSubmissionRequest
                {
                    EmployeeId =
                        employee.Id,

                    CompanyId =
                        companyId.Value,

                    BranchId =
                        employee.BranchId,

                    MovementType =
                        movement,

                    MovementDateTime =
                        scanTime
                };

            var name =
                employee.FullName;

            /*
             * Save-first: record the scan locally and free the scanner at once so the
             * next person can scan. The background service sends it to Ergani and keeps
             * retrying while the connection is down.
             */
            var queued = new PendingSubmission
            {
                EmployeeId = employee.Id,
                CompanyId = companyId.Value,
                BranchId = employee.BranchId,
                EmployeeBarcodeId = employee.BarcodeId,
                MovementType = movement,
                ScannedAt = scanTime,
                Synced = false
            };

            cache.PendingSubmissions.Add(queued);

            await cache.SaveChangesAsync();

            _retryService.TriggerNow();

            ShowResponse(
                true,
                $"✅ {movement.ToUpper()} — {name}",
                $"Saved — sending to Ergani in the background.\n" +
                $"Time:          {scanTime:HH:mm:ss dd/MM/yyyy}");

            RecentScans.Insert(
                0,
                new ScanResultRow
                {
                    ScannedAt = scanTime,
                    EmployeeName = name,
                    MovementType = movement,
                    PendingId = queued.Id,
                    IsPending = true   // updated automatically when the background send finishes
                });

            ClearRetryState();

            while (RecentScans.Count > 50)
            {
                RecentScans.RemoveAt(
                    RecentScans.Count - 1);
            }

            _lastScanTime[cooldownKey] =
                DateTime.Now;

            BarcodeInput =
                string.Empty;
        }
        catch (Exception ex)
        {
            ShowResponse(
                false,
                "Error",
                ex.Message);
        }
        finally
        {
            IsProcessing = false;
        }
    }

    // ────────────────────────────────────────────────────────────────────────
    // RETRY
    // ────────────────────────────────────────────────────────────────────────

    private void OpenRetryDialog(
        WorkCardSubmissionRequest request,
        string employeeName,
        string movementType,
        DateTime movementDateTime)
    {
        _retryRequest =
            request;

        RetryEmployeeName =
            employeeName;

        RetryMovementType =
            movementType;

        RetryDateTime =
            movementDateTime;

        RetryAitiologia =
            AvailableAitiologiai
                .FirstOrDefault()?.Code;

        IsRetryDialogOpen =
            true;
    }

    [RelayCommand]
    private void CloseRetryDialog()
    {
        ClearRetryState();
    }

    [RelayCommand]
    private async Task SubmitRetryAsync()
    {
        if (_retryRequest == null)
        {
            IsRetryDialogOpen =
                false;

            ShowResponse(
                false,
                "Retry Error",
                "There is no failed submission " +
                "available to retry.");

            return;
        }

        if (string.IsNullOrWhiteSpace(
                RetryAitiologia))
        {
            ShowResponse(
                false,
                "Reason Required",
                "Please select an Ergani " +
                "justification code.");

            return;
        }

        IsProcessing =
            true;

        try
        {
            var request =
                _retryRequest;

            var result =
                await _workCardSubmitter
                    .SubmitAsync(
                        request,
                        RetryAitiologia);

            if (result.Success)
            {
                ShowResponse(
                    true,
                    $"✅ Retry Successful — " +
                    $"{RetryEmployeeName}",
                    $"Protocol:      {result.Protocol}\n" +
                    $"Submission ID: {result.SubmissionId}\n" +
                    $"Time:          " +
                    $"{request.MovementDateTime:HH:mm:ss dd/MM/yyyy}\n" +
                    $"Reason:        {RetryAitiologia}");

                RecentScans.Insert(
                    0,
                    new ScanResultRow
                    {
                        ScannedAt =
                            request.MovementDateTime,

                        EmployeeName =
                            RetryEmployeeName,

                        MovementType =
                            RetryMovementType,

                        Success =
                            true,

                        Protocol =
                            result.Protocol ??
                            string.Empty
                    });

                while (RecentScans.Count > 50)
                {
                    RecentScans.RemoveAt(
                        RecentScans.Count - 1);
                }

                ClearRetryState();
            }
            else
            {
                var error =
                    result.ErrorMessage ??
                    "Ergani rejected the retry submission.";

                ShowResponse(
                    false,
                    $"❌ Retry Failed — " +
                    $"{RetryEmployeeName}",
                    error);

                IsRetryDialogOpen =
                    true;
            }
        }
        catch (Exception ex)
        {
            ShowResponse(
                false,
                "Retry Error",
                ex.Message);

            IsRetryDialogOpen =
                true;
        }
        finally
        {
            IsProcessing =
                false;
        }
    }

    private void ClearRetryState()
    {
        _retryRequest =
            null;

        RetryEmployeeName =
            string.Empty;

        RetryMovementType =
            string.Empty;

        RetryDateTime =
            default;

        RetryAitiologia =
            null;

        IsRetryDialogOpen =
            false;
    }

    // ────────────────────────────────────────────────────────────────────────
    // RESPONSE / HISTORY
    // ────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Called (from the background sender's thread) when a queued scan is delivered, rejected
    /// or is still waiting. Updates the matching row in the recent-scans list in place.
    /// </summary>
    private void OnQueuedScanUpdated(object? sender, QueuedScanUpdate update)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            var row = RecentScans.FirstOrDefault(r => r.PendingId == update.PendingId);
            if (row == null) return;

            row.RetryAttempts = update.Attempts;

            switch (update.Status)
            {
                case QueuedScanStatus.Sent:
                    row.IsPending = false;
                    row.Success = true;
                    row.ErrorDescription = string.Empty;
                    row.Protocol = update.Protocol ?? string.Empty;
                    break;

                case QueuedScanStatus.Rejected:
                    row.IsPending = false;
                    row.Success = false;
                    row.ErrorDescription = update.Error ?? "Rejected by Ergani.";
                    break;

                default: // Waiting
                    row.IsPending = true;
                    row.ErrorDescription = update.Error ?? string.Empty;
                    break;
            }

            // Keep the big message at the top in step when it belongs to the latest scan.
            if (RecentScans.Count > 0 && ReferenceEquals(RecentScans[0], row))
            {
                var title = $"{row.MovementType.ToUpper()} — {row.EmployeeName}";

                if (update.Status == QueuedScanStatus.Sent)
                    ShowResponse(
                        true,
                        $"✅ {title} — Successfully sent",
                        $"Protocol:      {row.Protocol}\n" +
                        $"Time:          {row.ScannedAt:HH:mm:ss dd/MM/yyyy}");
                else if (update.Status == QueuedScanStatus.Waiting)
                    ShowResponse(
                        false,
                        $"⏳ Retry sending — {row.EmployeeName}",
                        $"Attempt:       {row.RetryAttempts}\n" +
                        $"Status:        Waiting for Ergani / internet\n" +
                        (string.IsNullOrWhiteSpace(row.ErrorDescription) ? string.Empty : $"Reason:        {row.ErrorDescription}"));
                else
                    ShowResponse(
                        false,
                        $"❌ Failed — {row.EmployeeName}",
                        row.ErrorDescription);
            }
        });
    }

    private void ShowResponse(
        bool success,
        string title,
        string detail)
    {
        ResponseSuccess =
            success;

        ResponseTitle =
            title;

        ResponseDetail =
            detail;

        HasResponse =
            true;
    }

    [RelayCommand]
    private void ClearResponse()
    {
        HasResponse =
            false;

        BarcodeInput =
            string.Empty;
    }

    [RelayCommand]
    private void ClearHistory()
    {
        RecentScans.Clear();
    }
}