using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ErganiManager.Core.Interfaces;
using ErganiManager.Core.Models;
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
    Overtime,
    SubmissionLog
}

/// <summary>
/// Admin shell with sidebar navigation. Hosts one section ViewModel at a time
/// in CurrentSectionViewModel; views are resolved lazily via DI the first
/// time each section is opened, then cached for the rest of the session.
///
/// For super-admins (session.CompanyId == null), a company picker is shown.
/// Selecting a company calls ICompanyContext.SwitchCompany, which raises
/// CompanyChanged — every cached section ViewModel is then re-initialized
/// with the newly active company so Branches/Employees/Users immediately
/// reflect the switch without needing to be re-opened.
/// </summary>
public partial class AdminShellViewModel : ViewModelBase
{
    private readonly IServiceProvider _services;
    private readonly ICompanyContext _companyContext;
    private readonly ICompanyService _companyService;

    private readonly Dictionary<AdminSection, ViewModelBase> _sectionCache = new();

    [ObservableProperty] private string _welcomeText = string.Empty;
    [ObservableProperty] private string _companyDisplayText = string.Empty;
    [ObservableProperty] private AdminSection _activeSection = AdminSection.Companies;
    [ObservableProperty] private ViewModelBase? _currentSectionViewModel;

    [ObservableProperty] private bool _isSuperAdmin;
    [ObservableProperty] private bool _hasActiveCompany;
    public ObservableCollection<CompanyDto> SwitchableCompanies { get; } = new();
    [ObservableProperty] private CompanyDto? _selectedSwitchCompany;

    // ── Global notification bar ───────────────────────────────────────────────
    [ObservableProperty] private string _notificationMessage = string.Empty;
    [ObservableProperty] private bool _isNotificationError;
    [ObservableProperty] private bool _hasNotification;

    public void ShowNotification(string message, bool isError = false)
    {
        NotificationMessage = message;
        IsNotificationError = isError;
        HasNotification = true;
        _ = System.Threading.Tasks.Task.Delay(6000).ContinueWith(_ =>
            Avalonia.Threading.Dispatcher.UIThread.Post(() => HasNotification = false));
    }

    public void ShowError(string message) => ShowNotification(message, isError: true);

    [RelayCommand]
    private void DismissNotification() => HasNotification = false;

    [RelayCommand]
    private void OpenScanWindow()
    {
        var vm = _services.GetRequiredService<WorkCardScanViewModel>();
        if (_session != null) vm.Initialize(_session);
        var win = new ErganiManager.UI.Views.BarcodeScanWindow { DataContext = vm };
        win.Show();
    }

    public LanguageSelectorViewModel LanguageSelector { get; }

    private UserSession? _session;

    public AdminShellViewModel(IServiceProvider services, ICompanyContext companyContext, ICompanyService companyService)
    {
        _services = services;
        _companyContext = companyContext;
        _companyService = companyService;
        LanguageSelector = services.GetRequiredService<LanguageSelectorViewModel>();
    }

    public async void Initialize(UserSession session)
    {
        _session = session;
        WelcomeText = $"Welcome, {session.Username}";
        IsSuperAdmin = session.IsSuperAdmin;
        HasActiveCompany = _companyContext.ActiveCompanyId.HasValue;

        if (session.IsSuperAdmin)
        {
            // Run DB fetch on background thread, update collection on UI thread
            var companies = await _companyService.GetAllAsync().ConfigureAwait(false);
            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
            {
                SwitchableCompanies.Clear();
                foreach (var c in companies.Where(c => c.IsActive))
                    SwitchableCompanies.Add(c);
                if (SwitchableCompanies.Count > 0)
                    SelectedSwitchCompany = SwitchableCompanies[0];
            });
            CompanyDisplayText = "Super Admin — select a company above";
        }
        else
        {
            CompanyDisplayText = session.CompanyName ?? "No company assigned";
        }

        NavigateTo(nameof(AdminSection.Companies));
    }

    partial void OnSelectedSwitchCompanyChanged(CompanyDto? value)
    {
        if (value == null || _session == null)
            return;

        _companyContext.SwitchCompany(value.Id, value.Name);
        CompanyDisplayText = $"Managing: {value.Name}";
        HasActiveCompany = true;

        // Re-initialize every already-opened section so it picks up the new
        // ActiveCompanyId immediately, rather than only on next navigation.
        foreach (var kvp in _sectionCache)
        {
            if (kvp.Value is IAdminSectionViewModel sectionVm)
                sectionVm.Initialize(BuildSessionForActiveCompany());
        }
    }

    /// <summary>
    /// Section ViewModels read CompanyId off the session they're handed.
    /// For a super-admin, that's not the original login session (which has
    /// CompanyId == null) but a constructed view of "the session, but scoped
    /// to whichever company is currently active" — built fresh on every
    /// switch and on every section's first Initialize call.
    /// </summary>
    private UserSession BuildSessionForActiveCompany()
    {
        if (_session == null)
            throw new InvalidOperationException("Session not set.");

        if (!_session.IsSuperAdmin)
            return _session;

        return new UserSession
        {
            UserId = _session.UserId,
            Username = _session.Username,
            Role = _session.Role,
            CompanyId = _companyContext.ActiveCompanyId,
            CompanyName = SelectedSwitchCompany?.Name,
            BranchId = null,
            IsOfflineSession = _session.IsOfflineSession
        };
    }

    private async Task RefreshSwitchableCompaniesAsync()
    {
        if (_session is not { IsSuperAdmin: true })
            return;

        var companies = await _companyService.GetAllAsync();
        var previouslySelectedId = SelectedSwitchCompany?.Id;

        SwitchableCompanies.Clear();
        foreach (var c in companies.Where(c => c.IsActive))
            SwitchableCompanies.Add(c);

        if (previouslySelectedId.HasValue)
            SelectedSwitchCompany = SwitchableCompanies.FirstOrDefault(c => c.Id == previouslySelectedId.Value);
    }

    [RelayCommand]
    private void NavigateTo(string sectionName)
    {
        if (!Enum.TryParse<AdminSection>(sectionName, out var section))
        {
            Serilog.Log.Warning("Unknown AdminSection: {SectionName}", sectionName);
            return;
        }

        ActiveSection = section;

        if (!_sectionCache.TryGetValue(section, out var vm))
        {
            vm = section switch
            {
                AdminSection.Companies => (ViewModelBase)_services.GetRequiredService<CompaniesViewModel>(),
                AdminSection.Branches => _services.GetRequiredService<BranchesViewModel>(),
                AdminSection.Employees => _services.GetRequiredService<EmployeesViewModel>(),
                AdminSection.Users => _services.GetRequiredService<UsersViewModel>(),
                AdminSection.Schedules => _services.GetRequiredService<SchedulesViewModel>(),
                AdminSection.WorkCards    => _services.GetRequiredService<WorkCardHistoryViewModel>(),
                AdminSection.WorkCardScan => _services.GetRequiredService<WorkCardScanViewModel>(),
                AdminSection.Overtime     => _services.GetRequiredService<OvertimeViewModel>(),
                AdminSection.SubmissionLog => _services.GetRequiredService<SubmissionLogViewModel>(),
                _ => throw new ArgumentOutOfRangeException(nameof(section))
            };
            _sectionCache[section] = vm;

            if (vm is CompaniesViewModel companiesVm)
                companiesVm.CompaniesChanged += async (_, _) => await RefreshSwitchableCompaniesAsync();

            // Each section ViewModel implements IAdminSectionViewModel so the
            // shell can hand it the session without every case needing a cast.
            if (vm is IAdminSectionViewModel sectionVm && _session != null)
                sectionVm.Initialize(BuildSessionForActiveCompany());
        }

        CurrentSectionViewModel = vm;
    }
}

/// <summary>Implemented by every Admin section ViewModel so the shell can
/// initialize it with the current session/company context uniformly.</summary>
public interface IAdminSectionViewModel
{
    void Initialize(UserSession session);
}
