using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ErganiManager.Core.Interfaces;
using ErganiManager.Core.Models;
using System.Collections.ObjectModel;
using System.Linq;
using ErganiManager.UI.Localization;

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

    [ObservableProperty] private bool _isConfirmOpen;
    [ObservableProperty] private string _confirmText = string.Empty;

    private readonly IRoundingSettingsService _rounding;
    private readonly IPortalSettingsService _portal;
    private readonly ICompanyService _companyService;
    private readonly ICacheSyncService _cacheSync;
    private readonly IOfficialHolidayCatalogService _holidayCatalog;
    private int? _activeCompanyId;

    [ObservableProperty] private DateTime? _newOfficialHolidayDate = DateTime.Today;
    [ObservableProperty] private string _newOfficialHolidayName = string.Empty;

    public ObservableCollection<OfficialHolidayEdit> OfficialHolidays { get; } = new();
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

    public AdministrationViewModel(IDatabaseMaintenanceService maintenance, IRoundingSettingsService rounding,
        IPortalSettingsService portal, ICompanyService companyService, ICacheSyncService cacheSync,
        IOfficialHolidayCatalogService holidayCatalog)
    {
        _maintenance = maintenance;
        _rounding = rounding;
        _portal = portal;
        _companyService = companyService;
        _cacheSync = cacheSync;
        _holidayCatalog = holidayCatalog;
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

    public string SuggestedBackupName => _maintenance.SuggestBackupFileName();

    public void Initialize(UserSession session)
    {
        Refresh();
        _activeCompanyId = session.CompanyId;
        _ = LoadOfficialHolidaysAsync();
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
            await _holidayCatalog.AddCustomAsync(DateOnly.FromDateTime(date), NewOfficialHolidayName);
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

    /// <summary>Called by the view after the user picked a destination file.</summary>
    public async Task BackupToAsync(string path)
    {
        IsBusy = true;
        try { StatusMessage = "✅ " + await _maintenance.BackupAsync(path); }
        catch (Exception ex) { StatusMessage = string.Format(Loc["Vm.❌ Backup failed: {0}"], ex.Message); }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private void AskDeleteCache()
    {
        _pending = PendingAction.DeleteCache;
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
        ConfirmText = string.Format(Loc["Vm.PERMANENTLY DELETE the main database ({0})? All companies, employees, schedules and logs are lost. Make a backup first. The setup wizard opens afterwards."], DatabaseInfo);
        IsConfirmOpen = true;
    }

    [RelayCommand]
    private void CancelConfirm() { _pending = PendingAction.None; IsConfirmOpen = false; }

    [RelayCommand]
    private async Task ConfirmAsync()
    {
        var action = _pending;
        _pending = PendingAction.None;
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
