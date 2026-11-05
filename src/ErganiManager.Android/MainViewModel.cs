using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using System.Globalization;
using Avalonia.Threading;
using System.Runtime.Versioning;

namespace ErganiManager.Android;

[SupportedOSPlatform("android23.0")]
public partial class MainViewModel : ObservableObject
{
    public MainViewModel() => MainActivity.BarcodeCaptured += OnBarcodeCaptured;

    private void OnBarcodeCaptured(string? value)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                if (IsTerminalVisible) TerminalMessage = "No barcode was detected. Try again.";
                else WorkCardScanMessage = "No barcode was detected. Try again or enter the number manually.";
                return;
            }
            WorkCardScanInput = value;
            if (IsTerminalVisible)
            {
                TerminalMessage = "QR code captured. Processing attendance...";
                SubmitTerminalScanCommand.Execute(null);
            }
            else
            {
                WorkCardScanMessage = "Barcode captured. Sending to Ergani...";
                SubmitWorkCardScanCommand.Execute(null);
            }
        });
    }

    [RelayCommand]
    private void OpenBarcodeCamera()
    {
        if (!IsAuthenticated || !IsWorkCardsVisible) return;
        MainActivity.Current?.StartBarcodeCapture();
    }

    [RelayCommand]
    private async Task SubmitWorkCardScanAsync()
    {
        if (IsWorkCardScanBusy || _api is null) return;
        if (string.IsNullOrWhiteSpace(WorkCardScanInput))
        {
            WorkCardScanMessage = "Scan a barcode or type the employee number first.";
            return;
        }
        IsWorkCardScanBusy = true;
        var scanValue = WorkCardScanInput.Trim();
        OfflineWorkCardEvent? queuedEvent = null;
        var serverAcknowledged = false;
        await _outboxSyncGate.WaitAsync();
        try
        {
            // Write to durable device storage before touching the network.
            queuedEvent = await _workCardOutbox.EnqueueAsync(scanValue, _sessionCompanyId, DateTime.Now);
            WorkCardScanMessage = "Saved on this device. Sending to the server...";
            var result = await _api.ScanWorkCardAsync(scanValue, queuedEvent.EventId, queuedEvent.CapturedAt);
            await _workCardOutbox.MarkAcknowledgedAsync(queuedEvent.EventId);
            serverAcknowledged = true;
            WorkCardScanMessage = $"{result.Status}: {result.Movement} for {result.EmployeeName} at {result.ScannedAt:HH:mm:ss}. {result.Message}";
            if (!string.Equals(result.Status, "Rejected", StringComparison.OrdinalIgnoreCase))
                WorkCardScanInput = string.Empty;

            // Reload the authoritative history after Ergani responds, then ensure the
            // just-submitted movement is visible even if the history query is slightly delayed.
            await RefreshWorkCardsAsync();
            var matching = WorkCardHistory.FirstOrDefault(x =>
                x.EmployeeId == result.EmployeeId &&
                Math.Abs((x.MovementDateTime - result.ScannedAt).TotalSeconds) <= 5);
            var updated = new MobileWorkCardHistoryItem
            {
                EmployeeId = result.EmployeeId,
                EmployeeFullName = result.EmployeeName,
                MovementType = result.Movement,
                MovementDateTime = result.ScannedAt,
                SubmittedToErgani = string.Equals(result.Status, "Submitted", StringComparison.OrdinalIgnoreCase),
                IsLocalPending = string.Equals(result.Status, "Queued", StringComparison.OrdinalIgnoreCase),
                IsLocalFailed = string.Equals(result.Status, "Rejected", StringComparison.OrdinalIgnoreCase),
                RetryAttempts = string.Equals(result.Status, "Queued", StringComparison.OrdinalIgnoreCase) ? 1 : 0,
                Protocol = result.Protocol,
                RetryError = result.Status is "Queued" or "Rejected" ? result.Message : null
            };
            if (matching is not null)
            {
                var index = WorkCardHistory.IndexOf(matching);
                WorkCardHistory[index] = updated;
            }
            else
            {
                WorkCardHistory.Insert(0, updated);
            }
        }
        catch (Exception ex)
        {
            WorkCardScanMessage = serverAcknowledged
                ? "The server accepted the event, but refreshing history failed. " + GetFullError(ex)
                : queuedEvent is null
                    ? GetFullError(ex)
                    : "Saved on this device — waiting for server synchronization. " + GetFullError(ex);
            if (queuedEvent is not null) WorkCardScanInput = string.Empty;
        }
        finally { _outboxSyncGate.Release(); IsWorkCardScanBusy = false; }
    }

    private MobileApiClient? _api;
    private readonly OfflineWorkCardOutbox _workCardOutbox = new();
    private readonly SemaphoreSlim _outboxSyncGate = new(1, 1);
    private CancellationTokenSource? _outboxSyncCts;
    private CancellationTokenSource? _notificationPollingCts;
    private long _lastNotificationId;
    private string[]? _allowedMenus;
    private string _role = string.Empty;
    private int? _sessionCompanyId;
    private string? _lastTerminalScan;
    private DateTime _lastTerminalScanAt = DateTime.MinValue;
    [ObservableProperty] private string _pageTitle = "Sign in";
    [ObservableProperty] private string _serverAddress = "https://thinkpad-p16s.local:5001";
    [ObservableProperty] private string _username = string.Empty;
    [ObservableProperty] private string _password = string.Empty;
    [ObservableProperty] private string _connectionMessage = "Connect to the HTTPS mobile API hosted beside your Ergani II installation.";
    [ObservableProperty] private string _statusMessage = "Use your existing Ergani II account. No separate mobile user database is created.";
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private bool _isAuthenticated;
    [ObservableProperty] private string _companyName = string.Empty;
    [ObservableProperty] private MobileCompany? _selectedCompany;
    [ObservableProperty] private bool _isCompanySelectionRequired;
    public ObservableCollection<MobileCompany> Companies { get; } = new();
    [ObservableProperty] private bool _hasNotificationPopup;
    [ObservableProperty] private string _notificationTitle = string.Empty;
    [ObservableProperty] private string _notificationMessage = string.Empty;
    [ObservableProperty] private long? _activeNotificationId;

    [ObservableProperty] private bool _isDailyStatusVisible;
    [ObservableProperty] private bool _isEmployeesVisible;
    public bool HasEmployees => Employees.Count > 0;
    [ObservableProperty] private bool _isWorkCardsVisible;
    [ObservableProperty] private bool _isTerminalVisible;
    [ObservableProperty] private string _terminalMessage = "Ready to scan an employee QR code.";
    [ObservableProperty] private bool _isTerminalScanBusy;
    public bool CanOpenTerminal => CanSeeMenu("Terminal");
    public bool CanOpenWorkCards => CanSeeMenu("WorkCards");
    public bool CanOpenDailyStatus => CanSeeMenu("Today");
    public bool CanOpenEmployees => CanSeeMenu("Employees");
    public bool CanOpenSchedules => CanSeeMenu("Schedules");
    public bool CanOpenOvertime => CanSeeMenu("Overtime");
    public bool CanOpenSubmissions => CanSeeMenu("SubmissionLog");
    public bool CanOpenReports => CanSeeMenu("Reports");
    public bool CanOpenSettings => _role == "Admin" && !_sessionCompanyId.HasValue;
    public bool CanOpenOverview => CanOpenWorkCards || CanOpenDailyStatus || CanOpenEmployees || CanOpenSchedules || CanOpenOvertime || CanOpenSubmissions || CanOpenReports || CanOpenSettings;
    private bool CanSeeMenu(string key)
    {
        // Operator accounts are kiosk/terminal identities, not general mobile users.
        // Enforce this locally as well as in the API response so stale or older
        // server permission payloads cannot expose other menu tiles.
        if (string.Equals(_role, "Operator", StringComparison.OrdinalIgnoreCase))
            return string.Equals(key, "Terminal", StringComparison.Ordinal);
        return _allowedMenus?.Contains(key, StringComparer.Ordinal) == true;
    }
    private void RefreshMenuPermissions()
    {
        OnPropertyChanged(nameof(CanOpenTerminal)); OnPropertyChanged(nameof(CanOpenWorkCards));
        OnPropertyChanged(nameof(CanOpenDailyStatus)); OnPropertyChanged(nameof(CanOpenEmployees));
        OnPropertyChanged(nameof(CanOpenSchedules)); OnPropertyChanged(nameof(CanOpenOvertime));
        OnPropertyChanged(nameof(CanOpenSubmissions)); OnPropertyChanged(nameof(CanOpenReports));
        OnPropertyChanged(nameof(CanOpenSettings)); OnPropertyChanged(nameof(CanOpenOverview));
        OnPropertyChanged(nameof(IsOverviewVisible)); OnPropertyChanged(nameof(IsOverviewNavigationVisible));
    }
    public bool IsOverviewNavigationVisible => IsAuthenticated && CanOpenOverview && !IsTerminalVisible && !IsCompanySelectionRequired;
    [ObservableProperty] private string _workCardScanInput = string.Empty;
    [ObservableProperty] private string _workCardScanMessage = "Scan an employee barcode with the camera or type the employee number.";
    [ObservableProperty] private bool _isWorkCardScanBusy;
    [ObservableProperty] private string _workCardFromDate = DateOnly.FromDateTime(DateTime.Today.AddDays(-30)).ToString("yyyy-MM-dd");
    [ObservableProperty] private string _workCardToDate = DateOnly.FromDateTime(DateTime.Today).ToString("yyyy-MM-dd");
    [ObservableProperty] private DateTime? _workCardFromDatePicker = DateTime.Today.AddDays(-30);
    [ObservableProperty] private DateTime? _workCardToDatePicker = DateTime.Today;
    partial void OnWorkCardFromDatePickerChanged(DateTime? value)
        => WorkCardFromDate = value?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? string.Empty;
    partial void OnWorkCardToDatePickerChanged(DateTime? value)
        => WorkCardToDate = value?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? string.Empty;
    [ObservableProperty] private MobileEmployee? _selectedWorkCardEmployee;
    [ObservableProperty] private string _workCardsMessage = "Choose a date range and refresh to load work cards.";
    public ObservableCollection<MobileWorkCardHistoryItem> WorkCardHistory { get; } = new();
    [ObservableProperty] private bool _isWorkCalendarVisible;
    [ObservableProperty] private MobileEmployee? _selectedCalendarEmployee;
    [ObservableProperty] private string _selectedCalendarMonth = DateOnly.FromDateTime(DateTime.Today).ToString("yyyy-MM");
    [ObservableProperty] private DateTime? _selectedCalendarDate = DateTime.Today;
    partial void OnSelectedCalendarDateChanged(DateTime? value)
    {
        if (value.HasValue)
            SelectedCalendarMonth = value.Value.ToString("yyyy-MM", CultureInfo.InvariantCulture);
    }
    [ObservableProperty] private string _workCalendarMessage = "Choose an employee to load the work calendar.";
    [ObservableProperty] private DateTime? _scheduleEditDatePicker = DateTime.Today;
    [ObservableProperty] private int _scheduleWorkTypeIndex;
    [ObservableProperty] private string _scheduleStartTime = string.Empty;
    [ObservableProperty] private string _scheduleEndTime = string.Empty;
    [ObservableProperty] private string _scheduleComments = string.Empty;
    partial void OnScheduleEditDatePickerChanged(DateTime? value)
    {
        if (value.HasValue)
            SelectedCalendarMonth = value.Value.ToString("yyyy-MM", CultureInfo.InvariantCulture);
    }

    public ObservableCollection<MobileScheduleDay> WorkCalendarDays { get; } = new();
    [ObservableProperty] private string _selectedStatusDate = DateOnly.FromDateTime(DateTime.Today).ToString("yyyy-MM-dd");
    [ObservableProperty] private DateTime? _selectedStatusDatePicker = DateTime.Today;
    partial void OnSelectedStatusDatePickerChanged(DateTime? value)
        => SelectedStatusDate = value?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? string.Empty;
    [ObservableProperty] private string _graceMinutes = "10";
    [ObservableProperty] private string _dailyStatusMessage = "Select a date and refresh to load attendance.";
    public ObservableCollection<MobileTodayEmployeeStatus> TodayStatuses { get; } = new();
    public ObservableCollection<MobileEmployee> Employees { get; } = new();

    // The Overview menu is shown only when no feature screen is open.
    public bool IsOverviewVisible => IsAuthenticated && CanOpenOverview && !IsCompanySelectionRequired
        && !IsDailyStatusVisible && !IsEmployeesVisible
        && !IsWorkCardsVisible && !IsWorkCalendarVisible && !IsTerminalVisible;

    partial void OnIsAuthenticatedChanged(bool value)
    { OnPropertyChanged(nameof(IsOverviewVisible)); OnPropertyChanged(nameof(IsOverviewNavigationVisible)); }
    partial void OnIsCompanySelectionRequiredChanged(bool value)
    { OnPropertyChanged(nameof(IsOverviewVisible)); OnPropertyChanged(nameof(IsOverviewNavigationVisible)); }
    partial void OnIsDailyStatusVisibleChanged(bool value) => OnPropertyChanged(nameof(IsOverviewVisible));
    partial void OnIsEmployeesVisibleChanged(bool value) => OnPropertyChanged(nameof(IsOverviewVisible));
    partial void OnIsWorkCardsVisibleChanged(bool value) => OnPropertyChanged(nameof(IsOverviewVisible));
    partial void OnIsWorkCalendarVisibleChanged(bool value) => OnPropertyChanged(nameof(IsOverviewVisible));
    partial void OnIsTerminalVisibleChanged(bool value)
    { OnPropertyChanged(nameof(IsOverviewVisible)); OnPropertyChanged(nameof(IsOverviewNavigationVisible)); }

    [RelayCommand]
    private async Task CheckApiHealthAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        try
        {
            using var healthClient = new MobileApiClient(ServerAddress);
            ConnectionMessage = "Checking API health (GET /api/mobile/health)...";
            await healthClient.CheckHealthAsync();
            ConnectionMessage = "API HEALTH OK — received HTTP 200 from /api/mobile/health. You can now sign in.";
        }
        catch (Exception ex)
        {
            ConnectionMessage = "API HEALTH FAILED — " + GetFullError(ex);
        }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private async Task ConnectAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        try
        {
            _api?.Dispose();
            _api = new MobileApiClient(ServerAddress);

            ConnectionMessage = "Checking HTTPS connection to the Mobile API...";
            await _api.CheckHealthAsync();

            ConnectionMessage = "API reachable. Signing in...";
            var session = await _api.LoginAsync(Username, Password);
            _allowedMenus = session.AllowedMenus;
            _role = session.Role;
            _sessionCompanyId = session.CompanyId;
            RefreshMenuPermissions();
            IsAuthenticated = true;
            MainActivity.Current?.RequestNotificationPermission();
            Password = string.Empty;
            ConnectionMessage = $"Signed in as {session.Username} ({session.Role}).";

            Companies.Clear();
            SelectedCompany = null;
            IsCompanySelectionRequired = false;

            if (session.CompanyId.HasValue)
            {
                CompanyName = session.CompanyName ?? "Assigned company";
                if (CanOpenEmployees || CanOpenSchedules || CanOpenWorkCards)
                    await LoadCompanyDataAsync();
                StartOutboxSynchronization();
                if (CanOpenOverview) StartNotificationPolling();
                if (CanOpenTerminal && !CanOpenOverview) OpenTerminal();
            }
            else
            {
                // Super-admin accounts have no fixed CompanyId. Load the companies
                // they can access and require an explicit selection before tenant data.
                var companies = await _api.GetCompaniesAsync();
                foreach (var company in companies) Companies.Add(company);
                IsCompanySelectionRequired = true;
                CompanyName = "No company selected";
                StatusMessage = companies.Count == 0
                    ? "No active companies are available to this account."
                    : "Choose a company below before loading employees or daily status.";
            }
            PageTitle = IsTerminalVisible ? "Terminal" : "Overview";
        }
        catch (Exception ex)
        {
            IsAuthenticated = false;
            ConnectionMessage = GetFullError(ex);
            StatusMessage = "Connection diagnostics are shown above. If the API health check succeeds, verify the login response and credentials.";
            _api?.Dispose(); _api = null;
        }
        finally { IsBusy = false; }
    }

    private void StartNotificationPolling()
    {
        _notificationPollingCts?.Cancel();
        _notificationPollingCts?.Dispose();
        _notificationPollingCts = new CancellationTokenSource();
        _lastNotificationId = 0;
        _ = PollNotificationsAsync(_notificationPollingCts.Token);
    }

    private async Task PollNotificationsAsync(CancellationToken cancellationToken)
    {
        if (_api is null) return;
        try
        {
            // Load recent unread alerts on sign-in, then poll only for newly-created rows.
            var initial = await _api.GetNotificationsAsync(0, cancellationToken);
            _lastNotificationId = initial.Count == 0 ? 0 : initial.Max(x => x.Id);
            var unread = initial.Where(x => !x.IsRead).OrderBy(x => x.Id).ToList();
            if (unread.Count > 0)
                await ShowNotificationAsync(unread[^1]);

            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(10));
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                if (_api is null || !IsAuthenticated || IsCompanySelectionRequired) continue;
                var items = await _api.GetNotificationsAsync(_lastNotificationId, cancellationToken);
                foreach (var item in items.OrderBy(x => x.Id))
                {
                    _lastNotificationId = Math.Max(_lastNotificationId, item.Id);
                    await ShowNotificationAsync(item);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch
        {
            // Temporary API/network outages must not interrupt the mobile workspace.
            if (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(15), cancellationToken);
                    if (!cancellationToken.IsCancellationRequested) StartNotificationPolling();
                }
                catch (OperationCanceledException) { }
            }
        }
    }

    private async Task ShowNotificationAsync(MobileNotification item)
    {
        // Post a native Android notification in both foreground and background.
        // Android can therefore display its system heads-up banner over the current app.
        var context = global::Android.App.Application.Context;
        AndroidNotificationService.Show(context, item);

        // Keep the in-app notification as well, so the alert remains visible in the workspace.
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            NotificationTitle = item.Title;
            NotificationMessage = item.Message;
            ActiveNotificationId = item.Id;
            HasNotificationPopup = true;
        });
    }

    [RelayCommand]
    private async Task DismissNotificationAsync()
    {
        var id = ActiveNotificationId;
        HasNotificationPopup = false;
        ActiveNotificationId = null;
        if (id.HasValue && _api is not null)
        {
            try { await _api.MarkNotificationReadAsync(id.Value); }
            catch { /* Keep dismissal local if the API is temporarily unavailable. */ }
        }
    }

    private static string GetFullError(Exception exception)
    {
        var messages = new List<string>();
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (!string.IsNullOrWhiteSpace(current.Message) && (messages.Count == 0 || messages[^1] != current.Message))
                messages.Add(current.Message);
        }
        return string.Join(" → ", messages);
    }

    [RelayCommand]
    private async Task LogoutAsync()
    {
        _notificationPollingCts?.Cancel();
        _notificationPollingCts?.Dispose();
        _outboxSyncCts?.Cancel();
        _outboxSyncCts?.Dispose();
        _outboxSyncCts = null;
        _notificationPollingCts = null;
        HasNotificationPopup = false;
        if (_api is not null) { try { await _api.LogoutAsync(); } catch { } _api.Dispose(); _api = null; }
        Employees.Clear();
        OnPropertyChanged(nameof(HasEmployees));
        WorkCardHistory.Clear();
        WorkCalendarDays.Clear();
        TodayStatuses.Clear();
        Companies.Clear();
        SelectedCompany = null;
        SelectedWorkCardEmployee = null;
        SelectedCalendarEmployee = null;
        IsCompanySelectionRequired = false;

        // Reset every authenticated page before returning to the login screen.
        IsEmployeesVisible = false;
        IsWorkCardsVisible = false;
        IsWorkCalendarVisible = false;
        IsDailyStatusVisible = false;
        IsTerminalVisible = false;
        _allowedMenus = null; _role = string.Empty; _sessionCompanyId = null;
        RefreshMenuPermissions();
        IsAuthenticated = false;

        Password = string.Empty;
        CompanyName = string.Empty;
        PageTitle = "Sign in";
        ConnectionMessage = "Signed out. Sign in again to continue.";
        StatusMessage = "Sign in with your existing Ergani II account.";
    }

    [RelayCommand]
    private async Task SelectCompanyAsync()
    {
        if (IsBusy || _api is null || SelectedCompany is null) return;
        IsBusy = true;
        try
        {
            var selected = await _api.SelectCompanyAsync(SelectedCompany.Id);
            CompanyName = selected.Name;
            _sessionCompanyId = selected.Id;
            IsCompanySelectionRequired = false;
            if (CanOpenEmployees || CanOpenSchedules || CanOpenWorkCards)
                await LoadCompanyDataAsync();
            StartOutboxSynchronization();
            if (CanOpenOverview) StartNotificationPolling();
            StatusMessage = $"Connected to {CompanyName}. {Employees.Count} active employees loaded from Ergani II.";
            if (CanOpenTerminal && !CanOpenOverview) OpenTerminal();
        }
        catch (Exception ex)
        {
            StatusMessage = GetFullError(ex);
            IsCompanySelectionRequired = true;
        }
        finally { IsBusy = false; }
    }

    private void StartOutboxSynchronization()
    {
        _outboxSyncCts?.Cancel();
        _outboxSyncCts?.Dispose();
        _outboxSyncCts = new CancellationTokenSource();
        _ = RunOutboxSynchronizationAsync(_outboxSyncCts.Token);
    }

    private async Task RunOutboxSynchronizationAsync(CancellationToken cancellationToken)
    {
        // Retry on login and periodically while the app is open. A failed request
        // never removes the local event; the next cycle retries the same event ID.
        while (!cancellationToken.IsCancellationRequested)
        {
            try { await FlushOfflineWorkCardsAsync(cancellationToken); }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { break; }
            catch { /* Keep the durable outbox intact and retry on the next cycle. */ }
            try { await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken); }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { break; }
        }
    }

    private async Task FlushOfflineWorkCardsAsync(CancellationToken cancellationToken)
    {
        if (_api is null || !IsAuthenticated || !_sessionCompanyId.HasValue) return;
        if (!await _outboxSyncGate.WaitAsync(0, cancellationToken)) return;
        try
        {
            var pending = await _workCardOutbox.GetPendingAsync(cancellationToken);
            foreach (var item in pending)
            {
                if (cancellationToken.IsCancellationRequested || _api is null) break;
                // Never send one company's queued scan under another company's session.
                if (item.CompanyId.HasValue && item.CompanyId.Value != _sessionCompanyId.Value) continue;
                try
                {
                    await _api.ScanWorkCardAsync(item.ScanValue, item.EventId, item.CapturedAt, cancellationToken);
                    await _workCardOutbox.MarkAcknowledgedAsync(item.EventId, cancellationToken);
                }
                catch
                {
                    // Stop at the first failure to preserve scan order and avoid hammering
                    // the API while it is unavailable.
                    break;
                }
            }
        }
        finally { _outboxSyncGate.Release(); }
    }

    private async Task LoadCompanyDataAsync()
    {
        if (_api is null) return;
        var employees = await _api.GetEmployeesAsync();
        Employees.Clear();
        foreach (var employee in employees) Employees.Add(employee);
        OnPropertyChanged(nameof(HasEmployees));
        StatusMessage = $"Connected to {CompanyName}. {Employees.Count} active employees loaded from Ergani II.";
    }

    [RelayCommand]
    private void OpenTerminal()
    {
        if (!IsAuthenticated || !CanOpenTerminal || IsCompanySelectionRequired) return;
        IsDailyStatusVisible = false; IsEmployeesVisible = false; IsWorkCardsVisible = false;
        IsWorkCalendarVisible = false; IsTerminalVisible = true; PageTitle = "Terminal";
        TerminalMessage = "Ready. Scan an employee QR code or enter the employee number.";
    }

    [RelayCommand]
    private void BackFromTerminal()
    {
        IsTerminalVisible = false;
        if (CanOpenOverview) PageTitle = "Overview";
        else if (CanOpenTerminal) OpenTerminal();
    }

    [RelayCommand]
    private void OpenTerminalCamera()
    {
        if (!IsTerminalVisible || IsTerminalScanBusy) return;
        MainActivity.Current?.StartBarcodeCapture();
    }

    [RelayCommand]
    private async Task SubmitTerminalScanAsync()
    {
        if (IsTerminalScanBusy || _api is null || !IsTerminalVisible) return;
        var scan = WorkCardScanInput.Trim();
        if (string.IsNullOrWhiteSpace(scan)) { TerminalMessage = "Scan a QR code or enter an employee number."; return; }
        IsTerminalScanBusy = true;
        if (string.Equals(scan, _lastTerminalScan, StringComparison.Ordinal) && DateTime.UtcNow - _lastTerminalScanAt < TimeSpan.FromSeconds(20))
        {
            TerminalMessage = "Duplicate scan ignored. Please wait before scanning the same code again.";
            WorkCardScanInput = string.Empty;
            IsTerminalScanBusy = false;
            await Task.Delay(1300);
            if (IsTerminalVisible) MainActivity.Current?.StartBarcodeCapture();
            return;
        }
        try
        {
            var result = await _api.ScanWorkCardAsync(scan);
            _lastTerminalScan = scan; _lastTerminalScanAt = DateTime.UtcNow;
            TerminalMessage = $"{result.Status}: {result.Movement} · {result.EmployeeName} · {result.ScannedAt:HH:mm:ss}\n{result.Message}";
            WorkCardScanInput = string.Empty;
        }
        catch (Exception ex) { TerminalMessage = GetFullError(ex); }
        finally
        {
            IsTerminalScanBusy = false;
            if (IsTerminalVisible)
            {
                await Task.Delay(1300);
                if (IsTerminalVisible && !IsTerminalScanBusy)
                    MainActivity.Current?.StartBarcodeCapture();
            }
        }
    }

    [RelayCommand]
    private async Task OpenWorkCardsAsync()
    {
        if (!IsAuthenticated || _api is null || !CanOpenWorkCards) { StatusMessage = "You do not have permission to open work cards."; return; }
        if (IsCompanySelectionRequired) { StatusMessage = "Select a company before opening work cards."; return; }
        IsDailyStatusVisible = false;
        IsEmployeesVisible = false;
        IsWorkCalendarVisible = false;
        IsTerminalVisible = false;
        IsWorkCardsVisible = true;
        PageTitle = "Work cards";
        await RefreshWorkCardsAsync();
    }

    [RelayCommand]
    private async Task RefreshWorkCardsAsync()
    {
        if (IsBusy || _api is null) return;
        if (!DateOnly.TryParseExact(WorkCardFromDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var fromDate) ||
            !DateOnly.TryParseExact(WorkCardToDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var toDate))
        {
            WorkCardsMessage = "Enter both dates as yyyy-MM-dd.";
            return;
        }
        if (fromDate > toDate)
        {
            WorkCardsMessage = "The start date must be on or before the end date.";
            return;
        }

        IsBusy = true;
        try
        {
            var rows = await _api.GetWorkCardHistoryAsync(fromDate, toDate, SelectedWorkCardEmployee?.Id);
            WorkCardHistory.Clear();
            foreach (var row in rows.OrderByDescending(x => x.MovementDateTime))
                WorkCardHistory.Add(row);
            WorkCardsMessage = $"{rows.Count} work card(s) · {fromDate:dd/MM/yyyy} – {toDate:dd/MM/yyyy}";
        }
        catch (Exception ex)
        {
            WorkCardsMessage = GetFullError(ex);
        }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private void BackFromWorkCards()
    {
        IsWorkCardsVisible = false;
        PageTitle = "Overview";
    }
    [RelayCommand]
    private async Task OpenDailyStatusAsync()
    {
        if (!IsAuthenticated || _api is null || !CanOpenDailyStatus) { StatusMessage = "You do not have permission to open daily status."; return; }
        if (IsCompanySelectionRequired) { StatusMessage = "Select a company before opening daily status."; return; }
        IsWorkCalendarVisible = false;
        IsEmployeesVisible = false;
        IsDailyStatusVisible = true;
        PageTitle = "Daily status";
        await RefreshDailyStatusAsync();
    }

    [RelayCommand]
    private async Task RefreshDailyStatusAsync()
    {
        if (IsBusy || _api is null) return;
        if (!DateOnly.TryParseExact(SelectedStatusDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        { DailyStatusMessage = "Enter date as yyyy-MM-dd."; return; }
        if (!int.TryParse(GraceMinutes, out var grace) || grace < 0 || grace > 240)
        { DailyStatusMessage = "Grace minutes must be between 0 and 240."; return; }
        IsBusy = true;
        try
        {
            var rows = await _api.GetTodayStatusAsync(date, grace);
            TodayStatuses.Clear();
            foreach (var row in rows) TodayStatuses.Add(row);
            DailyStatusMessage = rows.Count + " employees · " + date.ToString("dd/MM/yyyy");
        }
        catch (Exception ex) { DailyStatusMessage = GetFullError(ex); }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private void BackToOverview() { IsDailyStatusVisible = false; PageTitle = "Overview"; }
    [RelayCommand]
    private void OpenEmployees()
    {
        if (!IsAuthenticated || !CanOpenEmployees) { StatusMessage = "You do not have permission to open employees."; return; }
        if (IsCompanySelectionRequired) { StatusMessage = "Select a company before opening employees."; return; }
        IsDailyStatusVisible = false;
        IsWorkCardsVisible = false;
        IsWorkCalendarVisible = false;
        IsEmployeesVisible = true;
        PageTitle = "Employees";
    }

    [RelayCommand]
    private void BackFromEmployees()
    {
        IsEmployeesVisible = false;
        PageTitle = "Overview";
    }
    [RelayCommand]
    private async Task OpenSchedulesAsync()
    {
        if (!IsAuthenticated || _api is null || !CanOpenSchedules) { StatusMessage = "You do not have permission to open the work calendar."; return; }
        if (IsCompanySelectionRequired) { StatusMessage = "Select a company before opening the work calendar."; return; }
        IsDailyStatusVisible = false;
        IsEmployeesVisible = false;
        IsWorkCardsVisible = false;
        IsTerminalVisible = false;
        IsWorkCalendarVisible = true;
        PageTitle = "Work calendar";
        SelectedCalendarEmployee ??= Employees.FirstOrDefault();
        await RefreshWorkCalendarAsync();
    }

    [RelayCommand]
    private async Task RefreshWorkCalendarAsync()
    {
        if (IsBusy || _api is null) return;
        if (SelectedCalendarEmployee is null) { WorkCalendarMessage = "No active employee is available."; return; }
        if (!DateOnly.TryParseExact(SelectedCalendarMonth + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var month))
        { WorkCalendarMessage = "Select a valid month."; return; }
        IsBusy = true;
        try
        {
            var rows = await _api.GetScheduleMonthAsync(SelectedCalendarEmployee.Id, month.Year, month.Month);
            WorkCalendarDays.Clear();
            foreach (var row in rows.OrderBy(x => x.ScheduleDate)) WorkCalendarDays.Add(row);
            WorkCalendarMessage = $"{SelectedCalendarEmployee.FirstName} {SelectedCalendarEmployee.LastName} · {month.ToString("MMMM yyyy", CultureInfo.CurrentCulture)} · {rows.Count} scheduled day(s)";
        }
        catch (Exception ex) { WorkCalendarMessage = GetFullError(ex); }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private void LoadSelectedScheduleDay()
    {
        if (SelectedCalendarEmployee is null || ScheduleEditDatePicker is null)
        {
            WorkCalendarMessage = "Choose an employee and a schedule date first.";
            return;
        }

        var date = DateOnly.FromDateTime(ScheduleEditDatePicker.Value);
        var day = WorkCalendarDays.FirstOrDefault(x =>
            x.EmployeeId == SelectedCalendarEmployee.Id && x.ScheduleDate == date);

        if (day is null)
        {
            ScheduleWorkTypeIndex = 0;
            ScheduleStartTime = string.Empty;
            ScheduleEndTime = string.Empty;
            ScheduleComments = string.Empty;
            WorkCalendarMessage = $"No schedule exists for {date:dd/MM/yyyy}. Enter a new day below.";
            return;
        }

        ScheduleWorkTypeIndex = day.WorkType;
        var periods = day.WorkingPeriods.OrderBy(x => x.PeriodNumber).ToList();
        ScheduleStartTime = periods.Count > 0
            ? periods[0].StartTime.ToString("HH:mm")
            : day.StartTime?.ToString("HH:mm") ?? string.Empty;
        ScheduleEndTime = periods.Count > 0
            ? periods[^1].EndTime.ToString("HH:mm")
            : day.EndTime?.ToString("HH:mm") ?? string.Empty;
        ScheduleComments = day.Comments ?? string.Empty;
        WorkCalendarMessage = $"Loaded {date:dd/MM/yyyy}. Changes to an already-submitted day will be marked as pending.";
    }

    [RelayCommand]
    private async Task SaveScheduleDayAsync()
    {
        if (_api is null || SelectedCalendarEmployee is null || ScheduleEditDatePicker is null)
        {
            WorkCalendarMessage = "Choose an employee and a schedule date first.";
            return;
        }

        var date = DateOnly.FromDateTime(ScheduleEditDatePicker.Value);
        TimeOnly? start = null;
        TimeOnly? end = null;
        if (ScheduleWorkTypeIndex is 0 or 1)
        {
            if (!TimeOnly.TryParse(ScheduleStartTime, CultureInfo.InvariantCulture, out var parsedStart) ||
                !TimeOnly.TryParse(ScheduleEndTime, CultureInfo.InvariantCulture, out var parsedEnd))
            {
                WorkCalendarMessage = "Enter valid start and end times in HH:mm format.";
                return;
            }
            start = parsedStart;
            end = parsedEnd;
        }

        IsBusy = true;
        try
        {
            await _api.SaveScheduleDayAsync(
                SelectedCalendarEmployee.Id, date, ScheduleWorkTypeIndex,
                start, end, string.IsNullOrWhiteSpace(ScheduleComments) ? null : ScheduleComments.Trim());
            WorkCalendarMessage = $"Schedule for {date:dd/MM/yyyy} saved. It is pending submission to Ergani.";
        }
        catch (Exception ex)
        {
            WorkCalendarMessage = GetFullError(ex);
        }
        finally
        {
            IsBusy = false;
        }

        await RefreshWorkCalendarAsync();
    }

    [RelayCommand]
    private async Task PreviousCalendarMonthAsync()
    {
        if (DateOnly.TryParseExact(SelectedCalendarMonth + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var month))
        {
            var previousMonth = month.AddMonths(-1);
            SelectedCalendarMonth = previousMonth.ToString("yyyy-MM");
            SelectedCalendarDate = previousMonth.ToDateTime(TimeOnly.MinValue);
            await RefreshWorkCalendarAsync();
        }
    }

    [RelayCommand]
    private async Task NextCalendarMonthAsync()
    {
        if (DateOnly.TryParseExact(SelectedCalendarMonth + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var month))
        {
            var nextMonth = month.AddMonths(1);
            SelectedCalendarMonth = nextMonth.ToString("yyyy-MM");
            SelectedCalendarDate = nextMonth.ToDateTime(TimeOnly.MinValue);
            await RefreshWorkCalendarAsync();
        }
    }

    [RelayCommand] private void BackFromWorkCalendar() { IsWorkCalendarVisible = false; PageTitle = "Overview"; }
    [RelayCommand]
    private void GoToOverview()
    {
        if (!IsAuthenticated || !CanOpenOverview) return;
        if (IsCompanySelectionRequired) return;

        IsDailyStatusVisible = false;
        IsEmployeesVisible = false;
        IsWorkCardsVisible = false;
        IsWorkCalendarVisible = false;
        IsTerminalVisible = false;
        PageTitle = "Overview";
    }

    [RelayCommand] private void OpenOvertime() { if (CanOpenOvertime) SetPage("Overtime"); }
    [RelayCommand] private void OpenSubmissions() { if (CanOpenSubmissions) SetPage("API submissions"); }
    [RelayCommand] private void OpenReports() { if (CanOpenReports) SetPage("Reports"); }
    [RelayCommand] private void OpenSettings() { if (CanOpenSettings) SetPage("Settings"); }
    private void SetPage(string page)
    {
        if (IsCompanySelectionRequired)
        {
            StatusMessage = "Select a company before opening " + page + ".";
            return;
        }
        IsDailyStatusVisible = false;
        IsEmployeesVisible = false;
        IsWorkCalendarVisible = false;
        IsWorkCardsVisible = false;
        IsTerminalVisible = false;
        PageTitle = page;
        StatusMessage = page == "Employees" && IsAuthenticated
            ? $"{Employees.Count} active employees loaded from the existing employee service."
            : $"{page} will be connected through existing Ergani II Core services.";
    }
}


public sealed class MobileWorkCardHistoryItem
{
    public int Id { get; set; }
    public int EmployeeId { get; set; }
    public string EmployeeFullName { get; set; } = string.Empty;
    public string EmployeeTaxId { get; set; } = string.Empty;
    public string BranchName { get; set; } = string.Empty;
    public string MovementType { get; set; } = string.Empty;
    public DateTime MovementDateTime { get; set; }
    public string? LateJustification { get; set; }
    public bool IsAutomatic { get; set; }
    public bool SubmittedToErgani { get; set; }
    public string? Protocol { get; set; }
    public DateOnly SubmissionDate { get; set; }
    public bool WasEarlyDeparture { get; set; }
    public int? EarlyDepartureMinutes { get; set; }
    public int? LocalPendingId { get; set; }
    public int? LocalFailedId { get; set; }
    public bool IsLocalPending { get; set; }
    public bool IsLocalFailed { get; set; }
    public int RetryAttempts { get; set; }
    public string? RetryError { get; set; }

    public string MovementText => MovementType == "Arrival" ? "Arrival" : "Departure";
    public string DateTimeText => MovementDateTime.ToString("dd/MM/yyyy HH:mm:ss");
    public string LateReasonText => LateJustification switch
    {
        "001" => "Power / telecommunications outage",
        "002" => "Employer systems problem",
        "003" => "Problem connecting to ERGANI",
        _ => string.Empty
    };
    public bool HasLateReason => !string.IsNullOrWhiteSpace(LateJustification);
    public string StatusText =>
        IsLocalPending ? (RetryAttempts > 0
            ? $"Waiting to send · attempt {RetryAttempts}" + (string.IsNullOrWhiteSpace(RetryError) ? string.Empty : $" · {RetryError}")
            : "Waiting to send" + (string.IsNullOrWhiteSpace(RetryError) ? string.Empty : $" · {RetryError}")) :
        IsLocalFailed ? (RetryAttempts > 0
            ? "Failed — retry pending" + (string.IsNullOrWhiteSpace(RetryError) ? string.Empty : $" · {RetryError}")
            : "Rejected by Ergani" + (string.IsNullOrWhiteSpace(RetryError) ? string.Empty : $" · {RetryError}")) :
        SubmittedToErgani ? "Sent to Ergani" + (string.IsNullOrWhiteSpace(Protocol) ? string.Empty : $" · Protocol: {Protocol}") :
        "Not submitted";
}
