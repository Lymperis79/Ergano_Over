using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ErganiManager.Core.Interfaces;
using ErganiManager.Core.Models;

namespace ErganiManager.UI.ViewModels;

public partial class LoginViewModel : ViewModelBase
{
    private readonly IAuthService _authService;

    public event EventHandler<UserSession>? LoginSucceeded;

    [ObservableProperty] private string _username = string.Empty;
    [ObservableProperty] private string _password = string.Empty;
    [ObservableProperty] private string _errorMessage = string.Empty;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private bool _isDegradedMode;

    public string DegradedBannerText =>
        "⚠️ Database unavailable — working in offline mode. Only previously synced users can log in, and clock-in/out scans will be queued until the connection is restored.";

    public LoginViewModel(IAuthService authService)
    {
        _authService = authService;
    }

    [RelayCommand]
    private async Task LoginAsync()
    {
        ErrorMessage = string.Empty;

        if (string.IsNullOrWhiteSpace(Username) || string.IsNullOrWhiteSpace(Password))
        {
            ErrorMessage = "Please enter both username and password.";
            return;
        }

        IsBusy = true;
        try
        {
            var result = await _authService.LoginAsync(Username, Password);

            if (result.Success && result.Session != null)
            {
                LoginSucceeded?.Invoke(this, result.Session);
            }
            else
            {
                ErrorMessage = result.ErrorMessage ?? "Login failed.";
            }
        }
        finally
        {
            IsBusy = false;
        }
    }
}
