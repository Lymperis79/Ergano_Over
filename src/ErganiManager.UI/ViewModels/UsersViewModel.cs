using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ErganiManager.Core.Interfaces;
using ErganiManager.Core.Models;

namespace ErganiManager.UI.ViewModels;

public partial class UsersViewModel : ViewModelBase, IAdminSectionViewModel
{
    private readonly IUserManagementService _userService;
    private readonly IBranchService _branchService;
    private UserSession? _session;

    public ObservableCollection<AppUserDto> Users { get; } = new();
    public ObservableCollection<BranchDto> AvailableBranches { get; } = new();
    public ObservableCollection<AppUserRole> AvailableRoles { get; } = new(Enum.GetValues<AppUserRole>());

    [ObservableProperty] private bool _isCreating;
    [ObservableProperty] private string _statusMessage = string.Empty;
    [ObservableProperty] private bool _hasActiveCompany;
    [ObservableProperty] private string _noCompanyMessage = string.Empty;

    [ObservableProperty] private string _formUsername = string.Empty;
    [ObservableProperty] private string _formPassword = string.Empty;
    [ObservableProperty] private AppUserRole _formRole = AppUserRole.Operator;
    [ObservableProperty] private BranchDto? _formBranch;

    public bool IsFormRoleOperator => FormRole == AppUserRole.Operator;
    public bool IsFormRoleAdmin => FormRole == AppUserRole.Admin;

    partial void OnFormRoleChanged(AppUserRole value)
    {
        OnPropertyChanged(nameof(IsFormRoleOperator));
        OnPropertyChanged(nameof(IsFormRoleAdmin));
    }

    // ── Which menus the user may see (create form and "Menus" editor share the list) ──
    public ObservableCollection<MenuChoiceViewModel> MenuChoices { get; } = new();
    [ObservableProperty] private AppUserDto? _editingMenusUser;
    [ObservableProperty] private string _menusPanelTitle = string.Empty;

    private static string MenuLabelKey(string menuKey) => menuKey switch
    {
        MenuKeys.Companies => "NavCompanies",
        MenuKeys.Branches  => "NavBranches",
        MenuKeys.Employees => "NavEmployees",
        MenuKeys.Users     => "NavUsers",
        MenuKeys.Schedules => "NavSchedules",
        MenuKeys.WorkCards => "NavWorkCards",
        MenuKeys.Scan      => "NavScan",
        MenuKeys.Today     => "NavToday",
        MenuKeys.Overtime  => "NavOvertime",
        MenuKeys.Reports   => "NavReports",
        MenuKeys.ApiLog    => "NavApiLog",
        _                  => "NavHelp"
    };

    /// <summary>Fills the checkbox list; <paramref name="allowed"/> null = every menu ticked.</summary>
    private void BuildMenuChoices(System.Collections.Generic.HashSet<string>? allowed)
    {
        MenuChoices.Clear();
        foreach (var key in MenuKeys.Configurable)
            MenuChoices.Add(new MenuChoiceViewModel(key, Loc[MenuLabelKey(key)], allowed == null || allowed.Contains(key)));
    }

    private string? SelectedMenusOrNull()
        => MenuKeys.Serialize(MenuChoices.Where(m => m.IsSelected).Select(m => m.Key));

    [RelayCommand]
    private void SelectAllMenus()
    {
        foreach (var m in MenuChoices) m.IsSelected = true;
    }

    [RelayCommand]
    private void ClearMenus()
    {
        foreach (var m in MenuChoices) m.IsSelected = false;
    }

    [RelayCommand]
    private void StartEditMenus(AppUserDto user)
    {
        StatusMessage = string.Empty;
        if (user.Role != AppUserRole.Admin)
        {
            StatusMessage = Loc["UsersMenusAdminOnly"];
            return;
        }

        EditingMenusUser = user;
        MenusPanelTitle = string.Format(Loc["UsersMenusFor"], user.Username);
        BuildMenuChoices(MenuKeys.Parse(user.AllowedMenus));
    }

    [RelayCommand]
    private void CancelEditMenus() => EditingMenusUser = null;

    [RelayCommand]
    private async Task SaveMenusAsync()
    {
        if (EditingMenusUser == null) return;
        if (!MenuChoices.Any(m => m.IsSelected))
        {
            StatusMessage = Loc["UsersMenusNeedOne"];
            return;
        }

        try
        {
            await _userService.UpdateMenusAsync(EditingMenusUser.Id, SelectedMenusOrNull());
            EditingMenusUser = null;
            StatusMessage = Loc["UsersMenusSaved"];
            await LoadAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = $"❌ {ex.Message}";
        }
    }

    // Password reset (per-row)
    [ObservableProperty] private AppUserDto? _resettingUser;
    [ObservableProperty] private string _resetPasswordValue = string.Empty;

    public UsersViewModel(IUserManagementService userService, IBranchService branchService)
    {
        _userService = userService;
        _branchService = branchService;
    }

    public void Initialize(UserSession session)
    {
        _session = session;
        HasActiveCompany = session.CompanyId.HasValue;
        NoCompanyMessage = session.CompanyId.HasValue
            ? string.Empty
            : "Select a company first (Super Admin: pick a company from the Companies tab).";

        if (HasActiveCompany)
            _ = LoadAsync();
    }

    [RelayCommand]
    private async Task LoadAsync()
    {
        if (_session?.CompanyId is not int companyId)
            return;

        var branches = await _branchService.GetByCompanyAsync(companyId).ConfigureAwait(false);
        await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
        {
            AvailableBranches.Clear();
            foreach (var b in branches)
                AvailableBranches.Add(b);
        });

        var list = await _userService.GetByCompanyAsync(companyId).ConfigureAwait(false);
        await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
        {
            Users.Clear();
            foreach (var u in list)
                Users.Add(u);
        });
    }

    [RelayCommand]
    private void StartCreate()
    {
        FormUsername = string.Empty;
        FormPassword = string.Empty;
        FormRole = AppUserRole.Operator;
        FormBranch = AvailableBranches.Count > 0 ? AvailableBranches[0] : null;
        BuildMenuChoices(null);   // every menu ticked by default
        StatusMessage = string.Empty;
        IsCreating = true;
    }

    [RelayCommand]
    private void CancelCreate() => IsCreating = false;

    [RelayCommand]
    private async Task SaveNewUserAsync()
    {
        if (_session?.CompanyId is not int companyId)
            return;

        if (string.IsNullOrWhiteSpace(FormUsername) || FormPassword.Length < 8)
        {
            StatusMessage = "Username is required and password must be at least 8 characters.";
            return;
        }

        if (FormRole == AppUserRole.Admin && !MenuChoices.Any(m => m.IsSelected))
        {
            StatusMessage = Loc["UsersMenusNeedOne"];
            return;
        }

        try
        {
            await _userService.CreateAsync(new CreateUserRequest
            {
                Username = FormUsername,
                Password = FormPassword,
                Role = FormRole,
                CompanyId = companyId,
                BranchId = FormRole == AppUserRole.Operator ? FormBranch?.Id : null,
                // Menu restrictions apply to administrators (operators use the scanning terminal).
                AllowedMenus = FormRole == AppUserRole.Admin ? SelectedMenusOrNull() : null
            });

            IsCreating = false;
            await LoadAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = $"❌ {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task ToggleActiveAsync(AppUserDto user)
    {
        await _userService.SetActiveAsync(user.Id, !user.IsActive);
        await LoadAsync();
    }

    [RelayCommand]
    private void StartResetPassword(AppUserDto user)
    {
        ResettingUser = user;
        ResetPasswordValue = string.Empty;
        StatusMessage = string.Empty;
    }

    [RelayCommand]
    private void CancelResetPassword() => ResettingUser = null;

    [RelayCommand]
    private async Task ConfirmResetPasswordAsync()
    {
        if (ResettingUser == null)
            return;

        if (ResetPasswordValue.Length < 8)
        {
            StatusMessage = "Password must be at least 8 characters.";
            return;
        }

        try
        {
            await _userService.ResetPasswordAsync(ResettingUser.Id, ResetPasswordValue);
            ResettingUser = null;
            StatusMessage = "✅ Password reset.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"❌ {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task DeleteAsync(AppUserDto item)
    {
        if (!await ConfirmDeleteAsync(item)) return;
        try
        {
            await _userService.DeleteAsync(item.Id);
            await LoadCommand.ExecuteAsync(null);
        }
        catch (Exception ex)
        {
            StatusMessage = $"❌ {ex.Message}";
        }
    }

    // Simple confirm: returns true always for now; 
    // replace with a dialog service if desired.
    private static Task<bool> ConfirmDeleteAsync(object item) 
        => Task.FromResult(true);

}

/// <summary>One tick box of the "visible menus" list.</summary>
public partial class MenuChoiceViewModel : ObservableObject
{
    public MenuChoiceViewModel(string key, string label, bool isSelected)
    {
        Key = key;
        Label = label;
        _isSelected = isSelected;
    }

    public string Key { get; }
    public string Label { get; }

    [ObservableProperty] private bool _isSelected;
}
