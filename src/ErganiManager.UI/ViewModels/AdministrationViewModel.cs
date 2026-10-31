using System;
using Avalonia.Threading;
using System.Collections.Generic;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ErganiManager.Core.Interfaces;
using ErganiManager.Core.Models;
using System.Collections.ObjectModel;
using System.Linq;
using System.Net.Http;
using ErganiManager.UI.Localization;
using ErganiManager.ErganiApi;
using ErganiManager.ErganiApi.Services;
using ErganiManager.Data;
using ErganiManager.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace ErganiManager.UI.ViewModels;

/// <summary>Administrator tools: database backup, delete database, delete local cache.</summary>
public partial class AdministrationViewModel : ViewModelBase, IAdminSectionViewModel
{
    private enum PendingAction { None, DeleteCache, DeleteDatabase }

    private readonly IDatabaseMaintenanceService _maintenance;
    private PendingAction _pending = PendingAction.None;

    /// <summary>Raised after the database was deleted — the app returns to the setup wizard.</summary>
    public event EventHandler? DatabaseDeleted;

    [ObservableProperty] private string _databaseInfo = string.Empty;
    [ObservableProperty] private string _cacheInfo = string.Empty;
    [ObservableProperty] private string _statusMessage = string.Empty;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private bool _isDatabaseOptionsView;

    public string FormTitle => IsDatabaseOptionsView ? Loc["Database Options"] : Loc["Application Tools"];
    partial void OnIsDatabaseOptionsViewChanged(bool value) => OnPropertyChanged(nameof(FormTitle));

    [ObservableProperty] private bool _isConfirmOpen;
    [ObservableProperty] private string _confirmText = string.Empty;
    [ObservableProperty] private string _confirmPhraseInput = string.Empty;
    [ObservableProperty] private bool _isDeleteDatabaseConfirmation;
    [ObservableProperty] private string _lastBackupInfo = string.Empty;

    private readonly ErganiManager.UI.Services.AutomaticDatabaseBackupService _automaticBackup;
    [ObservableProperty] private bool _automaticBackupEnabled;
    [ObservableProperty] private int _automaticBackupIntervalHours = 24;
    [ObservableProperty] private int _automaticBackupRetentionCount = 14;
    [ObservableProperty] private string _automaticBackupFolder = string.Empty;
    [ObservableProperty] private string _automaticBackupStatus = string.Empty;
    [ObservableProperty] private string _automaticBackupLastSuccess = string.Empty;
    [ObservableProperty] private string _automaticBackupNextRun = string.Empty;

    private readonly IRoundingSettingsService _rounding;
    private readonly IPortalSettingsService _portal;
    private readonly ICompanyService _companyService;
    private readonly ICacheSyncService _cacheSync;
    private readonly IOfficialHolidayCatalogService _holidayCatalog;
    private readonly IEmailAlertService _emailAlertService;
    private readonly IInternetCheckService _internetCheckService;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IErganiHealthCheckService _erganiHealthCheckService;
    private readonly IConnectionStateService _connectionState;
    private readonly ICredentialProtector _credentialProtector;
    private int? _activeCompanyId;
    private CompanyDto? _loadedCompanySettings;
    private bool _loadingCompanySettings;

    [ObservableProperty] private DateTimeOffset? _newOfficialHolidayDate = DateTimeOffset.Now;
    [ObservableProperty] private string _newOfficialHolidayName = string.Empty;

    public ObservableCollection<OfficialHolidayEdit> OfficialHolidays { get; } = new();
    // Employee terminal announcements (company-wide, retained in the main database).
    public ObservableCollection<TerminalAnnouncementEdit> TerminalAnnouncements { get; } = new();
    [ObservableProperty] private string _newTerminalAnnouncement = string.Empty;
    [ObservableProperty] private string _terminalAnnouncementsStatus = string.Empty;

    // Weather configuration is stored on each branch so each terminal can use its own location.
    public ObservableCollection<BusinessBranch> WeatherBranches { get; } = new();
    [ObservableProperty] private int? _selectedWeatherBranchId;
    [ObservableProperty] private bool _weatherShowTemperature = true;
    [ObservableProperty] private bool _weatherShowCondition = true;
    [ObservableProperty] private bool _weatherShowHumidity = true;
    [ObservableProperty] private bool _weatherShowWind;
    [ObservableProperty] private bool _weatherShowFeelsLike;
    [ObservableProperty] private bool _weatherShowLocation = true;
    [ObservableProperty] private string _weatherManualAddress = string.Empty;
    [ObservableProperty] private string _weatherLatitude = string.Empty;
    [ObservableProperty] private string _weatherLongitude = string.Empty;
    [ObservableProperty] private string _weatherPreviewText = string.Empty;
    [ObservableProperty] private string _weatherSettingsStatus = string.Empty;

    partial void OnSelectedWeatherBranchIdChanged(int? value)
    {
        var branch = WeatherBranches.FirstOrDefault(x => x.Id == value);
        if (branch == null) return;
        try
        {
            WeatherShowTemperature = branch.WeatherShowTemperature;
            WeatherShowCondition = branch.WeatherShowCondition;
            WeatherShowHumidity = branch.WeatherShowHumidity;
            WeatherShowWind = branch.WeatherShowWind;
            WeatherShowFeelsLike = branch.WeatherShowFeelsLike;
            WeatherShowLocation = branch.WeatherShowLocation;
            WeatherManualAddress = branch.WeatherManualAddress ?? string.Empty;
            WeatherLatitude = branch.WeatherLatitude?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;
            WeatherLongitude = branch.WeatherLongitude?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;
            UpdateWeatherPreview();
        }
        catch (Exception ex)
        {
            WeatherSettingsStatus = "❌ " + ex.Message;
        }
    }

    partial void OnWeatherShowTemperatureChanged(bool value) => UpdateWeatherPreview();
    partial void OnWeatherShowConditionChanged(bool value) => UpdateWeatherPreview();
    partial void OnWeatherShowHumidityChanged(bool value) => UpdateWeatherPreview();
    partial void OnWeatherShowWindChanged(bool value) => UpdateWeatherPreview();
    partial void OnWeatherShowFeelsLikeChanged(bool value) => UpdateWeatherPreview();
    partial void OnWeatherShowLocationChanged(bool value) => UpdateWeatherPreview();
    partial void OnWeatherManualAddressChanged(string value) => UpdateWeatherPreview();

    private void UpdateWeatherPreview()
    {
        var parts = new List<string>();
        var selectedBranch = WeatherBranches.FirstOrDefault(x => x.Id == SelectedWeatherBranchId);
        var previewLocation = !string.IsNullOrWhiteSpace(WeatherManualAddress)
            ? WeatherManualAddress.Trim()
            : selectedBranch?.Name ?? Loc["Vm.Branch location"];
        if (WeatherShowLocation) parts.Add(previewLocation);
        if (WeatherShowTemperature) parts.Add("22°C");
        if (WeatherShowCondition) parts.Add("Partly cloudy");
        if (WeatherShowFeelsLike) parts.Add("Feels 23°");
        if (WeatherShowHumidity) parts.Add("Humidity 58%");
        if (WeatherShowWind) parts.Add("Wind 12 km/h");
        WeatherPreviewText = parts.Count == 0 ? Loc["Vm.No weather details selected"] : string.Join("  ·  ", parts);
    }

    [RelayCommand]
    private async Task SaveWeatherSettingsAsync()
    {
        if (SelectedWeatherBranchId is not int branchId || _activeCompanyId is not int companyId)
        {
            WeatherSettingsStatus = Loc["Vm.Select a branch first"];
            return;
        }
        double? latitude = null, longitude = null;
        if (!string.IsNullOrWhiteSpace(WeatherLatitude) &&
            !double.TryParse(WeatherLatitude.Replace(',', '.'), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var lat))
        {
            WeatherSettingsStatus = Loc["Vm.Enter valid coordinates"];
            return;
        }
        else if (!string.IsNullOrWhiteSpace(WeatherLatitude)) latitude = double.Parse(WeatherLatitude.Replace(',', '.'), System.Globalization.CultureInfo.InvariantCulture);
        if (!string.IsNullOrWhiteSpace(WeatherLongitude) &&
            !double.TryParse(WeatherLongitude.Replace(',', '.'), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var lon))
        {
            WeatherSettingsStatus = Loc["Vm.Enter valid coordinates"];
            return;
        }
        else if (!string.IsNullOrWhiteSpace(WeatherLongitude)) longitude = double.Parse(WeatherLongitude.Replace(',', '.'), System.Globalization.CultureInfo.InvariantCulture);
        if (latitude.HasValue != longitude.HasValue || (latitude is < -90 or > 90) || (longitude is < -180 or > 180))
        {
            WeatherSettingsStatus = Loc["Vm.Enter valid coordinates"];
            return;
        }
        try
        {
            await using var db = new AppDbContext(_connectionState.GetDbOptions());
            var branch = await db.Branches.FirstOrDefaultAsync(x => x.Id == branchId && x.CompanyId == companyId);
            if (branch == null) return;
            branch.WeatherShowTemperature = WeatherShowTemperature;
            branch.WeatherShowCondition = WeatherShowCondition;
            branch.WeatherShowHumidity = WeatherShowHumidity;
            branch.WeatherShowWind = WeatherShowWind;
            branch.WeatherShowFeelsLike = WeatherShowFeelsLike;
            branch.WeatherShowLocation = WeatherShowLocation;
            branch.WeatherManualAddress = string.IsNullOrWhiteSpace(WeatherManualAddress) ? null : WeatherManualAddress.Trim();
            branch.WeatherLatitude = latitude;
            branch.WeatherLongitude = longitude;
            await db.SaveChangesAsync();
            WeatherSettingsStatus = Loc["Vm.Weather settings saved"];
            await LoadWeatherBranchesAsync();
            SelectedWeatherBranchId = branchId;
        }
        catch (Exception ex) { WeatherSettingsStatus = "❌ " + ex.Message; }
    }

    [RelayCommand]
    private async Task TestWeatherLocationAsync()
    {
        if (double.TryParse(WeatherLatitude, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var lat) &&
            double.TryParse(WeatherLongitude, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var lon))
        {
            if (lat is >= -90 and <= 90 && lon is >= -180 and <= 180)
            {
                WeatherSettingsStatus = Loc["Vm.Coordinates look valid; save settings to use this location"];
                return;
            }
            WeatherSettingsStatus = Loc["Vm.Enter valid coordinates"];
            return;
        }

        var address = WeatherManualAddress.Trim();
        if (address.Length == 0)
        {
            var branch = WeatherBranches.FirstOrDefault(x => x.Id == SelectedWeatherBranchId);
            address = branch?.Address ?? string.Empty;
        }
        if (string.IsNullOrWhiteSpace(address))
        {
            WeatherSettingsStatus = Loc["Vm.Enter a manual address or both coordinates"];
            return;
        }

        try
        {
            WeatherSettingsStatus = Loc["Vm.Checking weather location"];
            var client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(10);
            var url = "https://geocoding-api.open-meteo.com/v1/search?name=" +
                Uri.EscapeDataString(address) + "&count=3&language=en&format=json&countryCode=GR";
            using var response = await client.GetAsync(url);
            response.EnsureSuccessStatusCode();
            using var json = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            if (json.RootElement.TryGetProperty("results", out var results) && results.GetArrayLength() > 0)
            {
                var found = results[0];
                var name = found.TryGetProperty("name", out var n) ? n.GetString() : address;
                WeatherSettingsStatus = string.Format(Loc["Vm.Location found: {0}"], name);
                return;
            }

            // Open-Meteo may not recognize street addresses. Nominatim is the fallback.
            using var request = new HttpRequestMessage(HttpMethod.Get,
                "https://nominatim.openstreetmap.org/search?q=" +
                Uri.EscapeDataString(address + ", Greece") + "&format=jsonv2&limit=1&countrycodes=gr");
            request.Headers.UserAgent.ParseAdd("ErganiManager/1.0 (weather location test)");
            using var fallback = await client.SendAsync(request);
            fallback.EnsureSuccessStatusCode();
            using var fallbackJson = System.Text.Json.JsonDocument.Parse(await fallback.Content.ReadAsStringAsync());
            if (fallbackJson.RootElement.GetArrayLength() > 0)
            {
                var display = fallbackJson.RootElement[0].GetProperty("display_name").GetString();
                WeatherSettingsStatus = string.Format(Loc["Vm.Location found: {0}"], display);
            }
            else
            {
                WeatherSettingsStatus = Loc["Vm.Location could not be found. Enter a nearby town or coordinates."];
            }
        }
        catch (Exception ex)
        {
            WeatherSettingsStatus = Loc["Vm.Location test failed: "] + ex.Message;
        }
    }

    private async Task LoadWeatherBranchesAsync()
    {
        SelectedWeatherBranchId = null;
        WeatherBranches.Clear();
        if (_activeCompanyId is not int companyId) return;
        try
        {
            await using var db = new AppDbContext(_connectionState.GetDbOptions());
            var rows = await db.Branches.AsNoTracking().Where(x => x.CompanyId == companyId)
                .OrderBy(x => x.BranchNumber).ToListAsync();
            foreach (var branch in rows) WeatherBranches.Add(branch);
            if (SelectedWeatherBranchId == null && rows.Count > 0) SelectedWeatherBranchId = rows[0].Id;
        }
        catch (Exception ex) { WeatherSettingsStatus = "❌ " + ex.Message; }
    }

    [RelayCommand]
    private async Task AddTerminalAnnouncementAsync()
    {
        if (_activeCompanyId is not int companyId)
        {
            TerminalAnnouncementsStatus = Loc[L.SelectCompanyForAnnouncement];
            return;
        }

        var message = NewTerminalAnnouncement.Trim();
        if (message.Length == 0)
        {
            TerminalAnnouncementsStatus = Loc[L.EnterAnnouncement];
            return;
        }

        try
        {
            await using var db = new AppDbContext(_connectionState.GetDbOptions());
            db.TerminalAnnouncements.Add(new ErganiManager.Data.Entities.TerminalAnnouncement
            {
                CompanyId = companyId,
                Message = message,
                IsActive = true,
                CreatedAtUtc = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
            NewTerminalAnnouncement = string.Empty;
            await LoadTerminalAnnouncementsAsync();
            TerminalAnnouncementsStatus = Loc[L.AnnouncementSaved];
        }
        catch (Exception ex)
        {
            TerminalAnnouncementsStatus = "❌ " + ex.Message;
        }
    }

    [RelayCommand]
    private async Task ToggleTerminalAnnouncementAsync(TerminalAnnouncementEdit? item)
    {
        if (item == null) return;
        try
        {
            await using var db = new AppDbContext(_connectionState.GetDbOptions());
            var row = await db.TerminalAnnouncements.FirstOrDefaultAsync(x => x.Id == item.Id && x.CompanyId == _activeCompanyId);
            if (row == null) return;
            row.IsActive = !row.IsActive;
            await db.SaveChangesAsync();
            item.IsActive = row.IsActive;
            TerminalAnnouncementsStatus = Loc[L.AnnouncementStatusUpdated];
        }
        catch (Exception ex) { TerminalAnnouncementsStatus = "❌ " + ex.Message; }
    }

    [RelayCommand]
    private async Task DeleteTerminalAnnouncementAsync(TerminalAnnouncementEdit? item)
    {
        if (item == null || _activeCompanyId is not int companyId) return;
        try
        {
            await using var db = new AppDbContext(_connectionState.GetDbOptions());
            var row = await db.TerminalAnnouncements.FirstOrDefaultAsync(x => x.Id == item.Id && x.CompanyId == companyId);
            if (row == null) return;
            db.TerminalAnnouncements.Remove(row);
            await db.SaveChangesAsync();
            TerminalAnnouncements.Remove(item);
            TerminalAnnouncementsStatus = Loc[L.AnnouncementDeleted];
        }
        catch (Exception ex) { TerminalAnnouncementsStatus = "❌ " + ex.Message; }
    }

    private async Task LoadTerminalAnnouncementsAsync()
    {
        TerminalAnnouncements.Clear();
        if (_activeCompanyId is not int companyId) return;
        try
        {
            await using var db = new AppDbContext(_connectionState.GetDbOptions());
            var rows = await db.TerminalAnnouncements.AsNoTracking()
                .Where(x => x.CompanyId == companyId)
                .OrderByDescending(x => x.CreatedAtUtc)
                .ToListAsync();
            foreach (var row in rows)
                TerminalAnnouncements.Add(new TerminalAnnouncementEdit(row.Id, row.Message, row.IsActive, row.CreatedAtUtc));
        }
        catch (Exception ex) { TerminalAnnouncementsStatus = "❌ " + ex.Message; }
    }

    [ObservableProperty] private string _officialHolidaysStatus = string.Empty;

    // Schedule portal settings
    [ObservableProperty] private string _schedulePortalBaseUrl = string.Empty;
    [ObservableProperty] private string _schedulePortalTargetUrl = string.Empty;
    [ObservableProperty] private string _schedulePortalFromField = string.Empty;
    [ObservableProperty] private string _schedulePortalToField = string.Empty;
    [ObservableProperty] private string _schedulePortalDateFormat = string.Empty;
    [ObservableProperty] private bool _schedulePortalAutoSearch = true;
    [ObservableProperty] private bool _schedulePortalAutoDownload = true;
    [ObservableProperty] private string _schedulePortalSearchButton = string.Empty;
    [ObservableProperty] private string _schedulePortalDownloadFolder = string.Empty;
    [ObservableProperty] private string _schedulePortalStatus = string.Empty;

    // Work Cards portal settings
    [ObservableProperty] private string _workCardsPortalBaseUrl = string.Empty;
    [ObservableProperty] private string _workCardsPortalTargetUrl = string.Empty;
    [ObservableProperty] private string _workCardsPortalFromField = string.Empty;
    [ObservableProperty] private string _workCardsPortalToField = string.Empty;
    [ObservableProperty] private string _workCardsPortalDateFormat = string.Empty;
    [ObservableProperty] private bool _workCardsPortalAutoSearch = true;
    [ObservableProperty] private string _workCardsPortalSearchButton = string.Empty;
    [ObservableProperty] private string _workCardsPortalStatus = string.Empty;

    /// <summary>Editable rounding bands used by the "Rounded" column of the Declared vs actual report.</summary>
    public ObservableCollection<LeaveTypeEdit> EditableLeaveTypes { get; } = new();

    public IReadOnlyList<LeavePaymentOption> LeavePaymentOptions => new[]
    {
        new LeavePaymentOption("EmployerPaid", Loc["LeavePaymentEmployerPaid"]),
        new LeavePaymentOption("Subsidized", Loc["LeavePaymentSubsidized"]),
        new LeavePaymentOption("Unpaid", Loc["LeavePaymentUnpaid"])
    };
    [ObservableProperty] private string _leaveTypesStatus = string.Empty;

    public ObservableCollection<RoundingBandEdit> RoundingBands { get; } = new();
    public ObservableCollection<ShiftEdit> Shifts { get; } = new();
    [ObservableProperty] private string _roundingStatus = string.Empty;

    [ObservableProperty] private TimeSpan? _nightStartTime = new(22, 0, 0);
    [ObservableProperty] private TimeSpan? _nightEndTime = new(6, 0, 0);
    [ObservableProperty] private string _nightStatus = string.Empty;

    /// <summary>Minutes after a scan during which Ergani treats the work card as on time.</summary>
    [ObservableProperty] private decimal? _onTimeWindowMinutes = RoundingRules.DefaultOnTimeWindowMinutes;
    [ObservableProperty] private string _onTimeWindowStatus = string.Empty;

    [ObservableProperty] private bool _autoClockOutEnabled;
    [ObservableProperty] private decimal? _autoClockOutMinutes = 15;
    [ObservableProperty] private string _autoClockOutStatus = string.Empty;

    // Company-specific operational settings (loaded from the active company selected in the main shell).
    [ObservableProperty] private string _companyErganiBaseUrl = ErganiEndpoints.TrialBaseUrl;
    [ObservableProperty] private int _earlyClockInBlockMinutes = 15;
    [ObservableProperty] private int _earlyDepartureAlertMinutes = 10;
    [ObservableProperty] private bool _blockClockInWithoutSchedule = true;
    [ObservableProperty] private bool _blockClockInOnRestOrAbsentDay = true;
    [ObservableProperty] private bool _alertEmailEnabled;
    [ObservableProperty] private bool _autoRetryFailedSubmissions = true;
    [ObservableProperty] private string _alertEmailRecipients = string.Empty;
    [ObservableProperty] private string _smtpHost = string.Empty;
    [ObservableProperty] private int? _smtpPort = 465;
    [ObservableProperty] private string _smtpUser = string.Empty;
    [ObservableProperty] private string _smtpPassword = string.Empty;
    [ObservableProperty] private bool _smtpUseTls = true;
    [ObservableProperty] private bool _alertEvEarlyDeparture = true;
    [ObservableProperty] private bool _alertEvErganiDown;
    [ObservableProperty] private bool _alertEvInternetDown;
    [ObservableProperty] private bool _alertEvEarlyClockIn;
    [ObservableProperty] private bool _alertEvLateClockIn;
    [ObservableProperty] private bool _alertEvNoSchedule;
    [ObservableProperty] private bool _alertEvScanRejected;
    [ObservableProperty] private bool _alertEvAutomaticClockOut;
    [ObservableProperty] private int _clockInAlertMinutes = 10;
    [ObservableProperty] private bool _isTestingCompanyEmail;
    [ObservableProperty] private bool _companyEmailTestSuccess;
    [ObservableProperty] private string _companyEmailTestResult = string.Empty;
    [ObservableProperty] private string _companySettingsStatus = string.Empty;
    [ObservableProperty] private bool _isCompanySettingsDirty;


    // System health dashboard (read-only checks; no submissions or emails are sent).
    [ObservableProperty] private bool _isRefreshingSystemHealth;
    [ObservableProperty] private string _systemHealthSummary = string.Empty;
    [ObservableProperty] private string _databaseHealthStatus = string.Empty;
    [ObservableProperty] private string _databaseHealthDetail = string.Empty;
    [ObservableProperty] private string _internetHealthStatus = string.Empty;
    [ObservableProperty] private string _erganiHealthStatus = string.Empty;
    [ObservableProperty] private string _emailHealthStatus = string.Empty;
    [ObservableProperty] private string _cacheHealthStatus = string.Empty;
    [ObservableProperty] private string _queuedScansHealthStatus = "—";
    [ObservableProperty] private string _failedSubmissionsHealthStatus = "—";
    [ObservableProperty] private string _lastSuccessfulSyncHealthStatus = "—";
    [ObservableProperty] private string _healthLastChecked = string.Empty;




    public AdministrationViewModel(IDatabaseMaintenanceService maintenance, IRoundingSettingsService rounding,
        IPortalSettingsService portal, ICompanyService companyService, ICacheSyncService cacheSync,
        IOfficialHolidayCatalogService holidayCatalog, IEmailAlertService emailAlertService,
        IInternetCheckService internetCheckService, IHttpClientFactory httpClientFactory,
        IErganiHealthCheckService erganiHealthCheckService, IConnectionStateService connectionState,
        ICredentialProtector credentialProtector, ErganiManager.UI.Services.AutomaticDatabaseBackupService automaticBackup)
    {
        _maintenance = maintenance;
        _rounding = rounding;
        _portal = portal;
        _companyService = companyService;
        _cacheSync = cacheSync;
        _holidayCatalog = holidayCatalog;
        _emailAlertService = emailAlertService;
        _internetCheckService = internetCheckService;
        _httpClientFactory = httpClientFactory;
        _erganiHealthCheckService = erganiHealthCheckService;
        _connectionState = connectionState;
        _credentialProtector = credentialProtector;
        _automaticBackup = automaticBackup;
        AutomaticBackupEnabled = automaticBackup.IsEnabled;
        AutomaticBackupIntervalHours = automaticBackup.IntervalHours;
        AutomaticBackupRetentionCount = automaticBackup.RetentionCount;
        AutomaticBackupFolder = automaticBackup.BackupFolder;
        RefreshAutomaticBackupStatus();
        automaticBackup.StatusChanged += (_, _) => Dispatcher.UIThread.Post(RefreshAutomaticBackupStatus);
        var schedule = portal.Current.Schedule;
        var workCards = portal.Current.WorkCards;

        SchedulePortalBaseUrl = schedule.PortalBaseUrl;
        SchedulePortalTargetUrl = schedule.TargetPageUrl;
        SchedulePortalFromField = schedule.FromDateFieldId;
        SchedulePortalToField = schedule.ToDateFieldId;
        SchedulePortalDateFormat = schedule.DateFormat;
        SchedulePortalAutoSearch = schedule.AutoSearch;
        SchedulePortalAutoDownload = schedule.AutoDownload;
        SchedulePortalSearchButton = schedule.SearchButtonId;
        SchedulePortalDownloadFolder = schedule.DownloadFolder;

        WorkCardsPortalBaseUrl = workCards.PortalBaseUrl;
        WorkCardsPortalTargetUrl = workCards.TargetPageUrl;
        WorkCardsPortalFromField = workCards.FromDateFieldId;
        WorkCardsPortalToField = workCards.ToDateFieldId;
        WorkCardsPortalDateFormat = workCards.DateFormat;
        WorkCardsPortalAutoSearch = workCards.AutoSearch;
        WorkCardsPortalSearchButton = workCards.SearchButtonId;
        var rules = _rounding.Current;
        LoadRounding(rules.Bands);
        LoadShifts(rules.Shifts);
        NightStartTime = TimeSpan.FromMinutes(Math.Clamp(rules.NightStartMinuteOfDay, 0, 1439));
        NightEndTime = TimeSpan.FromMinutes(Math.Clamp(rules.NightEndMinuteOfDay, 0, 1439));
        OnTimeWindowMinutes = rules.OnTimeWindowMinutes;
        AutoClockOutEnabled = portal.Current.AutoClockOutEnabled;
        AutoClockOutMinutes = Math.Clamp(portal.Current.AutoClockOutMinutes, 0, 1440);
        LeaveTypes.Refresh();
        LoadLeaveTypes();
    }

    private void LoadLeaveTypes()
    {
        EditableLeaveTypes.Clear();
        foreach (var type in LeaveTypes.All)
        {
            var edit = new LeaveTypeEdit(type.Code, type.Description, type.IsHourly, type.IsPaid, type.EffectivePaymentCategory);
            edit.SelectedPaymentOption = LeavePaymentOptions.FirstOrDefault(option => option.Key == type.EffectivePaymentCategory);
            EditableLeaveTypes.Add(edit);
        }
    }

    [RelayCommand]
    private void AddLeaveType()
    {
        var edit = new LeaveTypeEdit(string.Empty, string.Empty, false, true, "EmployerPaid");
        edit.SelectedPaymentOption = LeavePaymentOptions.FirstOrDefault(option => option.Key == "EmployerPaid");
        EditableLeaveTypes.Add(edit);
    }

    [RelayCommand]
    private void RemoveLeaveType(LeaveTypeEdit? type)
    {
        if (type != null) EditableLeaveTypes.Remove(type);
    }

    [RelayCommand]
    private void SaveLeaveTypes()
    {
        try
        {
            LeaveTypes.Save(EditableLeaveTypes.Select(t => new LeaveTypeInfo(t.Code, t.Description, t.IsHourly, t.IsPaid, t.SelectedPaymentOption?.Key ?? t.PaymentCategory)));
            LeaveTypesStatus = Loc["Vm.✅ Leave types saved. Changes apply to new and existing holiday display."];
            LoadLeaveTypes();
        }
        catch (Exception ex) { LeaveTypesStatus = $"❌ {ex.Message}"; }
    }

    private void LoadRounding(System.Collections.Generic.IEnumerable<RoundingBand> bands)
    {
        RoundingBands.Clear();
        foreach (var b in bands.OrderBy(b => b.FromMinute))
            RoundingBands.Add(new RoundingBandEdit(b.FromMinute, b.ToMinute, b.RoundToMinute));
    }

    private void LoadShifts(System.Collections.Generic.IEnumerable<WorkShiftDefinition>? shifts)
    {
        Shifts.Clear();
        var source = (shifts ?? RoundingRules.DefaultShifts()).Take(3).ToList();
        var defaults = RoundingRules.DefaultShifts();
        for (var i = 0; i < 3; i++)
        {
            var shift = i < source.Count ? source[i] : defaults[i];
            Shifts.Add(new ShiftEdit(shift.Name, shift.StartTime, shift.EndTime));
        }
    }

    private List<WorkShiftDefinition> BuildShifts()
    {
        if (Shifts.Count != 3)
            throw new InvalidOperationException(Loc[L.ThreeShiftsRequired]);

        var result = new List<WorkShiftDefinition>();
        foreach (var shift in Shifts)
        {
            var name = (shift.Name ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(name) || shift.StartTime is not { } start || shift.EndTime is not { } end)
                throw new InvalidOperationException(Loc[L.ShiftValuesRequired]);
            if (start < TimeSpan.Zero || start >= TimeSpan.FromDays(1) || end < TimeSpan.Zero || end >= TimeSpan.FromDays(1))
                throw new InvalidOperationException(Loc[L.ShiftTimesInvalid]);
            if (start == end)
                throw new InvalidOperationException(Loc[L.ShiftTimesEqual]);
            result.Add(new WorkShiftDefinition { Name = name, StartTime = start, EndTime = end });
        }
        return result;
    }

    [RelayCommand]
    private void SaveSchedulePortal()
    {
        try
        {
            var schedule = BuildScheduleProfile();
            if (!ValidatePortalUrl(schedule.PortalBaseUrl, Loc[L.PortalBaseAddress], out var error))
            {
                SchedulePortalStatus = error;
                return;
            }

            var current = _portal.Current;
            current.Schedule = schedule;
            _portal.Save(current);
            SchedulePortalStatus = Loc[L.SchedulePortalSaved];
        }
        catch (Exception ex)
        {
            SchedulePortalStatus = string.Format(Loc[L.CouldNotSaveSchedulePortal], ex.Message);
        }
    }

    [RelayCommand]
    private void SaveWorkCardsPortal()
    {
        try
        {
            var workCards = BuildWorkCardsProfile();
            if (!ValidatePortalUrl(workCards.PortalBaseUrl, Loc[L.WorkCardsPortal], out var error))
            {
                WorkCardsPortalStatus = error;
                return;
            }

            var current = _portal.Current;
            current.WorkCards = workCards;
            _portal.Save(current);
            WorkCardsPortalStatus = Loc[L.WorkCardsPortalSaved];
        }
        catch (Exception ex)
        {
            WorkCardsPortalStatus = string.Format(Loc[L.CouldNotSaveWorkCardsPortal], ex.Message);
        }
    }

    private ErganiPortalProfile BuildScheduleProfile() => new()
    {
        PortalBaseUrl = (SchedulePortalBaseUrl ?? string.Empty).Trim(),
        TargetPageUrl = (SchedulePortalTargetUrl ?? string.Empty).Trim(),
        FromDateFieldId = (SchedulePortalFromField ?? string.Empty).Trim(),
        ToDateFieldId = (SchedulePortalToField ?? string.Empty).Trim(),
        DateFormat = string.IsNullOrWhiteSpace(SchedulePortalDateFormat) ? "dd/MM/yyyy" : SchedulePortalDateFormat.Trim(),
        AutoSearch = SchedulePortalAutoSearch,
        AutoDownload = SchedulePortalAutoDownload,
        SearchButtonId = (SchedulePortalSearchButton ?? string.Empty).Trim(),
        DownloadFolder = (SchedulePortalDownloadFolder ?? string.Empty).Trim()
    };

    private ErganiPortalProfile BuildWorkCardsProfile() => new()
    {
        PortalBaseUrl = (WorkCardsPortalBaseUrl ?? string.Empty).Trim(),
        TargetPageUrl = (WorkCardsPortalTargetUrl ?? string.Empty).Trim(),
        FromDateFieldId = (WorkCardsPortalFromField ?? string.Empty).Trim(),
        ToDateFieldId = (WorkCardsPortalToField ?? string.Empty).Trim(),
        DateFormat = string.IsNullOrWhiteSpace(WorkCardsPortalDateFormat) ? "dd/MM/yyyy" : WorkCardsPortalDateFormat.Trim(),
        AutoSearch = WorkCardsPortalAutoSearch,
        AutoDownload = false,
        SearchButtonId = (WorkCardsPortalSearchButton ?? string.Empty).Trim()
    };

    private bool ValidatePortalUrl(string value, string name, out string message)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            (uri.Scheme != "https" && uri.Scheme != "http"))
        {
            message = string.Format(Loc[L.PortalInvalidUrl], name);
            return false;
        }

        message = string.Empty;
        return true;
    }

    [RelayCommand]
    private void AddRoundingBand() => RoundingBands.Add(new RoundingBandEdit(0, 0, 0));

    [RelayCommand]
    private void RemoveRoundingBand(RoundingBandEdit? band)
    {
        if (band != null) RoundingBands.Remove(band);
    }

    [RelayCommand]
    private void RestoreRoundingDefaults()
    {
        LoadRounding(RoundingRules.Defaults());
        LoadShifts(RoundingRules.DefaultShifts());
        NightStartTime = new TimeSpan(22, 0, 0);
        NightEndTime = new TimeSpan(6, 0, 0);
        RoundingStatus = Loc["Vm.Defaults loaded — press Save to apply them."];
    }

    [RelayCommand]
    private void SaveNightHours()
    {
        try
        {
            if (NightStartTime is not { } nightStart || NightEndTime is not { } nightEnd ||
                nightStart < TimeSpan.Zero || nightStart >= TimeSpan.FromDays(1) ||
                nightEnd < TimeSpan.Zero || nightEnd >= TimeSpan.FromDays(1) || nightStart == nightEnd)
            {
                NightStatus = Loc["Vm.❌ Night start/end must be valid times between 00:00 and 23:59."];
                return;
            }

            var current = _rounding.Current;
            _rounding.Save(new RoundingRules
            {
                Bands = current.Bands,
                Shifts = BuildShifts(),
                NightStartMinuteOfDay = nightStart.Hours * 60 + nightStart.Minutes,
                NightEndMinuteOfDay = nightEnd.Hours * 60 + nightEnd.Minutes,
                OnTimeWindowMinutes = _rounding.Current.OnTimeWindowMinutes
            });
            NightStatus = Loc[L.NightSettingsSaved] + " " + Loc[L.NightSettingsApply];
        }
        catch (Exception ex) { NightStatus = $"❌ {ex.Message}"; }
    }

    [RelayCommand]
    private void SaveAutoClockOut()
    {
        if (AutoClockOutMinutes is not { } value || value < 0 || value > 1440 || value != decimal.Truncate(value))
        {
            AutoClockOutStatus = string.Format(Loc[L.AutoClockOutInvalid], 1440);
            return;
        }

        try
        {
            var settings = _portal.Current;
            settings.AutoClockOutEnabled = AutoClockOutEnabled;
            settings.AutoClockOutMinutes = (int)value;
            _portal.Save(settings);
            AutoClockOutStatus = Loc[L.AutoClockOutSaved];
        }
        catch (Exception ex)
        {
            AutoClockOutStatus = string.Format(Loc[L.AutoClockOutSaveFailed], ex.Message);
        }
    }

    [RelayCommand]
    private void SaveOnTimeWindow()
    {
        try
        {
            if (OnTimeWindowMinutes is not { } value || value < 0 || value > RoundingRules.MaxOnTimeWindowMinutes ||
                value != decimal.Truncate(value))
            {
                OnTimeWindowStatus = string.Format(Loc[L.OnTimeWindowInvalid], RoundingRules.MaxOnTimeWindowMinutes);
                return;
            }

            var current = _rounding.Current;
            _rounding.Save(new RoundingRules
            {
                Bands = current.Bands,
                Shifts = current.Shifts,
                NightStartMinuteOfDay = current.NightStartMinuteOfDay,
                NightEndMinuteOfDay = current.NightEndMinuteOfDay,
                OnTimeWindowMinutes = (int)value
            });
            OnTimeWindowStatus = Loc[L.OnTimeWindowSaved];
        }
        catch (Exception ex) { OnTimeWindowStatus = $"❌ {ex.Message}"; }
    }

    [RelayCommand]
    private void SaveRounding()
    {
        var bands = RoundingBands
            .Select(b => new RoundingBand { FromMinute = (int)(b.From ?? 0), ToMinute = (int)(b.To ?? 0), RoundToMinute = (int)(b.RoundTo ?? 0) })
            .OrderBy(b => b.FromMinute).ToList();

        foreach (var b in bands)
        {
            if (b.FromMinute < 0 || b.ToMinute > 59 || b.FromMinute > b.ToMinute)
            { RoundingStatus = string.Format(Loc["Vm.❌ Invalid range {0}–{1}: minutes must be 0–59 and From ≤ To."], b.FromMinute, b.ToMinute); return; }
            if (b.RoundToMinute is < 0 or > 60)
            { RoundingStatus = Loc["Vm.❌ 'Round to' must be between 0 and 60 minutes."]; return; }
        }
        for (var i = 1; i < bands.Count; i++)
            if (bands[i].FromMinute <= bands[i - 1].ToMinute)
            { RoundingStatus = string.Format(Loc["Vm.❌ Ranges overlap: {0}–{1} and {2}–{3}."], bands[i - 1].FromMinute, bands[i - 1].ToMinute, bands[i].FromMinute, bands[i].ToMinute); return; }

        try
        {
            if (NightStartTime is not { } nightStart || NightEndTime is not { } nightEnd ||
                nightStart < TimeSpan.Zero || nightStart >= TimeSpan.FromDays(1) ||
                nightEnd < TimeSpan.Zero || nightEnd >= TimeSpan.FromDays(1) || nightStart == nightEnd)
            {
                RoundingStatus = Loc["Vm.❌ Night start/end must be valid times between 00:00 and 23:59."];
                return;
            }

            _rounding.Save(new RoundingRules
            {
                Bands = bands,
                Shifts = BuildShifts(),
                NightStartMinuteOfDay = nightStart.Hours * 60 + nightStart.Minutes,
                NightEndMinuteOfDay = nightEnd.Hours * 60 + nightEnd.Minutes,
                OnTimeWindowMinutes = _rounding.Current.OnTimeWindowMinutes
            });
            RoundingStatus = Loc[L.NightSettingsSaved] + " " + Loc[L.NightSettingsApply];
        }
        catch (Exception ex) { RoundingStatus = $"❌ {ex.Message}"; }
    }



    [RelayCommand]
    private async Task RefreshSystemHealthAsync()
    {
        if (IsRefreshingSystemHealth) return;
        IsRefreshingSystemHealth = true;
        SystemHealthSummary = Loc["HealthChecking"];
        var apiServerError = false;
        var apiConfigurationProblem = false;
        var emailConfigurationProblem = false;
        try
        {
            var internetTask = _internetCheckService.IsOnlineAsync();
            List<CompanyDto>? companies = null;
            Exception? databaseError = null;
            try
            {
                // A read-only data query verifies the configured main database without running migrations.
                companies = await _companyService.GetAllAsync();
                DatabaseHealthStatus = Loc["HealthOnline"];
                try { DatabaseHealthDetail = _maintenance.DescribeDatabase(); }
                catch { DatabaseHealthDetail = Loc["HealthDatabaseConfigured"]; }
            }
            catch (Exception ex)
            {
                databaseError = ex;
                DatabaseHealthStatus = Loc["HealthOffline"];
                DatabaseHealthDetail = ex.Message;
            }

            bool internetOnline;
            try
            {
                internetOnline = await internetTask;
                InternetHealthStatus = internetOnline ? Loc["HealthOnline"] : Loc["HealthOffline"];
            }
            catch (Exception ex)
            {
                internetOnline = false;
                InternetHealthStatus = Loc["HealthCheckFailed"] + " " + ex.Message;
            }

            var company = companies?.FirstOrDefault(c => c.Id == _activeCompanyId);
            if (company == null && companies?.Count > 0 && _activeCompanyId == null)
                company = companies[0];

            if (!internetOnline)
            {
                ErganiHealthStatus = Loc["HealthNotCheckedNoInternet"];
            }
            else if (company == null || string.IsNullOrWhiteSpace(company.ErganiBaseUrl) ||
                     !Uri.TryCreate(company.ErganiBaseUrl, UriKind.Absolute, out var apiUri) ||
                     (apiUri.Scheme != Uri.UriSchemeHttp && apiUri.Scheme != Uri.UriSchemeHttps))
            {
                ErganiHealthStatus = Loc["HealthNoCompanyApiUrl"];
                apiConfigurationProblem = true;
            }
            else
            {
                try
                {
                    // Use the same real JWT-authentication health check as the background retry/outage routine.
                    // Do not GET the API base URL: that route may legitimately return HTTP 404.
                    await using var db = new AppDbContext(_connectionState.GetDbOptions());
                    var storedCompany = await db.Companies.AsNoTracking()
                        .FirstOrDefaultAsync(c => c.Id == company.Id);

                    if (storedCompany == null)
                    {
                        ErganiHealthStatus = Loc["HealthNoCompanyApiUrl"];
                        apiConfigurationProblem = true;
                    }
                    else if (string.IsNullOrWhiteSpace(storedCompany.ErganiUsername) ||
                             string.IsNullOrWhiteSpace(storedCompany.ErganiPasswordEncrypted))
                    {
                        ErganiHealthStatus = Loc["HealthEndpointUnreachable"] + " " +
                            Loc["HealthMissingErganiCredentials"];
                        apiConfigurationProblem = true;
                    }
                    else
                    {
                        var credentials = new ErganiManager.ErganiApi.Models.ErganiCredentials
                        {
                            Username = storedCompany.ErganiUsername,
                            Password = _credentialProtector.Unprotect(storedCompany.ErganiPasswordEncrypted),
                            Usertype = storedCompany.ErganiUsertype,
                            BaseUrl = storedCompany.ErganiBaseUrl
                        };

                        var status = await _erganiHealthCheckService.CheckAsync(credentials);
                        apiServerError = status == ErganiServiceStatus.Offline;
                        ErganiHealthStatus = status == ErganiServiceStatus.Online
                            ? Loc["HealthOnline"] + " — " + Loc["HealthErganiAuthenticationSucceeded"]
                            : Loc["HealthOffline"] + " — " + Loc["HealthErganiAuthenticationFailed"];
                    }
                }
                catch (Exception ex)
                {
                    apiServerError = true;
                    ErganiHealthStatus = Loc["HealthEndpointUnreachable"] + " " + ex.Message;
                }
            }

            if (company != null)
            {
                try
                {
                    var lastSync = await _cacheSync.GetLastSuccessfulRefreshAsync(company.Id);
                    LastSuccessfulSyncHealthStatus = lastSync.HasValue
                        ? lastSync.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss")
                        : Loc["HealthNeverSynced"];
                }
                catch
                {
                    LastSuccessfulSyncHealthStatus = Loc["HealthSyncTimeUnavailable"];
                }
            }
            else
            {
                LastSuccessfulSyncHealthStatus = Loc["HealthNoCompanySelected"];
            }

            if (company == null)
                EmailHealthStatus = Loc["HealthNoCompanySelected"];
            else if (!company.AlertEmailEnabled)
                EmailHealthStatus = Loc["HealthEmailDisabled"];
            else if (string.IsNullOrWhiteSpace(company.SmtpHost) ||
                     string.IsNullOrWhiteSpace(company.AlertEmailRecipients))
            {
                EmailHealthStatus = Loc["HealthEmailNeedsConfiguration"];
                emailConfigurationProblem = true;
            }
            else
                EmailHealthStatus = Loc["HealthEmailConfiguredNotTested"];

            try
            {
                var cache = _maintenance.GetLocalCacheStatus();
                QueuedScansHealthStatus = cache.PendingScans.ToString();
                FailedSubmissionsHealthStatus = cache.FailedSubmissions.ToString();
                CacheHealthStatus = Loc["HealthCacheAvailable"];
            }
            catch (Exception ex)
            {
                CacheHealthStatus = Loc["HealthCacheUnavailable"] + " " + ex.Message;
                QueuedScansHealthStatus = "—";
                FailedSubmissionsHealthStatus = "—";
            }

            var hasFailedSubmissions = int.TryParse(FailedSubmissionsHealthStatus, out var failed) && failed > 0;
            var endpointUnreachable = ErganiHealthStatus.StartsWith(Loc["HealthEndpointUnreachable"], StringComparison.Ordinal);
            var hasProblem = databaseError != null || !internetOnline || apiServerError || apiConfigurationProblem || endpointUnreachable || hasFailedSubmissions || emailConfigurationProblem;
            SystemHealthSummary = hasProblem ? Loc["HealthAttentionRequired"] : Loc["HealthAllChecksComplete"];
            HealthLastChecked = DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss");
        }
        catch (Exception ex)
        {
            SystemHealthSummary = Loc["HealthDashboardError"] + " " + ex.Message;
            HealthLastChecked = DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss");
        }
        finally
        {
            IsRefreshingSystemHealth = false;
        }
    }

    private int ComposeCompanyAlertEvents() =>
        (AlertEvEarlyDeparture ? (int)AlertEvent.EarlyDeparture : 0) |
        (AlertEvErganiDown ? (int)AlertEvent.ErganiDown : 0) |
        (AlertEvInternetDown ? (int)AlertEvent.InternetDown : 0) |
        (AlertEvEarlyClockIn ? (int)AlertEvent.EarlyClockIn : 0) |
        (AlertEvLateClockIn ? (int)AlertEvent.LateClockIn : 0) |
        (AlertEvNoSchedule ? (int)AlertEvent.NoScheduleClockIn : 0) |
        (AlertEvScanRejected ? (int)AlertEvent.ScanRejected : 0) |
        (AlertEvAutomaticClockOut ? (int)AlertEvent.AutomaticClockOut : 0);

    private void ApplyCompanyAlertEvents(int mask)
    {
        var events = (AlertEvent)mask;
        AlertEvEarlyDeparture = events.HasFlag(AlertEvent.EarlyDeparture);
        AlertEvErganiDown = events.HasFlag(AlertEvent.ErganiDown);
        AlertEvInternetDown = events.HasFlag(AlertEvent.InternetDown);
        AlertEvEarlyClockIn = events.HasFlag(AlertEvent.EarlyClockIn);
        AlertEvLateClockIn = events.HasFlag(AlertEvent.LateClockIn);
        AlertEvNoSchedule = events.HasFlag(AlertEvent.NoScheduleClockIn);
        AlertEvScanRejected = events.HasFlag(AlertEvent.ScanRejected);
        AlertEvAutomaticClockOut = events.HasFlag(AlertEvent.AutomaticClockOut);
    }

    private async Task LoadCompanySettingsAsync()
    {
        if (_activeCompanyId is not { } companyId)
        {
            _loadedCompanySettings = null;
            CompanySettingsStatus = Loc["Select a company in the main window to edit its settings."];
            return;
        }

        try
        {
            var company = await _companyService.GetByIdAsync(companyId);
            if (company == null)
            {
                CompanySettingsStatus = Loc["Could not load settings for the selected company."];
                return;
            }

            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
            {
                _loadingCompanySettings = true;
                try
                {
                    _loadedCompanySettings = company;
                    CompanyErganiBaseUrl = string.IsNullOrWhiteSpace(company.ErganiBaseUrl)
                        ? ErganiEndpoints.TrialBaseUrl : company.ErganiBaseUrl;
                    EarlyClockInBlockMinutes = company.EarlyClockInBlockMinutes;
                    EarlyDepartureAlertMinutes = company.EarlyDepartureAlertMinutes;
                    BlockClockInWithoutSchedule = company.BlockClockInWithoutSchedule;
                    BlockClockInOnRestOrAbsentDay = company.BlockClockInOnRestOrAbsentDay;
                    AlertEmailEnabled = company.AlertEmailEnabled;
                    AutoRetryFailedSubmissions = company.AutoRetryFailedSubmissions;
                    AlertEmailRecipients = company.AlertEmailRecipients ?? string.Empty;
                    SmtpHost = company.SmtpHost ?? string.Empty;
                    SmtpPort = company.SmtpPort ?? 465;
                    SmtpUser = company.SmtpUser ?? string.Empty;
                    // Passwords are intentionally not returned by CompanyService. Blank means keep the stored password.
                    SmtpPassword = string.Empty;
                    SmtpUseTls = company.SmtpUseTls;
                    ApplyCompanyAlertEvents(company.AlertEvents);
                    ClockInAlertMinutes = company.ClockInAlertMinutes;
                    CompanyEmailTestResult = string.Empty;
                    CompanyEmailTestSuccess = false;
                    CompanySettingsStatus = string.Format(Loc["Settings loaded for {0}."], company.Name);
                    IsCompanySettingsDirty = false;
                }
                finally { _loadingCompanySettings = false; }
            });
        }
        catch (Exception ex)
        {
            CompanySettingsStatus = string.Format(Loc["Could not load company settings: {0}"], ex.Message);
        }
    }

    private void MarkCompanySettingsDirty()
    {
        if (!_loadingCompanySettings && _loadedCompanySettings != null)
            IsCompanySettingsDirty = true;
    }

    partial void OnCompanyErganiBaseUrlChanged(string value) => MarkCompanySettingsDirty();
    partial void OnEarlyClockInBlockMinutesChanged(int value) => MarkCompanySettingsDirty();
    partial void OnEarlyDepartureAlertMinutesChanged(int value) => MarkCompanySettingsDirty();
    partial void OnBlockClockInWithoutScheduleChanged(bool value) => MarkCompanySettingsDirty();
    partial void OnBlockClockInOnRestOrAbsentDayChanged(bool value) => MarkCompanySettingsDirty();
    partial void OnAlertEmailEnabledChanged(bool value) => MarkCompanySettingsDirty();
    partial void OnAutoRetryFailedSubmissionsChanged(bool value) => MarkCompanySettingsDirty();
    partial void OnAlertEmailRecipientsChanged(string value) => MarkCompanySettingsDirty();
    partial void OnSmtpHostChanged(string value) => MarkCompanySettingsDirty();
    partial void OnSmtpPortChanged(int? value) => MarkCompanySettingsDirty();
    partial void OnSmtpUserChanged(string value) => MarkCompanySettingsDirty();
    partial void OnSmtpPasswordChanged(string value) => MarkCompanySettingsDirty();
    partial void OnSmtpUseTlsChanged(bool value) => MarkCompanySettingsDirty();
    partial void OnAlertEvEarlyDepartureChanged(bool value) => MarkCompanySettingsDirty();
    partial void OnAlertEvErganiDownChanged(bool value) => MarkCompanySettingsDirty();
    partial void OnAlertEvInternetDownChanged(bool value) => MarkCompanySettingsDirty();
    partial void OnAlertEvEarlyClockInChanged(bool value) => MarkCompanySettingsDirty();
    partial void OnAlertEvLateClockInChanged(bool value) => MarkCompanySettingsDirty();
    partial void OnAlertEvNoScheduleChanged(bool value) => MarkCompanySettingsDirty();
    partial void OnAlertEvScanRejectedChanged(bool value) => MarkCompanySettingsDirty();
    partial void OnAlertEvAutomaticClockOutChanged(bool value) => MarkCompanySettingsDirty();
    partial void OnClockInAlertMinutesChanged(int value) => MarkCompanySettingsDirty();

    [RelayCommand]
    private async Task DiscardCompanySettingsAsync()
    {
        await LoadCompanySettingsAsync();
        CompanySettingsStatus = Loc["Company settings changes discarded."];
    }

    [RelayCommand]
    private async Task SaveCompanySettingsAsync()
    {
        if (_activeCompanyId is not { } companyId)
        {
            CompanySettingsStatus = Loc["Select a company in the main window to edit its settings."];
            return;
        }

        if (_loadedCompanySettings == null || _loadedCompanySettings.Id != companyId)
        {
            CompanySettingsStatus = Loc["Company settings have not finished loading. Please try again."];
            return;
        }

        if (!Uri.TryCreate(CompanyErganiBaseUrl, UriKind.Absolute, out var baseUri) ||
            (baseUri.Scheme != Uri.UriSchemeHttp && baseUri.Scheme != Uri.UriSchemeHttps))
        {
            CompanySettingsStatus = Loc["Enter a valid Ergani API URL beginning with http:// or https://."];
            return;
        }

        if (EarlyClockInBlockMinutes is < 0 or > 120 ||
            EarlyDepartureAlertMinutes is < 0 or > 120 ||
            ClockInAlertMinutes is < 0 or > 240 ||
            SmtpPort is < 1 or > 65535)
        {
            CompanySettingsStatus = Loc["One or more settings are outside the allowed range."];
            return;
        }

        try
        {
            var company = await _companyService.GetByIdAsync(companyId)
                ?? throw new InvalidOperationException(Loc["Could not load settings for the selected company."]);
            company.ErganiBaseUrl = CompanyErganiBaseUrl.Trim();
            company.EarlyClockInBlockMinutes = EarlyClockInBlockMinutes;
            company.EarlyDepartureAlertMinutes = EarlyDepartureAlertMinutes;
            company.BlockClockInWithoutSchedule = BlockClockInWithoutSchedule;
            company.BlockClockInOnRestOrAbsentDay = BlockClockInOnRestOrAbsentDay;
            company.AlertEmailEnabled = AlertEmailEnabled;
            company.AutoRetryFailedSubmissions = AutoRetryFailedSubmissions;
            company.AlertEmailRecipients = string.IsNullOrWhiteSpace(AlertEmailRecipients) ? null : AlertEmailRecipients.Trim();
            company.SmtpHost = string.IsNullOrWhiteSpace(SmtpHost) ? null : SmtpHost.Trim();
            company.SmtpPort = SmtpPort;
            company.SmtpUser = string.IsNullOrWhiteSpace(SmtpUser) ? null : SmtpUser.Trim();
            company.SmtpPasswordPlainText = string.IsNullOrWhiteSpace(SmtpPassword) ? null : SmtpPassword;
            company.SmtpUseTls = SmtpUseTls;
            company.AlertEvents = ComposeCompanyAlertEvents();
            company.ClockInAlertMinutes = ClockInAlertMinutes;

            await _companyService.UpdateAsync(company);
            var sync = await _cacheSync.RefreshCacheFromMainDatabaseAsync(companyId);
            _loadedCompanySettings = await _companyService.GetByIdAsync(companyId);
            SmtpPassword = string.Empty;
            IsCompanySettingsDirty = false;
            CompanySettingsStatus = sync.Success
                ? Loc["Company settings saved."]
                : Loc["Company settings saved, but the terminal cache could not be refreshed: "] + sync.ErrorMessage;
        }
        catch (Exception ex)
        {
            CompanySettingsStatus = string.Format(Loc["Could not save company settings: {0}"], ex.Message);
        }
    }

    [RelayCommand]
    private async Task TestCompanyEmailAsync()
    {
        CompanyEmailTestResult = string.Empty;
        CompanyEmailTestSuccess = false;
        if (string.IsNullOrWhiteSpace(SmtpHost))
        {
            CompanyEmailTestResult = Loc["Enter the SMTP host."];
            return;
        }
        if (string.IsNullOrWhiteSpace(AlertEmailRecipients))
        {
            CompanyEmailTestResult = Loc["Enter at least one recipient email address."];
            return;
        }
        if (!string.IsNullOrWhiteSpace(SmtpUser) && string.IsNullOrEmpty(SmtpPassword))
        {
            CompanyEmailTestResult = Loc["Enter the SMTP password to test authenticated SMTP settings."];
            return;
        }

        IsTestingCompanyEmail = true;
        CompanyEmailTestResult = Loc["Sending test email..."];
        try
        {
            var result = await _emailAlertService.TestSmtpAsync(new EmailSmtpTestRequest(
                SmtpHost.Trim(), SmtpPort ?? 465,
                string.IsNullOrWhiteSpace(SmtpUser) ? null : SmtpUser.Trim(),
                string.IsNullOrEmpty(SmtpPassword) ? null : SmtpPassword,
                AlertEmailRecipients.Trim(), SmtpUseTls));
            CompanyEmailTestSuccess = result.Success;
            CompanyEmailTestResult = result.Success
                ? Loc["Test email sent. Check the recipient inbox."]
                : "❌ " + (result.ErrorMessage ?? Loc["Email test failed."]);
        }
        catch (Exception ex)
        {
            CompanyEmailTestSuccess = false;
            CompanyEmailTestResult = "❌ " + ex.Message;
        }
        finally { IsTestingCompanyEmail = false; }
    }


    public string SuggestedBackupName => _maintenance.SuggestBackupFileName();

    public void Initialize(UserSession session)
    {
        Refresh();
        var previousCompanyId = _activeCompanyId;
        _activeCompanyId = session.CompanyId;
        _ = RefreshSystemHealthAsync();
        if (previousCompanyId != _activeCompanyId || _loadedCompanySettings == null)
            _ = LoadCompanySettingsAsync();
        _ = LoadOfficialHolidaysAsync();
        _ = LoadTerminalAnnouncementsAsync();
        _ = LoadWeatherBranchesAsync();
    }

    private async Task LoadOfficialHolidaysAsync(bool synchronize = true)
    {
        OfficialHolidays.Clear();
        if (_activeCompanyId is not { } companyId)
        {
            OfficialHolidaysStatus = Loc[L.OfficialHolidaysSelectCompany];
            return;
        }

        try
        {
            var company = await _companyService.GetByIdAsync(companyId);
            if (company == null)
            {
                OfficialHolidaysStatus = Loc[L.OfficialHolidaysLoadFailed];
                return;
            }

            var currentYear = DateTime.Today.Year;
            var displayYears = new[] { currentYear, currentYear + 1 };
            var catalogueYears = new[] { currentYear - 1, currentYear, currentYear + 1 };
            var synchronizedYears = synchronize
                ? await _holidayCatalog.SynchronizeAsync(catalogueYears)
                : (int?)null;
            var catalogue = await _holidayCatalog.GetForYearsAsync(displayYears);
            var enabledDates = CompanyHolidayCalendar.Parse(company.OfficialHolidayDatesJson);

            foreach (var holiday in catalogue.OrderBy(h => h.Date))
            {
                var item = new OfficialHolidayEdit(holiday.Date, holiday.Name)
                {
                    IsEnabled = enabledDates.Contains(holiday.Date.ToString("yyyy-MM-dd"))
                };
                OfficialHolidays.Add(item);
            }

            var loadedText = string.Format(Loc[L.OfficialHolidaysLoaded], company.Name, $"{currentYear}–{currentYear + 1}");
            OfficialHolidaysStatus = synchronizedYears.HasValue
                ? loadedText + " " + string.Format(Loc[L.OfficialHolidaysSyncComplete], synchronizedYears.Value)
                : loadedText;
        }
        catch (Exception ex)
        {
            OfficialHolidaysStatus = string.Format(Loc[L.OfficialHolidaysLoadFailedDetails], ex.Message);
        }
    }

    [RelayCommand]
    private async Task SynchronizeOfficialHolidaysAsync()
    {
        try
        {
            var currentYear = DateTime.Today.Year;
            var synchronized = await _holidayCatalog.SynchronizeAsync(new[] { currentYear - 1, currentYear, currentYear + 1 });
            await LoadOfficialHolidaysAsync(synchronize: false);
            OfficialHolidaysStatus += " " + string.Format(Loc[L.OfficialHolidaysSyncComplete], synchronized);
        }
        catch (Exception ex)
        {
            OfficialHolidaysStatus = string.Format(Loc[L.OfficialHolidaysLoadFailedDetails], ex.Message);
        }
    }

    [RelayCommand]
    private async Task AddCustomOfficialHolidayAsync()
    {
        if (NewOfficialHolidayDate is not { } date || string.IsNullOrWhiteSpace(NewOfficialHolidayName))
        {
            OfficialHolidaysStatus = Loc[L.OfficialHolidayCustomRequired];
            return;
        }

        try
        {
            await _holidayCatalog.AddCustomAsync(DateOnly.FromDateTime(date.DateTime), NewOfficialHolidayName);
            NewOfficialHolidayName = string.Empty;
            await LoadOfficialHolidaysAsync(synchronize: false);
            OfficialHolidaysStatus = Loc[L.OfficialHolidayCustomAdded];
        }
        catch (Exception ex)
        {
            OfficialHolidaysStatus = string.Format(Loc[L.OfficialHolidaysLoadFailedDetails], ex.Message);
        }
    }

    [RelayCommand]
    private async Task SaveOfficialHolidaysAsync()
    {
        if (_activeCompanyId is not { } companyId)
        {
            OfficialHolidaysStatus = Loc[L.OfficialHolidaysSelectCompany];
            return;
        }

        try
        {
            var company = await _companyService.GetByIdAsync(companyId)
                ?? throw new InvalidOperationException(Loc[L.OfficialHolidaysLoadFailed]);
            var displayedYears = new HashSet<int> { DateTime.Today.Year, DateTime.Today.Year + 1 };
            var preservedDates = CompanyHolidayCalendar.Parse(company.OfficialHolidayDatesJson)
                .Select(value => DateOnly.Parse(value))
                .Where(date => !displayedYears.Contains(date.Year));
            company.OfficialHolidayDatesJson = CompanyHolidayCalendar.Serialize(
                preservedDates.Concat(OfficialHolidays.Where(h => h.IsEnabled).Select(h => h.Date)));
            await _companyService.UpdateAsync(company);
            await _cacheSync.RefreshCacheFromMainDatabaseAsync(companyId);
            OfficialHolidaysStatus = Loc[L.OfficialHolidaysSaved];
        }
        catch (Exception ex)
        {
            OfficialHolidaysStatus = string.Format(Loc[L.OfficialHolidaysSaveFailed], ex.Message);
        }
    }

    private void Refresh()
    {
        DatabaseInfo = _maintenance.DescribeDatabase();
        try
        {
            var s = _maintenance.GetLocalCacheStatus();
            CacheInfo = string.Format(Loc["Vm.Unsynced scans: {0} · Unresolved failed submissions: {1}"], s.PendingScans, s.FailedSubmissions);
        }
        catch (Exception ex) { CacheInfo = string.Format(Loc["Vm.Cache unreadable: {0}"], ex.Message); }
    }


    private void RefreshAutomaticBackupStatus()
    {
        AutomaticBackupStatus = _automaticBackup.Status;
        AutomaticBackupLastSuccess = _automaticBackup.LastSuccessLocal is { } last
            ? last.ToString("yyyy-MM-dd HH:mm:ss")
            : Loc["Vm.No automatic backup completed yet."];
        AutomaticBackupNextRun = _automaticBackup.NextRunLocal is { } next
            ? next.ToString("yyyy-MM-dd HH:mm:ss")
            : Loc["Vm.Not scheduled"];
    }

    [RelayCommand]
    private void SaveAutomaticBackupSettings()
    {
        try
        {
            _automaticBackup.SaveSettings(AutomaticBackupEnabled, AutomaticBackupIntervalHours,
                AutomaticBackupRetentionCount, AutomaticBackupFolder);
            RefreshAutomaticBackupStatus();
            StatusMessage = Loc["Vm.Automatic backup settings saved."];
        }
        catch (Exception ex)
        {
            AutomaticBackupStatus = string.Format(Loc["Vm.Could not save automatic backup settings: {0}"], ex.Message);
        }
    }

    public async Task BackupToAsync(string path)
    {
        IsBusy = true;
        try
        {
            var message = await _maintenance.BackupAsync(path);
            LastBackupInfo = string.Format(Loc["Vm.Last backup verified at {0}"], DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            StatusMessage = "✅ " + message;
        }
        catch (Exception ex) { StatusMessage = string.Format(Loc["Vm.❌ Backup failed: {0}"], ex.Message); }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private void AskDeleteCache()
    {
        _pending = PendingAction.DeleteCache;
        IsDeleteDatabaseConfirmation = false;
        ConfirmPhraseInput = string.Empty;
        string extra;
        try
        {
            var s = _maintenance.GetLocalCacheStatus();
            extra = s.PendingScans + s.FailedSubmissions > 0
                ? string.Format(Loc["Vm. It still holds {0} unsynced scan(s) and {1} unresolved failed submission(s); a .bak copy of the file is kept."], s.PendingScans, s.FailedSubmissions)
                : string.Empty;
        }
        catch { extra = string.Empty; }
        ConfirmText = Loc["Vm.Delete the temporary local database? It is recreated empty automatically."] + extra;
        IsConfirmOpen = true;
    }

    [RelayCommand]
    private void AskDeleteDatabase()
    {
        _pending = PendingAction.DeleteDatabase;
        IsDeleteDatabaseConfirmation = true;
        ConfirmPhraseInput = string.Empty;
        ConfirmText = string.Format(Loc["Vm.PERMANENTLY DELETE the main database ({0})? All companies, employees, schedules and logs are lost. Make a backup first. The setup wizard opens afterwards."], DatabaseInfo)
            + Environment.NewLine + Loc["Vm.Type DELETE to confirm database deletion."];
        IsConfirmOpen = true;
    }

    [RelayCommand]
    private void CancelConfirm()
    {
        _pending = PendingAction.None;
        IsDeleteDatabaseConfirmation = false;
        ConfirmPhraseInput = string.Empty;
        IsConfirmOpen = false;
    }

    [RelayCommand]
    private async Task ConfirmAsync()
    {
        var action = _pending;
        if (action == PendingAction.DeleteDatabase &&
            !string.Equals(ConfirmPhraseInput?.Trim(), "DELETE", StringComparison.Ordinal))
        {
            ConfirmText = string.Format(Loc["Vm.PERMANENTLY DELETE the main database ({0})? All companies, employees, schedules and logs are lost. Make a backup first. The setup wizard opens afterwards."], DatabaseInfo)
                + Environment.NewLine + Loc["Vm.Type DELETE to confirm database deletion."]
                + Environment.NewLine + Loc["Vm.Database deletion blocked: type DELETE exactly to confirm."];
            return;
        }

        _pending = PendingAction.None;
        IsDeleteDatabaseConfirmation = false;
        ConfirmPhraseInput = string.Empty;
        IsConfirmOpen = false;
        IsBusy = true;
        try
        {
            switch (action)
            {
                case PendingAction.DeleteCache:
                    var bak = _maintenance.DeleteLocalCache();
                    StatusMessage = Loc["Vm.✅ Local cache deleted"] + (bak != null ? string.Format(Loc["Vm. (backup: {0})."], bak) : ".") +
                                    Loc["Vm. It is recreated on next use — use 🔄 Sync Cache to refill it."];
                    break;

                case PendingAction.DeleteDatabase:
                    await _maintenance.DeleteDatabaseAsync();
                    StatusMessage = Loc["Vm.✅ Database deleted."];
                    DatabaseDeleted?.Invoke(this, EventArgs.Empty);
                    break;
            }
        }
        catch (Exception ex) { StatusMessage = $"❌ {ex.Message}"; }
        finally { IsBusy = false; Refresh(); }
    }
}


/// <summary>Editable administrator row for an Ergani leave category.</summary>
public sealed record LeavePaymentOption(string Key, string Label);

public partial class LeaveTypeEdit : ObservableObject
{
    public LeaveTypeEdit(string code, string description, bool isHourly, bool isPaid, string paymentCategory)
    {
        _code = code; _description = description; _isHourly = isHourly; _isPaid = isPaid; _paymentCategory = paymentCategory;
    }
    [ObservableProperty] private string _code;
    [ObservableProperty] private string _description;
    [ObservableProperty] private bool _isHourly;
    [ObservableProperty] private bool _isPaid;
    [ObservableProperty] private string _paymentCategory;
    [ObservableProperty] private LeavePaymentOption? _selectedPaymentOption;

    partial void OnSelectedPaymentOptionChanged(LeavePaymentOption? value)
    {
        if (value is not null) PaymentCategory = value.Key;
    }
}


/// <summary>Editable row shown in the Administration / Announcements tab.</summary>
public partial class TerminalAnnouncementEdit : ObservableObject
{
    public int Id { get; }
    public string Message { get; }
    public DateTime CreatedAtUtc { get; }
    [ObservableProperty] private bool _isActive;

    public TerminalAnnouncementEdit(int id, string message, bool isActive, DateTime createdAtUtc)
    {
        Id = id;
        Message = message;
        IsActive = isActive;
        CreatedAtUtc = createdAtUtc;
    }
}
