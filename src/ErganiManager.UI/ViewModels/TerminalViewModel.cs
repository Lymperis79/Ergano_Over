using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ErganiManager.Core.Interfaces;
using ErganiManager.Core.Models;
using ErganiManager.Data.Entities;
using ErganiManager.ErganiApi.Services;
using ErganiManager.LocalCache;
using ErganiManager.LocalCache.Entities;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ErganiManager.UI.ViewModels;

public enum ScanPopupKind
{
    None,
    Success,
    EarlyDepartureWarning,
    TooEarlyBlocked,
    NoScheduleBlocked,
    UnknownBadge,
    Error
}

public partial class TerminalViewModel : ViewModelBase
{
    private readonly IConnectionStateService _connectionState;
    private readonly ErganiRetryService _retryService;
    private readonly IEmailAlertService _emailAlertService;
    private readonly ICacheSyncService _cacheSync;
    private readonly System.Timers.Timer _clockTimer;
    private readonly System.Timers.Timer _popupAutoCloseTimer;

    private UserSession? _session;

    [ObservableProperty] private string _companyName = string.Empty;
    [ObservableProperty] private string _branchName = string.Empty;
    [ObservableProperty] private string _currentTimeText = string.Empty;
    [ObservableProperty] private string _currentDateText = string.Empty;

    [ObservableProperty] private string _scanInput = string.Empty;
    [ObservableProperty] private bool _isProcessing;

    [ObservableProperty] private bool _isOfflineMode;

    // Popup state
    [ObservableProperty] private bool _isPopupVisible;
    [ObservableProperty] private ScanPopupKind _popupKind = ScanPopupKind.None;
    [ObservableProperty] private string _popupTitle = string.Empty;
    [ObservableProperty] private string _popupEmployeeName = string.Empty;
    [ObservableProperty] private string _popupDetailLine1 = string.Empty;
    [ObservableProperty] private string _popupDetailLine2 = string.Empty;
    [ObservableProperty] private string _popupDetailLine3 = string.Empty;
    
    [ObservableProperty] private bool _isArrival = true;
    [ObservableProperty] private bool _autoDetect = true;

    public TerminalViewModel(
        IConnectionStateService connectionState,
        ErganiRetryService retryService,
        IEmailAlertService emailAlertService,
        ICacheSyncService cacheSync)
    {
        _connectionState = connectionState;
        _retryService = retryService;
        _emailAlertService = emailAlertService;
        _cacheSync = cacheSync;

        _clockTimer = new System.Timers.Timer(1000);
        _clockTimer.Elapsed += (_, _) => UpdateClock();
        _clockTimer.Start();
        UpdateClock();

        _popupAutoCloseTimer = new System.Timers.Timer(4000) { AutoReset = false };
        _popupAutoCloseTimer.Elapsed += (_, _) => CloseScanPopup();
    }

    public void Initialize(UserSession session)
    {
        _session = session;
        CompanyName = session.CompanyName ?? "Unknown Company";
        BranchName = session.BranchName ?? "All Branches";
        IsOfflineMode = session.IsOfflineSession;

        // Sync offline cache so scan lookups have fresh employee/schedule data
        if (session.CompanyId.HasValue)
            _ = Task.Run(() => _cacheSync.RefreshCacheFromMainDatabaseAsync(session.CompanyId.Value));
    }

    private void UpdateClock()
    {
        var now = DateTime.Now;
        CurrentTimeText = now.ToString("HH:mm:ss");
        CurrentDateText = now.ToString("dddd, d MMMM yyyy");
    }

    private void CloseScanPopup()
    {
        IsPopupVisible = false;
        PopupKind = ScanPopupKind.None;
        ScanInput = string.Empty;
    }

    // Per-employee cooldown — prevents double-scans within 20 seconds
    private readonly Dictionary<string, DateTime> _lastScanTime = new();
    private static readonly TimeSpan ScanCooldown = TimeSpan.FromSeconds(20);

    [RelayCommand]
    private async Task ProcessScanAsync()
    {
        var barcode = ScanInput?.Trim();
        ScanInput = string.Empty;

        if (string.IsNullOrWhiteSpace(barcode) || _session?.CompanyId == null || IsProcessing)
            return;

        // Use the same cooldown key for a plain AFM and an Ergani
        // serialized scan containing that AFM.
        var afm = ExtractAfm(barcode);
        var cooldownKey = afm != null
            ? $"AFM:{afm}"
            : $"BARCODE:{barcode}";

        // Cooldown check — block repeat scans of the same employee within 20 seconds.
        if (_lastScanTime.TryGetValue(cooldownKey, out var lastTime))
        {
            var elapsed = DateTime.Now - lastTime;
            if (elapsed < ScanCooldown)
            {
                var remaining = (int)Math.Ceiling((ScanCooldown - elapsed).TotalSeconds);
                ShowPopup(ScanPopupKind.TooEarlyBlocked,
                    "⏳ ALREADY SCANNED", "",
                    $"This employee was scanned {(int)elapsed.TotalSeconds}s ago.",
                    $"Please wait {remaining} more second(s).", "");
                return;
            }
        }

        IsProcessing = true;
        try
        {
            await HandleScanAsync(barcode, _session.CompanyId.Value);
            _lastScanTime[cooldownKey] = DateTime.Now;
        }
        finally
        {
            IsProcessing = false;
        }

    }

    /// <summary>
    /// Returns a normalized AFM from either a plain nine-digit value or
    /// a serialized Ergani scanner string such as:
    /// ergInm:NAME;In:FIRSTNAME;afm:AFMno;id:idNo
    /// </summary>
    private static string? ExtractAfm(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return null;

        var value = input.Trim();

        // Plain AFM: require exactly nine digits. This avoids treating
        // arbitrary text containing nine digits as an AFM.
        if (value.Length == 9 && value.All(char.IsDigit))
            return value;

        // Serialized Ergani scanner input: parse key/value pairs separated by ';'.
        if (!value.Contains(':'))
            return null;

        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var part in value.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = part.IndexOf(':');
            if (separator <= 0)
                continue;

            var key = part[..separator].Trim();
            var fieldValue = part[(separator + 1)..].Trim();

            if (key.Length > 0)
                values[key] = fieldValue;
        }

        if (!values.TryGetValue("afm", out var afm))
            return null;

        return NormalizeAfm(afm);
    }

    private static string? NormalizeAfm(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        // AFM is stored as a string so leading zeroes are preserved.
        // Accept separators/spaces in the scanned field, but require nine digits.
        var digits = new string(value.Where(char.IsDigit).ToArray());

        return digits.Length == 9 ? digits : null;
    }

    private async Task HandleScanAsync(string barcode, int companyId)
    {
        // Always read fresh from the local cache for the lookup — this is what
        // makes the terminal work identically online or offline. The cache is
        // kept warm by CacheSyncService while online.
        using var cache = LocalCacheDbContextFactory.Create();

        var scanValue = barcode.Trim();
        var afm = ExtractAfm(scanValue);

        // First try an exact barcode match. This preserves existing barcode
        // behavior, including barcodes that happen to contain nine digits.
        var employee = await cache.CachedEmployees
            .FirstOrDefaultAsync(e =>
                e.CompanyId == companyId &&
                e.BarcodeId == scanValue &&
                e.IsActive);

        // If no barcode matches, try the AFM/TaxId. ExtractAfm supports both
        // a plain nine-digit AFM and serialized Ergani scanner data.
        if (employee == null && afm != null)
        {
            employee = await cache.CachedEmployees
                .FirstOrDefaultAsync(e =>
                    e.CompanyId == companyId &&
                    e.TaxId == afm &&
                    e.IsActive);
        }

        if (employee == null)
        {
            var detail = afm != null
                ? $"No active employee with AFM '{afm}' was found for this company."
                : $"Barcode '{scanValue}' is not recognized.";

            ShowPopup(ScanPopupKind.UnknownBadge, Loc[L.UnknownBadge], "",
                detail, "Please contact your administrator.", "");
            return;
        }

        var settings = await cache.CachedCompanySettings.FindAsync(companyId);
        var today = DateOnly.FromDateTime(DateTime.Today);
        var schedule = await cache.CachedSchedules
            .FirstOrDefaultAsync(s => s.EmployeeId == employee.Id && s.ScheduleDate == today);

        // Cached state, refreshed in the background — probing the database here could hold up
        // the next person in the queue for as long as a connection timeout.
        var isOnline = _connectionState.CurrentState == AppConnectionState.Normal;

        var movementType = await DetermineMovementTypeAsync(cache, employee.Id);

        var now = DateTime.Now;

        if (movementType == "Arrival")
        {
            if (schedule == null && settings != null && settings.BlockClockInWithoutSchedule)
            {
                ShowPopup(ScanPopupKind.NoScheduleBlocked, Loc[L.NoSchedule], employee.FullName,
                    "No schedule found for today.", "Clock-in is blocked until a schedule is entered.", "");
                return;
            }

            if (schedule?.StartTime != null && settings != null)
            {
                var earliestAllowed = schedule.StartTime.Value.AddMinutes(-settings.EarlyClockInBlockMinutes);
                var nowTimeOnly = TimeOnly.FromDateTime(now);

                if (nowTimeOnly < earliestAllowed)
                {
                    var minutesRemaining = (int)Math.Ceiling(
                        (earliestAllowed.ToTimeSpan() - nowTimeOnly.ToTimeSpan()).TotalMinutes);

                    await LogBlockedAttemptAsync(cache, employee, companyId);

                    ShowPopup(ScanPopupKind.TooEarlyBlocked, Loc[L.TooEarly], employee.FullName,
                        $"{Loc[L.ShiftLabel]}: {schedule.StartTime:HH:mm}    Earliest: {earliestAllowed:HH:mm}",
                        $"{now:HH:mm}",
                        string.Format(Loc[L.PleaseReturnIn], minutesRemaining));
                    return;
                }
            }

            await QueueScanAsync(cache, employee, companyId, "Arrival", now);

            var shiftText = schedule is { StartTime: not null, EndTime: not null }
                ? $"{Loc[L.ShiftLabel]}: {schedule.StartTime:HH:mm} → {schedule.EndTime:HH:mm}"
                : "";

            ShowPopup(ScanPopupKind.Success, Loc[L.ClockInSuccess], employee.FullName,
                $"{Loc[L.Arrival]}  {now:HH:mm:ss}", shiftText,
                isOnline ? "" : Loc[L.OfflineMode]);
        }
        else // Departure
        {
            bool isEarly = false;
            int earlyMinutes = 0;

            if (schedule?.EndTime != null && settings != null)
            {
                var alertThreshold = schedule.EndTime.Value.AddMinutes(-settings.EarlyDepartureAlertMinutes);
                var nowTimeOnly = TimeOnly.FromDateTime(now);

                if (nowTimeOnly < alertThreshold)
                {
                    isEarly = true;
                    earlyMinutes = (int)Math.Round((schedule.EndTime.Value.ToTimeSpan() - nowTimeOnly.ToTimeSpan()).TotalMinutes);
                }
            }

            await QueueScanAsync(cache, employee, companyId, "Departure", now);

            if (isEarly)
            {
                if (isOnline && schedule?.EndTime != null)
                {
                    _ = _emailAlertService.SendEarlyDepartureAlertAsync(companyId, new EarlyDepartureAlertRequest
                    {
                        EmployeeFullName = employee.FullName,
                        EmployeeTaxId = string.Empty,
                        BranchName = BranchName,
                        CompanyName = CompanyName,
                        ScheduledEndTime = schedule.EndTime.Value,
                        ActualDeparture = now,
                        EarlyMinutes = earlyMinutes
                    });
                }

                ShowPopup(ScanPopupKind.EarlyDepartureWarning, Loc[L.ClockOutEarly], employee.FullName,
                    $"{Loc[L.Departure]}  {now:HH:mm:ss}",
                    $"{schedule!.EndTime:HH:mm}  •  {string.Format(Loc[L.MinutesEarly], earlyMinutes)}",
                    isOnline ? "✉️" : Loc[L.OfflineMode]);
            }
            else
            {
                var shiftText = schedule is { StartTime: not null, EndTime: not null }
                    ? $"{Loc[L.ShiftLabel]}: {schedule.StartTime:HH:mm} → {schedule.EndTime:HH:mm}  ✓"
                    : "";

                ShowPopup(ScanPopupKind.Success, Loc[L.ClockOutSuccess], employee.FullName,
                    $"{Loc[L.Departure]}  {now:HH:mm:ss}", shiftText,
                    isOnline ? "" : Loc[L.OfflineMode]);
            }
        }
    }

    /// <summary>
    /// Determines whether the next scan for this employee should be treated
    /// as an ARRIVAL or DEPARTURE. Checks the offline queue first (covers the
    /// case of multiple scans happening while disconnected), and otherwise
    /// defaults to ARRIVAL — the assumption being that the cache's notion of
    /// "today's WorkCards" is kept current by CacheSyncService while online.
    /// A fuller implementation would also check the main DB's WorkCards table
    /// directly when online for full correctness across app restarts; that
    /// hook is left as a follow-up since it requires a scoped main-DB query
    /// service rather than the cache-only context used here.
    /// </summary>
    private static async Task<string> DetermineMovementTypeAsync(LocalCacheDbContext cache, int employeeId)
    {
        var todaysQueued = await cache.PendingSubmissions
            .Where(p => p.EmployeeId == employeeId && p.ScannedAt.Date == DateTime.Today)
            .OrderBy(p => p.ScannedAt)
            .ToListAsync();

        if (todaysQueued.Count > 0)
        {
            var last = todaysQueued[^1];
            return last.MovementType == "Arrival" ? "Departure" : "Arrival";
        }

        return "Arrival";
    }

    /// <summary>
    /// Save-first: the scan is written to the local cache (a few milliseconds) and the background
    /// service sends it to Ergani, retrying for as long as the connection is down. The terminal is
    /// free for the next person immediately, whatever the state of the network or Ergani.
    /// </summary>
    private async Task QueueScanAsync(
        LocalCacheDbContext cache, CachedEmployee employee, int companyId,
        string movementType, DateTime scannedAt)
    {
        cache.PendingSubmissions.Add(new PendingSubmission
        {
            EmployeeId = employee.Id,
            CompanyId = companyId,
            BranchId = employee.BranchId,
            EmployeeBarcodeId = employee.BarcodeId,
            MovementType = movementType,
            ScannedAt = scannedAt,
            Synced = false
        });
        await cache.SaveChangesAsync();

        // Wake the background sender now; this returns immediately.
        _retryService.TriggerNow();
    }

    private static async Task LogBlockedAttemptAsync(LocalCacheDbContext cache, CachedEmployee employee, int companyId)
    {
        // Blocked early-clock-in attempts are audit-only and are never sent to
        // Ergani. Recorded here as a queued-but-marked record would overload
        // PendingSubmission's meaning, so for this phase it is logged via
        // Serilog; a dedicated BlockedAttempt cache table is a natural
        // follow-up once the Admin "blocked attempts" log viewer is built.
        Serilog.Log.Warning(
            "Blocked early clock-in attempt: Employee {EmployeeId} ({Name}), Company {CompanyId}, at {Time}",
            employee.Id, employee.FullName, companyId, DateTime.Now);

        await Task.CompletedTask;
    }

    private void ShowPopup(ScanPopupKind kind, string title, string employeeName, string line1, string line2, string line3)
    {
        PopupKind = kind;
        PopupTitle = title;
        PopupEmployeeName = employeeName;
        PopupDetailLine1 = line1;
        PopupDetailLine2 = line2;
        PopupDetailLine3 = line3;
        IsPopupVisible = true;

        _popupAutoCloseTimer.Stop();
        _popupAutoCloseTimer.Start();
    }
}
