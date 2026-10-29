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
    /// <summary>How long the result window of a scan stays on screen.</summary>
    private const int PopupSeconds = 2;

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
    [ObservableProperty] private bool _isExitConfirmVisible;

    // Colour of the result window / list lines
    public bool IsPopupOk => PopupKind == ScanPopupKind.Success;
    public bool IsPopupWarn => PopupKind == ScanPopupKind.EarlyDepartureWarning;
    public bool IsPopupBad => PopupKind is ScanPopupKind.TooEarlyBlocked or ScanPopupKind.NoScheduleBlocked
                                or ScanPopupKind.UnknownBadge or ScanPopupKind.Error;

    partial void OnPopupKindChanged(ScanPopupKind value)
    {
        OnPropertyChanged(nameof(IsPopupOk));
        OnPropertyChanged(nameof(IsPopupWarn));
        OnPropertyChanged(nameof(IsPopupBad));
    }

    /// <summary>The last scans (newest first), including refused ones.</summary>
    public System.Collections.ObjectModel.ObservableCollection<RecentScanItem> RecentScans { get; } = new();
    private const int RecentScanCount = 10;
    public bool HasRecentScans => RecentScans.Count > 0;

    // Movement of the scan being processed (for the list); empty when it could not be decided.
    private string _currentMovementText = string.Empty;

    /// <summary>Raised when the operator confirms closing the application.</summary>
    public event EventHandler? ExitRequested;

    [RelayCommand] private void AskExit() => IsExitConfirmVisible = true;
    [RelayCommand] private void CancelExit() => IsExitConfirmVisible = false;
    [RelayCommand] private void ConfirmExit() { IsExitConfirmVisible = false; ExitRequested?.Invoke(this, EventArgs.Empty); }
    [ObservableProperty] private bool _autoDetect = true;

    /// <summary>Raised when the operator signs out (Ctrl+Shift+L); the application shows the login screen again.</summary>
    public event EventHandler? LogoutRequested;

    [RelayCommand] private void Logout() => LogoutRequested?.Invoke(this, EventArgs.Empty);

    /// <summary>Stops the timers when the terminal window is replaced (sign out).</summary>
    public void Shutdown()
    {
        _clockTimer.Stop();
        _popupAutoCloseTimer.Stop();
    }

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

        _popupAutoCloseTimer = new System.Timers.Timer(PopupSeconds * 1000) { AutoReset = false };
        _popupAutoCloseTimer.Elapsed += (_, _) => CloseScanPopup();

        RecentScans.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasRecentScans));
    }

    public void Initialize(UserSession session)
    {
        _session = session;
        CompanyName = session.CompanyName ?? Loc["Vm.Unknown Company"];
        BranchName = session.BranchName ?? Loc["Vm.All Branches"];
        IsOfflineMode = session.IsOfflineSession;
        _ = Logo.RefreshAsync(session.CompanyId);

        _ = LoadRecentScansAsync(session.CompanyId);

        // Sync offline cache so scan lookups have fresh employee/schedule data
        if (session.CompanyId.HasValue)
            _ = Task.Run(() => _cacheSync.RefreshCacheFromMainDatabaseAsync(session.CompanyId.Value));
    }

    /// <summary>Shows the last accepted scans saved on this computer, so the list is not empty after a restart.</summary>
    private async Task LoadRecentScansAsync(int? companyId)
    {
        if (companyId == null) return;
        try
        {
            var items = await Task.Run(async () =>
            {
                using var cache = LocalCacheDbContextFactory.Create();
                var scans = await cache.PendingSubmissions
                    .Where(p => p.CompanyId == companyId)
                    .OrderByDescending(p => p.ScannedAt).Take(RecentScanCount).ToListAsync();
                var ids = scans.Select(s => s.EmployeeId).Distinct().ToList();
                var names = await cache.CachedEmployees.Where(e => ids.Contains(e.Id))
                    .ToDictionaryAsync(e => e.Id, e => e.FullName);
                return scans.Select(s => new RecentScanItem
                {
                    TimeText = TimeLabel(s.ScannedAt),
                    EmployeeName = names.TryGetValue(s.EmployeeId, out var n) ? n : "—",
                    MovementText = s.MovementType == "Arrival" ? Loc[L.Arrival] : Loc[L.Departure],
                    StatusText = s.Synced ? Loc[L.ScanSent] : Loc[L.ScanWaiting],
                    IsOk = s.Synced,
                    IsWarn = !s.Synced
                }).ToList();
            });

            // Scans made while this was loading are already at the top; keep them.
            foreach (var item in items)
            {
                if (RecentScans.Count >= RecentScanCount) break;
                RecentScans.Add(item);
            }
        }
        catch (Exception ex)
        {
            Serilog.Log.Warning(ex, Loc["Vm.Could not load the recent scans for the terminal."]);
        }
    }

    private static string TimeLabel(DateTime when) =>
        when.Date == DateTime.Today ? when.ToString("HH:mm:ss") : when.ToString("dd/MM HH:mm");

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
                Serilog.Log.Warning(
                    "Blocked scan: Source={ScanSource}, CompanyId={CompanyId}, ScanKey={ScanKey}, AttemptedAction={AttemptedAction}, Reason={Reason}, Time={ScanTime}",
                    "BarcodeScanView", _session.CompanyId.Value, cooldownKey, "Unknown", "Duplicate scan cooldown", DateTime.Now);
                ShowPopup(ScanPopupKind.TooEarlyBlocked,
                    Loc["Vm.⏳ ALREADY SCANNED"], "",
                    Loc["Vm.This employee was scanned"] + $" {(int)elapsed.TotalSeconds}" + Loc["Vm.s ago."],
                    Loc["Vm.Please wait"] + $" {remaining} " + Loc["Vm.more second(s)."], "");
                return;
            }
        }

        IsProcessing = true;
        _currentMovementText = string.Empty;
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
                ? Loc["Vm.No active employee with AFM"] + $" '{afm}' " + Loc["Vm.was found for this company."]
                : Loc["Vm.Barcode"]+ $" '{scanValue}' " + Loc["Vm.is not recognized."];

            ShowPopup(ScanPopupKind.UnknownBadge, Loc[L.UnknownBadge], "",
                detail, Loc["Vm.Please contact your administrator."], "");
            return;
        }

        var settings = await cache.CachedCompanySettings.FindAsync(companyId);
        // Cached state, refreshed in the background — probing the database here could hold up
        // the next person in the queue for as long as a connection timeout.
        var isOnline = _connectionState.CurrentState == AppConnectionState.Normal;

        var now = DateTime.Now;

        // Arrival or departure, and the schedule that governs this scan. For an overnight shift the
        // departure belongs to the schedule of the day the shift STARTED, not of today.
        var shift = await ShiftResolver.ResolveAsync(
            cache, employee.Id, now, settings?.EarlyClockInBlockMinutes ?? 0);
        var movementType = shift.Movement;
        var schedule = shift.Schedule;
        var isOfficialHoliday = movementType == "Arrival" &&
            CompanyHolidayCalendar.IsEnabled(settings?.OfficialHolidayDatesJson, DateOnly.FromDateTime(now));
        _currentMovementText = movementType == "Arrival" ? Loc[L.Arrival] : Loc[L.Departure];

        // Rest/Absent days block the clock-in (company setting; without cached settings the safe default is
        // to block). A departure is never blocked: it closes an arrival that already exists, and blocking
        // it would leave the employee clocked in forever.
        if (!isOfficialHoliday && movementType == "Arrival" && schedule != null && schedule.WorkType is 2 or 3 &&
            (settings == null || settings.BlockClockInOnRestOrAbsentDay))
        {
            var dayType = schedule.WorkType == 2 ? Loc["Vm.rest day"] : Loc["Vm.absence day"];
            await LogBlockedAttemptAsync(cache, employee, companyId, movementType, $"Schedule marked as {dayType}");
            SendAlert(companyId, AlertEvent.NoScheduleClockIn, employee, isOnline,
                ("Reason", ErganiManager.Data.UiText.T(schedule.WorkType == 2 ? "The schedule marks today as a rest day." : "The schedule marks today as an absence day.")),
                ("Result", ErganiManager.Data.UiText.T("Clock-in blocked")), ("Time", $"{now:HH:mm:ss}"));
            ShowPopup(ScanPopupKind.NoScheduleBlocked, Loc[L.NoSchedule], employee.FullName,
                Loc["Vm.Today is marked as a"] +$" {dayType}.",
                Loc["Vm.Clock-in/out is blocked because the employee is not scheduled to work today."], "");
            return;
        }

        if (movementType == "Arrival")
        {
            if (!isOfficialHoliday && schedule == null && settings != null && settings.BlockClockInWithoutSchedule)
            {
                await LogBlockedAttemptAsync(cache, employee, companyId, movementType, "Clock-in blocked: no schedule");
                SendAlert(companyId, AlertEvent.NoScheduleClockIn, employee, isOnline,
                    ("Reason", ErganiManager.Data.UiText.T("There is no schedule for today.")), ("Result", ErganiManager.Data.UiText.T("Clock-in blocked")), ("Time", $"{now:HH:mm:ss}"));
                ShowPopup(ScanPopupKind.NoScheduleBlocked, Loc[L.NoSchedule], employee.FullName,
                    Loc["Vm.No schedule found for today."], Loc["Vm.Clock-in is blocked until a schedule is entered."], "");
                return;
            }

            // 0 = option off. Full date-times are compared, so shifts around midnight work.
            if (!isOfficialHoliday && shift.ShiftStart is { } shiftStart && settings is { EarlyClockInBlockMinutes: > 0 })
            {
                var earliestAllowed = shiftStart.AddMinutes(-settings.EarlyClockInBlockMinutes);

                if (now < earliestAllowed)
                {
                    var minutesRemaining = (int)Math.Ceiling((earliestAllowed - now).TotalMinutes);

                    await LogBlockedAttemptAsync(cache, employee, companyId, movementType,
                        $"Clock-in blocked: too early; shift starts {shiftStart:HH:mm}, earliest allowed {earliestAllowed:HH:mm}");

                    ShowPopup(ScanPopupKind.TooEarlyBlocked, Loc[L.TooEarly], employee.FullName,
                        $"{Loc[L.ShiftLabel]}: {shiftStart:HH:mm}    Earliest: {earliestAllowed:HH:mm}",
                        $"{now:HH:mm}",
                        string.Format(Loc[L.PleaseReturnIn], minutesRemaining));
                    return;
                }
            }

            if (!await QueueScanAsync(cache, employee, companyId, "Arrival", now))
                return;

            // Alert mails for the arrival (each one only when the company selected that event).
            if (schedule == null || schedule.WorkType is 2 or 3)
            {
                SendAlert(companyId, AlertEvent.NoScheduleClockIn, employee, isOnline,
                    ("Reason", ErganiManager.Data.UiText.T(schedule == null ? "There is no schedule for today."
                        : "The schedule marks today as a rest or absence day.")),
                    ("Result", ErganiManager.Data.UiText.T("Clock-in accepted")), ("Time", $"{now:HH:mm:ss}"));
            }
            else if (shift.ShiftStart is { } plannedStart && settings is { ClockInAlertMinutes: > 0 })
            {
                var offset = (int)Math.Round((now - plannedStart).TotalMinutes);   // + late, - early
                if (offset > settings.ClockInAlertMinutes)
                    SendAlert(companyId, AlertEvent.LateClockIn, employee, isOnline,
                        ("Scheduled start", $"{plannedStart:HH:mm}"), ("Clock-in", $"{now:HH:mm:ss}"),
                        ("Late by", ErganiManager.Data.UiText.F("{0} minute(s)", offset)));
                else if (-offset > settings.ClockInAlertMinutes)
                    SendAlert(companyId, AlertEvent.EarlyClockIn, employee, isOnline,
                        ("Scheduled start", $"{plannedStart:HH:mm}"), ("Clock-in", $"{now:HH:mm:ss}"),
                        ("Early by", ErganiManager.Data.UiText.F("{0} minute(s)", -offset)));
            }

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

            // 0 = option off. The end is the full date-time of the shift (the day after the start for an
            // overnight shift), so a clock-out at 06:00 for a 22:00 → 06:00 shift is judged correctly.
            if (shift.ShiftEnd is { } shiftEnd && settings is { EarlyDepartureAlertMinutes: > 0 }
                && now < shiftEnd.AddMinutes(-settings.EarlyDepartureAlertMinutes))
            {
                isEarly = true;
                earlyMinutes = (int)Math.Round((shiftEnd - now).TotalMinutes);
            }

            if (!await QueueScanAsync(cache, employee, companyId, "Departure", now))
                return;

            if (isEarly)
            {
                if (isOnline && schedule?.EndTime != null)
                {
                    _ = _emailAlertService.SendEarlyDepartureAlertAsync(companyId, new EarlyDepartureAlertRequest
                    {
                        EmployeeFullName = employee.FullName,
                        EmployeeTaxId = employee.TaxId,
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
    /// Mails an alert about a scan (fire and forget). The mail service itself checks whether the
    /// company enabled alerts and selected this kind of event, and keeps the mail for later when the
    /// mail server cannot be reached. Nothing is sent while the main database is unreachable.
    /// </summary>
    private void SendAlert(int companyId, AlertEvent kind, CachedEmployee employee, bool isOnline,
        params (string Label, string Value)[] details)
    {
        if (!isOnline) return;

        var title = kind switch
        {
            AlertEvent.LateClockIn        => ErganiManager.Data.UiText.F("Late clock-in – {0}", employee.FullName),
            AlertEvent.EarlyClockIn       => ErganiManager.Data.UiText.F("Early clock-in – {0}", employee.FullName),
            AlertEvent.NoScheduleClockIn  => ErganiManager.Data.UiText.F("Clock-in without a schedule – {0}", employee.FullName),
            _                             => ErganiManager.Data.UiText.F("Alert – {0}", employee.FullName)
        };

        var alert = new AlertMessage { Event = kind, Title = title, OccurredAt = DateTime.Now }
            .Add("Branch", BranchName)
            .Add("Employee", $"{employee.FullName} (AFM: {employee.TaxId})");
        foreach (var (label, value) in details) alert.Add(label, value);

        _ = _emailAlertService.SendAlertAsync(companyId, alert);
    }

    /// <summary>
    /// Save-first: the scan is written to the local cache (a few milliseconds) and the background
    /// service sends it to Ergani, retrying for as long as the connection is down. The terminal is
    /// free for the next person immediately, whatever the state of the network or Ergani.
    /// </summary>
    private async Task<bool> QueueScanAsync(
        LocalCacheDbContext cache, CachedEmployee employee, int companyId,
        string movementType, DateTime scannedAt)
    {
        // Check persisted queue history as well as the in-memory cooldown. This survives
        // application restarts and still catches duplicates after the first scan synced.
        var duplicateSince = scannedAt - ScanCooldown;
        var recentDuplicate = await cache.PendingSubmissions.AnyAsync(p =>
            p.CompanyId == companyId &&
            p.EmployeeId == employee.Id &&
            p.ScannedAt >= duplicateSince &&
            p.ScannedAt <= scannedAt.AddSeconds(1));

        if (recentDuplicate)
        {
            Serilog.Log.Warning(
                "Blocked scan: Source={ScanSource}, EmployeeId={EmployeeId}, CompanyId={CompanyId}, Movement={Movement}, Reason={Reason}, Time={ScanTime}",
                "BarcodeScanView", employee.Id, companyId, movementType,
                "Persistent duplicate scan prevention", scannedAt);
            ShowPopup(ScanPopupKind.TooEarlyBlocked, Loc["Vm.⏳ ALREADY SCANNED"], employee.FullName,
                Loc["Vm.This employee was scanned recently."],
                Loc["Vm.Please wait before scanning again."], "");
            return false;
        }

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
        return true;
    }

    private static Task LogBlockedAttemptAsync(
        LocalCacheDbContext cache, CachedEmployee employee, int companyId,
        string attemptedAction, string reason)
    {
        // Audit-only: blocked scans are never queued or submitted to Ergani.
        Serilog.Log.Warning(
            "Blocked scan: Source={ScanSource}, EmployeeId={EmployeeId}, EmployeeName={EmployeeName}, Barcode={Barcode}, CompanyId={CompanyId}, AttemptedAction={AttemptedAction}, Reason={Reason}, Time={ScanTime}",
            "BarcodeScanView", employee.Id, employee.FullName, employee.BarcodeId,
            companyId, attemptedAction, reason, DateTime.Now);
        return Task.CompletedTask;
    }

    private void AddRecentScan(ScanPopupKind kind, string title, string employeeName)
    {
        RecentScans.Insert(0, new RecentScanItem
        {
            TimeText = DateTime.Now.ToString("HH:mm:ss"),
            EmployeeName = string.IsNullOrWhiteSpace(employeeName) ? "—" : employeeName,
            MovementText = _currentMovementText,
            StatusText = title,
            IsOk = kind == ScanPopupKind.Success,
            IsWarn = kind == ScanPopupKind.EarlyDepartureWarning,
            IsBad = kind is ScanPopupKind.TooEarlyBlocked or ScanPopupKind.NoScheduleBlocked
                    or ScanPopupKind.UnknownBadge or ScanPopupKind.Error
        });
        while (RecentScans.Count > RecentScanCount) RecentScans.RemoveAt(RecentScans.Count - 1);
    }

    private void ShowPopup(ScanPopupKind kind, string title, string employeeName, string line1, string line2, string line3)
    {
        AddRecentScan(kind, title, employeeName);

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
