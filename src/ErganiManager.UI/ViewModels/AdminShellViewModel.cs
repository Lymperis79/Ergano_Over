
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Diagnostics;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ErganiManager.Core.Interfaces;
using ErganiManager.Core.Models;
using ErganiManager.ErganiApi.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ErganiManager.UI.ViewModels;

public enum AdminSection
{
    Dashboard,
    Companies,
    Branches,
    Employees,
    Users,
    Schedules,
    WorkCards,
    WorkCardScan,
    Today,
    Overtime,
    Reports,
    SubmissionLog,
    EmailLog,
    Help,
    Administration,
    DatabaseOptions
}

/// <summary>
/// Admin shell with sidebar navigation.
///
/// For Super Admin users, the shell provides a company selector.
/// The selected company is stored in ICompanyContext and is also
/// passed to child ViewModels through BuildSessionForActiveCompany().
/// </summary>
public partial class AdminShellViewModel : ViewModelBase
{
    private readonly IServiceProvider _services;
    private readonly ICompanyContext _companyContext;
    private readonly ICompanyService _companyService;
    private readonly ICacheSyncService _cacheSync;
    private readonly IErganiHealthCheckService _erganiHealth;

    private readonly Dictionary<AdminSection, ViewModelBase> _sectionCache = new();

    [ObservableProperty]
    private string _welcomeText = string.Empty;

    [ObservableProperty]
    private string _companyDisplayText = string.Empty;

    [ObservableProperty]
    private AdminSection _activeSection = AdminSection.Companies;

    [ObservableProperty]
    private ViewModelBase? _currentSectionViewModel;

    [ObservableProperty]
    private bool _isSuperAdmin;

    [ObservableProperty]
    private bool _hasActiveCompany;

    public ObservableCollection<CompanyDto> SwitchableCompanies { get; } = new();

    [ObservableProperty]
    private CompanyDto? _selectedSwitchCompany;

    // ── Ergani API status ────────────────────────────────────────────────────

    [ObservableProperty]
    private string _erganiStatusIcon = "⚪";

    [ObservableProperty]
    private string _erganiStatusText = string.Empty;

    [ObservableProperty]
    private string _erganiStatusColor = "#888888";

    // ── Global notification bar ──────────────────────────────────────────────

    [ObservableProperty]
    private string _notificationMessage = string.Empty;

    [ObservableProperty]
    private bool _isNotificationError;

    [ObservableProperty]
    private bool _hasNotification;

    [ObservableProperty]
    private string? _notificationFilePath;

    public bool HasNotificationFile => !string.IsNullOrWhiteSpace(NotificationFilePath);

    public void ShowNotification(
        string message,
        bool isError = false,
        string? filePath = null)
    {
        NotificationMessage = message;
        IsNotificationError = isError;
        NotificationFilePath = !string.IsNullOrWhiteSpace(filePath) && System.IO.File.Exists(filePath)
            ? filePath : null;
        OnPropertyChanged(nameof(HasNotificationFile));
        HasNotification = true;

        _ = Task.Delay(10000).ContinueWith(_ =>
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                HasNotification = false;
                NotificationFilePath = null;
                OnPropertyChanged(nameof(HasNotificationFile));
            }));
    }

    [RelayCommand]
    private void OpenNotificationFile()
    {
        if (string.IsNullOrWhiteSpace(NotificationFilePath) || !System.IO.File.Exists(NotificationFilePath))
            return;

        try
        {
            Process.Start(new ProcessStartInfo(NotificationFilePath) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            ShowNotification($"❌ Could not open file: {ex.Message}", isError: true);
        }
    }

    public void ShowError(string message) =>
        ShowNotification(message, isError: true);

    [RelayCommand]
    private void DismissNotification() =>
        HasNotification = false;

    // ── Ergani API status ────────────────────────────────────────────────────

    private void ApplyErganiStatus(
        ErganiManager.ErganiApi.Services.ErganiServiceStatus status)
    {
        ApplyErganiKind(status switch
        {
            ErganiManager.ErganiApi.Services.ErganiServiceStatus.Online => ErganiKind.Online,
            ErganiManager.ErganiApi.Services.ErganiServiceStatus.Offline => ErganiKind.Offline,
            _ => ErganiKind.Unknown
        });
    }

    private enum ErganiKind { Unknown, Checking, Online, Offline, Error }

    private ErganiKind _erganiKind = ErganiKind.Unknown;

    /// <summary>Sets the Ergani status badge. The kind is remembered so the text can be
    /// translated again when the language changes (see <see cref="RefreshLocalizedTexts"/>).</summary>
    private void ApplyErganiKind(ErganiKind kind)
    {
        _erganiKind = kind;
        (ErganiStatusIcon, ErganiStatusText, ErganiStatusColor) = kind switch
        {
            ErganiKind.Online   => ("🟢", Loc["Vm.Ergani: Online"],      "#4CAF50"),
            ErganiKind.Offline  => ("🔴", Loc["Vm.Ergani: Offline"],     "#EF5350"),
            ErganiKind.Checking => ("⏳", Loc["Vm.Ergani: Checking…"],   "#888888"),
            ErganiKind.Error    => ("❌", Loc["Vm.Ergani: Error"],        "#EF5350"),
            _                   => ("⚪", Loc["Vm.Ergani: Unknown"],      "#888888")
        };
    }

    // 0 = nothing yet, 1 = "Managing: name", 2 = super admin without company, 3 = own company name
    private int _companyTextKind;
    private string? _companyTextName;

    /// <summary>True when the header shows the company picture instead of the company name
    /// (only while a company is selected — never for "select a company above").</summary>
    public bool ShowCompanyLogo => Logo.HasLogo && (_companyTextKind == 1 || _companyTextKind == 3);

    public bool ShowCompanyText => !ShowCompanyLogo;

    private void SetCompanyText(int kind, string? name)
    {
        _companyTextKind = kind;
        _companyTextName = name;
        CompanyDisplayText = kind switch
        {
            1 => string.Format(Loc["Vm.Managing: {0}"], name),
            2 => Loc["Vm.Super Admin — select a company above"],
            3 => name ?? Loc["Vm.No company assigned"],
            _ => string.Empty
        };
        OnPropertyChanged(nameof(ShowCompanyLogo));
        OnPropertyChanged(nameof(ShowCompanyText));
    }

    /// <summary>Re-translates the texts that are stored as plain strings when the language changes.</summary>
    /// <summary>
    /// The sections are kept alive between visits and many of them build texts when they load (calendar
    /// cells, table rows, status lines). After a language change they are thrown away and the open one is
    /// opened again, so nothing is left in the old language. (Unsaved input in the open section is lost.)
    /// </summary>
    private void RebuildSectionsForLanguage()
    {
        if (_session == null) return;

        var active = ActiveSection;
        _sectionCache.Clear();
        NavigateTo(active.ToString());
    }

    private void RefreshLocalizedTexts()
    {
        ApplyErganiKind(_erganiKind);
        if (_session != null)
            WelcomeText = string.Format(Loc["Vm.Welcome, {0}"], _session.Username);
        if (_companyTextKind != 0)
            SetCompanyText(_companyTextKind, _companyTextName);
    }

    [RelayCommand]
    private async Task CheckErganiNowAsync()
    {
        var companyId = _companyContext.ActiveCompanyId;

        if (companyId == null)
            return;

        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            ApplyErganiKind(ErganiKind.Checking);
        });

        try
        {
            await using var db =
                new ErganiManager.Data.AppDbContext(
                    _services
                        .GetRequiredService<IConnectionStateService>()
                        .GetDbOptions());

            var company =
                await db.Companies.FindAsync(companyId.Value);

            if (company == null)
                return;

            var protector =
                _services.GetRequiredService<ICredentialProtector>();

            var credentials =
                new ErganiManager.ErganiApi.Models.ErganiCredentials
                {
                    Username = company.ErganiUsername,

                    Password = protector.Unprotect(
                        company.ErganiPasswordEncrypted),

                    BaseUrl = company.ErganiBaseUrl
                };

            var status =
                await _erganiHealth
                    .CheckAsync(credentials)
                    .ConfigureAwait(false);

            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                ApplyErganiStatus(status));
        }
        catch (Exception ex)
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                ApplyErganiKind(ErganiKind.Error);

                ShowError(
                    string.Format(Loc["Vm.Ergani check failed: {0}"], ex.Message));
            });
        }
    }

    // ── Cache sync ───────────────────────────────────────────────────────────

    private async Task SyncCacheAsync(int companyId)
    {
        try
        {
            var result =
                await _cacheSync
                    .RefreshCacheFromMainDatabaseAsync(companyId)
                    .ConfigureAwait(false);

            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                if (result.Success)
                {
                    ShowNotification(
                        string.Format(Loc["Vm.✅ Offline cache updated: {0} employees, {1} schedules."],
                            result.EmployeesSynced, result.SchedulesSynced));
                }
                else
                {
                    ShowError(
                        string.Format(Loc["Vm.⚠️ Cache sync failed: {0}"], result.ErrorMessage));
                }
            });
        }
        catch (Exception ex)
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                ShowError(
                    string.Format(Loc["Vm.⚠️ Cache sync error: {0}"], ex.Message)));
        }
    }

    [RelayCommand]
    private void RefreshCache()
    {
        var companyId =
            _companyContext.ActiveCompanyId;

        if (companyId.HasValue)
        {
            _ = SyncCacheAsync(companyId.Value);
        }
        else
        {
            ShowError(Loc["Vm.Select a company first."]);
        }
    }

    // ── Barcode scan window ──────────────────────────────────────────────────

    [RelayCommand]
    private void OpenScanWindow()
    {
        var vm =
            _services.GetRequiredService<WorkCardScanViewModel>();

        /*
         * IMPORTANT:
         *
         * A Super Admin's original session has CompanyId == null.
         *
         * The scan window must receive a session containing the
         * company currently selected in the main window.
         */
        if (_session != null)
        {
            vm.Initialize(
                BuildSessionForActiveCompany());
        }

        var win =
            new ErganiManager.UI.Views.BarcodeScanWindow
            {
                DataContext = vm
            };

        win.Show();
    }

    public LanguageSelectorViewModel LanguageSelector { get; }

    private UserSession? _session;

    public AdminShellViewModel(
        IServiceProvider services,
        ICompanyContext companyContext,
        ICompanyService companyService,
        ICacheSyncService cacheSync,
        IErganiHealthCheckService erganiHealth)
    {
        _services = services;
        _companyContext = companyContext;
        _companyService = companyService;
        _cacheSync = cacheSync;
        _erganiHealth = erganiHealth;

        LanguageSelector =
            services.GetRequiredService<
                LanguageSelectorViewModel>();

        ApplyErganiKind(ErganiKind.Unknown);
        Loc.LanguageChanged += (_, _) =>
        {
            RefreshLocalizedTexts();
            RebuildSectionsForLanguage();
        };
        Logo.PropertyChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(ShowCompanyLogo));
            OnPropertyChanged(nameof(ShowCompanyText));
        };

        _erganiHealth.StatusChanged += (_, status) =>
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                ApplyErganiStatus(status));
    }

    // ── Menu visibility (per user) and menu groups ───────────────────────

    [ObservableProperty] private bool _showDashboard = true;\n    [ObservableProperty] private bool _showDashboard = true;
    [ObservableProperty] private bool _showCompanies = true;
    [ObservableProperty] private bool _showBranches = true;
    [ObservableProperty] private bool _showEmployees = true;
    [ObservableProperty] private bool _showUsers = true;
    [ObservableProperty] private bool _showSchedules = true;
    [ObservableProperty] private bool _showWorkCards = true;
    [ObservableProperty] private bool _showScan = true;
    [ObservableProperty] private bool _showToday = true;
    [ObservableProperty] private bool _showOvertime = true;
    [ObservableProperty] private bool _showReports = true;
    [ObservableProperty] private bool _showApiLog = true;
    [ObservableProperty] private bool _showEmailLog = true;

    /// <summary>The scan window belongs to the work card module of the license.</summary>
    public bool ShowScanWindowButton => ErganiManager.Licensing.LicenseState.IsMenuLicensed(MenuKeys.Scan);
    [ObservableProperty] private bool _showHelp = true;
    [ObservableProperty] private bool _showAdministration;

    // A group is shown only when at least one of its menus is visible for the user.
    [ObservableProperty] private bool _showMasterData = true;
    [ObservableProperty] private bool _showAdminGroup = true;

    [ObservableProperty] private bool _isMasterDataExpanded = true;
    [ObservableProperty] private bool _isAdminGroupExpanded = true;

    public string MasterDataHeader => (IsMasterDataExpanded ? "▾ " : "▸ ") + Loc["NavMasterData"];
    public string AdminGroupHeader => (IsAdminGroupExpanded ? "▾ " : "▸ ") + Loc["NavAdminGroup"];

    partial void OnIsMasterDataExpandedChanged(bool value) => OnPropertyChanged(nameof(MasterDataHeader));
    partial void OnIsAdminGroupExpandedChanged(bool value) => OnPropertyChanged(nameof(AdminGroupHeader));

    [RelayCommand] private void ToggleMasterData() => IsMasterDataExpanded = !IsMasterDataExpanded;
    [RelayCommand] private void ToggleAdminGroup() => IsAdminGroupExpanded = !IsAdminGroupExpanded;

    /// <summary>Raised when the user chooses Log out; the application shows the login screen again.</summary>
    public event EventHandler? LogoutRequested;

    [RelayCommand] private void Logout() => LogoutRequested?.Invoke(this, EventArgs.Empty);

    /// <summary>Shows only the menus the logged-in user is allowed to see.</summary>
    private void ApplyMenuAccess(UserSession session)
    {
        ShowCompanies      = session.CanSee(MenuKeys.Companies);
        ShowBranches       = session.CanSee(MenuKeys.Branches);
        ShowEmployees      = session.CanSee(MenuKeys.Employees);
        ShowUsers          = session.CanSee(MenuKeys.Users);
        ShowSchedules      = session.CanSee(MenuKeys.Schedules);
        ShowWorkCards      = session.CanSee(MenuKeys.WorkCards);
        ShowScan           = session.CanSee(MenuKeys.Scan);
        ShowToday          = session.CanSee(MenuKeys.Today);
        ShowOvertime       = session.CanSee(MenuKeys.Overtime);
        ShowReports        = session.CanSee(MenuKeys.Reports);
        ShowApiLog         = session.CanSee(MenuKeys.ApiLog);
        ShowEmailLog       = session.CanSee(MenuKeys.ApiLog);   // same permission as the API log
        ShowHelp           = session.CanSee(MenuKeys.Help);
        ShowAdministration = session.CanSee(MenuKeys.Administration);

        ShowMasterData = ShowCompanies || ShowBranches || ShowEmployees;
        ShowAdminGroup = ShowUsers || ShowAdministration;
    }

    /// <summary>The first menu the user can open (the section shown right after login).</summary>
    private static string FirstAllowedSection(UserSession session)
        => MenuKeys.Configurable.FirstOrDefault(session.CanSee) ?? MenuKeys.Help;

    public async void Initialize(UserSession session)
    {
        _session = session;
        _ = Logo.RefreshAsync(session.CompanyId);

        WelcomeText =
            string.Format(Loc["Vm.Welcome, {0}"], session.Username);

        IsSuperAdmin =
            session.IsSuperAdmin;

        ApplyMenuAccess(session);

        HasActiveCompany =
            _companyContext.ActiveCompanyId.HasValue;

        if (session.IsSuperAdmin)
        {
            var companies =
                await _companyService
                    .GetAllAsync()
                    .ConfigureAwait(false);

            await Avalonia.Threading.Dispatcher.UIThread
                .InvokeAsync(() =>
                {
                    SwitchableCompanies.Clear();

                    foreach (var company in
                             companies.Where(c => c.IsActive))
                    {
                        SwitchableCompanies.Add(company);
                    }

                    /*
                     * IMPORTANT:
                     *
                     * First try the company already stored in
                     * ICompanyContext.
                     *
                     * Only if there is no active company do we
                     * fall back to the first active company.
                     */
                    var activeCompanyId =
                        _companyContext.ActiveCompanyId;

                    if (activeCompanyId.HasValue)
                    {
                        SelectedSwitchCompany =
                            SwitchableCompanies.FirstOrDefault(
                                c => c.Id ==
                                     activeCompanyId.Value);
                    }

                    if (SelectedSwitchCompany == null &&
                        SwitchableCompanies.Count > 0)
                    {
                        SelectedSwitchCompany =
                            SwitchableCompanies[0];
                    }
                });

            if (SelectedSwitchCompany != null)
            {
                SetCompanyText(1, SelectedSwitchCompany.Name);
            }
            else
            {
                SetCompanyText(2, null);
            }
        }
        else
        {
            SetCompanyText(3, session.CompanyName);
        }

        NavigateTo("Dashboard");

        var companyId =
            _companyContext.ActiveCompanyId;

        if (companyId.HasValue)
        {
            _ = SyncCacheAsync(companyId.Value);
            _ = CheckErganiNowAsync();
        }
    }


    partial void OnSelectedSwitchCompanyChanged(CompanyDto? value)
    {
        if (value == null || _session == null)
            return;

        // Update the displayed name, but don't refresh everything
        // if this is already the active company.
        SetCompanyText(1, value.Name);

        if (_companyContext.ActiveCompanyId == value.Id)
        {
            HasActiveCompany = true;
            return;
        }

        // Do not silently discard company-specific settings edits when the main company selector changes.
        var dirtySettingsVm = _sectionCache.Values
            .OfType<AdministrationViewModel>()
            .FirstOrDefault(vm => vm.IsCompanySettingsDirty);
        if (dirtySettingsVm != null)
        {
            var currentCompanyId = _companyContext.ActiveCompanyId;
            SelectedSwitchCompany = currentCompanyId.HasValue
                ? SwitchableCompanies.FirstOrDefault(c => c.Id == currentCompanyId.Value)
                : null;
            dirtySettingsVm.CompanySettingsStatus = Loc["Unsaved company settings. Save or discard the changes before switching companies."];
            ShowNotification(Loc["Unsaved company settings. Save or discard the changes before switching companies."]);
            return;
        }

        // Only perform the expensive operations on a real company switch.
        _companyContext.SwitchCompany(value.Id, value.Name);
        HasActiveCompany = true;

        foreach (var kvp in _sectionCache)
        {
            if (kvp.Value is IAdminSectionViewModel sectionVm)
            {
                sectionVm.Initialize(BuildSessionForActiveCompany());
            }
        }

        _ = Logo.RefreshAsync(value.Id);
        _ = SyncCacheAsync(value.Id);
        _ = CheckErganiNowAsync();
    }


    /// <summary>
    /// Creates a company-scoped session for the currently
    /// selected company.
    ///
    /// Super Admin login itself has CompanyId == null.
    /// Child ViewModels, however, need the active CompanyId.
    /// </summary>
    private UserSession BuildSessionForActiveCompany()
    {
        if (_session == null)
            throw new InvalidOperationException(
                "Session not set.");

        if (!_session.IsSuperAdmin)
            return _session;

        return new UserSession
        {
            UserId = _session.UserId,
            Username = _session.Username,
            Role = _session.Role,

            CompanyId =
                _companyContext.ActiveCompanyId,

            CompanyName =
                SelectedSwitchCompany?.Name,

            BranchId = null,
            BranchName = null,

            IsOfflineSession =
                _session.IsOfflineSession
        };
    }

    private async Task RefreshSwitchableCompaniesAsync()
    {
        if (_session is not { IsSuperAdmin: true })
            return;

        var companies =
            await _companyService
                .GetAllAsync();

        var previouslySelectedId =
            SelectedSwitchCompany?.Id;

        await Avalonia.Threading.Dispatcher.UIThread
            .InvokeAsync(() =>
            {
                SwitchableCompanies.Clear();

                foreach (var company in
                         companies.Where(c => c.IsActive))
                {
                    SwitchableCompanies.Add(company);
                }

                if (previouslySelectedId.HasValue)
                {
                    SelectedSwitchCompany =
                        SwitchableCompanies.FirstOrDefault(
                            c => c.Id ==
                                 previouslySelectedId.Value);
                }

                if (SelectedSwitchCompany == null)
                {
                    var activeId =
                        _companyContext.ActiveCompanyId;

                    if (activeId.HasValue)
                    {
                        SelectedSwitchCompany =
                            SwitchableCompanies.FirstOrDefault(
                                c => c.Id == activeId.Value);
                    }
                }
            });
    }

    [RelayCommand]
    private void NavigateTo(string sectionName)
    {
        if (!Enum.TryParse<AdminSection>(
                sectionName,
                out var section))
        {
            Serilog.Log.Warning(
                "Unknown AdminSection: {SectionName}",
                sectionName);

            return;
        }

        // A menu hidden for this user cannot be opened (also not through a stale command).
        if (_session != null && section != AdminSection.Dashboard &&
            !(section == AdminSection.DatabaseOptions
                ? _session.CanSee(MenuKeys.Administration)
                : _session.CanSee(sectionName)))
            return;

        var wasCached = _sectionCache.ContainsKey(section);

        // Poll the Email Log only while that section is selected.
        if (CurrentSectionViewModel is EmailLogViewModel previousEmailLogVm)
            previousEmailLogVm.StopAutoRefresh();

        ActiveSection = section;

        if (!_sectionCache.TryGetValue(
                section,
                out var vm))
        {
            vm = section switch
            {
                AdminSection.Dashboard =>
                    (ViewModelBase)_services.GetRequiredService<DashboardViewModel>(),

                AdminSection.Companies =>
                    (ViewModelBase)
                        _services
                            .GetRequiredService<
                                CompaniesViewModel>(),

                AdminSection.Branches =>
                    _services
                        .GetRequiredService<
                            BranchesViewModel>(),

                AdminSection.Employees =>
                    _services
                        .GetRequiredService<
                            EmployeesViewModel>(),

                AdminSection.Users =>
                    _services
                        .GetRequiredService<
                            UsersViewModel>(),

                AdminSection.Schedules =>
                    _services
                        .GetRequiredService<
                            SchedulesViewModel>(),

                AdminSection.WorkCards =>
                    _services
                        .GetRequiredService<
                            WorkCardHistoryViewModel>(),

                AdminSection.WorkCardScan =>
                    _services
                        .GetRequiredService<
                            WorkCardScanViewModel>(),

                AdminSection.Today =>
                    _services
                        .GetRequiredService<
                            TodayBoardViewModel>(),

                AdminSection.Reports =>
                    _services
                        .GetRequiredService<
                            ReportsViewModel>(),

                AdminSection.Overtime =>
                    _services
                        .GetRequiredService<
                            OvertimeViewModel>(),

                AdminSection.SubmissionLog =>
                    _services
                        .GetRequiredService<
                            SubmissionLogViewModel>(),

                AdminSection.EmailLog =>
                    _services
                        .GetRequiredService<
                            EmailLogViewModel>(),

                AdminSection.Help =>
                    _services
                        .GetRequiredService<
                            HelpViewModel>(),

                AdminSection.Administration =>
                    _services
                        .GetRequiredService<
                            AdministrationViewModel>(),

                AdminSection.DatabaseOptions =>
                    _services
                        .GetRequiredService<
                            AdministrationViewModel>(),

                _ =>
                    throw new ArgumentOutOfRangeException(
                        nameof(section))
            };

            _sectionCache[section] = vm;

            if (vm is WorkCardHistoryViewModel workCardsVm)
            {
                workCardsVm.FileSaved += (message, filePath) =>
                    Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                        ShowNotification(message, filePath: filePath));
            }

            if (vm is CompaniesViewModel companiesVm)
            {
                companiesVm.CompaniesChanged +=
                    async (_, _) =>
                        await RefreshSwitchableCompaniesAsync();
            }

            if (vm is IAdminSectionViewModel sectionVm &&
                _session != null)
            {
                sectionVm.Initialize(
                    BuildSessionForActiveCompany());
            }
        }

        if (vm is AdministrationViewModel administrationVm)
            administrationVm.IsDatabaseOptionsView = section == AdminSection.DatabaseOptions;

        CurrentSectionViewModel = vm;

        // Reload immediately on every visit, then poll for delivery/status changes while visible.
        if (section == AdminSection.EmailLog && vm is EmailLogViewModel emailLogVm)
            _ = emailLogVm.RefreshOnFormSelectedAsync();

        // Automatically load saved work-card results whenever the Work Cards form is selected.
        // This loads the application's history; it does not open the external Ergani portal.
        if (section == AdminSection.WorkCards && vm is WorkCardHistoryViewModel workCardsHistoryVm)
            _ = workCardsHistoryVm.LoadOnFormSelectedAsync();

        // Cached sections keep their view models; refresh overtime records on every revisit.
        if (wasCached && section == AdminSection.Overtime && vm is OvertimeViewModel overtimeVm)
            _ = overtimeVm.RefreshAsync();
        else if (wasCached && section == AdminSection.Schedules && vm is SchedulesViewModel schedulesVm)
            _ = schedulesVm.RefreshAsync();
    }
}

/// <summary>
/// Implemented by every Admin section ViewModel so the shell
/// can initialize it with the current session/company context.
/// </summary>
public interface IAdminSectionViewModel
{
    void Initialize(UserSession session);
}
