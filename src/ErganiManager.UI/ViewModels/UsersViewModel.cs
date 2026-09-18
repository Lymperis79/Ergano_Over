using System;
using System.Collections.ObjectModel;
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

    partial void OnFormRoleChanged(AppUserRole value) => OnPropertyChanged(nameof(IsFormRoleOperator));

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

        try
        {
            await _userService.CreateAsync(new CreateUserRequest
            {
                Username = FormUsername,
                Password = FormPassword,
                Role = FormRole,
                CompanyId = companyId,
                BranchId = FormRole == AppUserRole.Operator ? FormBranch?.Id : null
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