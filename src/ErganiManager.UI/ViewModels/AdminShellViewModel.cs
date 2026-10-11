
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
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
    Help,
    Administration
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
    private string _erganiStatusText = "Ergani: Unknown";

    [ObservableProperty]
    private string _erganiStatusColor = "#888888";

    // ── Global notification bar ──────────────────────────────────────────────

    [ObservableProperty]
    private string _notificationMessage = string.Empty;

    [ObservableProperty]
    private bool _isNotificationError;

    [ObservableProperty]
    private bool _hasNotification;

    public void ShowNotification(
        string message,
        bool isError = false)
    {
        NotificationMessage = message;
        IsNotificationError = isError;
        HasNotification = true;

        _ = Task.Delay(6000).ContinueWith(_ =>
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                HasNotification = false));
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
        (ErganiStatusIcon, ErganiStatusText, ErganiStatusColor) = status switch
        {
            ErganiManager.ErganiApi.Services.ErganiServiceStatus.Online =>
                ("🟢", "Ergani: Online", "#4CAF50"),

            ErganiManager.ErganiApi.Services.ErganiServiceStatus.Offline =>
                ("🔴", "Ergani: Offline", "#EF5350"),

            _ =>
                ("⚪", "Ergani: Unknown", "#888888")
        };
    }

    [RelayCommand]
    private async Task CheckErganiNowAsync()
    {
        var companyId = _companyContext.ActiveCompanyId;

        if (companyId == null)
            return;

        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            ErganiStatusIcon = "⏳";
            ErganiStatusText = "Ergani: Checking…";
            ErganiStatusColor = "#888888";
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
                ErganiStatusIcon = "❌";
                ErganiStatusText = "Ergani: Error";
                ErganiStatusColor = "#EF5350";

                ShowError(
                    $"Ergani check failed: {ex.Message}");
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
                        $"✅ Offline cache updated: " +
                        $"{result.EmployeesSynced} employees, " +
                        $"{result.SchedulesSynced} schedules.");
                }
                else
                {
                    ShowError(
                        $"⚠️ Cache sync failed: " +
                        $"{result.ErrorMessage}");
                }
            });
        }
        catch (Exception ex)
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                ShowError(
                    $"⚠️ Cache sync error: {ex.Message}"));
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
            ShowError("Select a company first.");
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

        _erganiHealth.StatusChanged += (_, status) =>
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                ApplyErganiStatus(status));
    }

    // ── Menu visibility (per user) and menu groups ───────────────────────

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

        WelcomeText =
            $"Welcome, {session.Username}";

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
                CompanyDisplayText =
                    $"Managing: {SelectedSwitchCompany.Name}";
            }
            else
            {
                CompanyDisplayText =
                    "Super Admin — select a company above";
            }
        }
        else
        {
            CompanyDisplayText =
                session.CompanyName ??
                "No company assigned";
        }

        NavigateTo(FirstAllowedSection(session));

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
        CompanyDisplayText = $"Managing: {value.Name}";

        if (_companyContext.ActiveCompanyId == value.Id)
        {
            HasActiveCompany = true;
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
        if (_session != null && !_session.CanSee(sectionName))
            return;

        ActiveSection = section;

        if (!_sectionCache.TryGetValue(
                section,
                out var vm))
        {
            vm = section switch
            {
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

                AdminSection.Help =>
                    _services
                        .GetRequiredService<
                            HelpViewModel>(),

                AdminSection.Administration =>
                    _services
                        .GetRequiredService<
                            AdministrationViewModel>(),

                _ =>
                    throw new ArgumentOutOfRangeException(
                        nameof(section))
            };

            _sectionCache[section] = vm;

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

        CurrentSectionViewModel = vm;
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
