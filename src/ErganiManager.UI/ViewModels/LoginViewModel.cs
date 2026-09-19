using System;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ErganiManager.Core.Interfaces;
using ErganiManager.Core.Models;
using ErganiManager.ErganiApi;
using ErganiManager.ErganiApi.Models;
using ErganiManager.ErganiApi.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ErganiManager.UI.ViewModels;

public partial class LoginViewModel : ViewModelBase
{
    private readonly IAuthService _authService;
    private readonly IErganiHealthCheckService _erganiHealth;

    public event EventHandler<UserSession>? LoginSucceeded;

    [ObservableProperty] private string _username = string.Empty;
    [ObservableProperty] private string _password = string.Empty;
    [ObservableProperty] private string _errorMessage = string.Empty;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private bool _isDegradedMode;

    // Ergani status
    [ObservableProperty] private string _erganiStatusIcon  = "⚪";
    [ObservableProperty] private string _erganiStatusText  = "Ergani API: Not checked";
    [ObservableProperty] private string _erganiStatusColor = "#888888";
    [ObservableProperty] private bool   _isCheckingErgani;

    public string DegradedBannerText =>
        "⚠️ Database unavailable — working in offline mode. Only previously synced users can log in.";

    public LoginViewModel(IAuthService authService, IErganiHealthCheckService erganiHealth)
    {
        _authService  = authService;
        _erganiHealth = erganiHealth;
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
                LoginSucceeded?.Invoke(this, result.Session);
            else
                ErrorMessage = result.ErrorMessage ?? "Login failed.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task CheckErganiStatusAsync()
    {
        IsCheckingErgani = true;
        ErganiStatusIcon  = "⏳";
        ErganiStatusText  = "Ergani API: Checking…";
        ErganiStatusColor = "#888888";

        try
        {
            // Use the configured company's Ergani URL, or fall back to the trial URL
            var cs = Program.Services.GetRequiredService<IConnectionStateService>();
            string baseUrl = ErganiEndpoints.TrialBaseUrl;

            if (cs.ConfigExists())
            {
                // Try to find any active company's URL from the DB
                try
                {
                    await using var db = new ErganiManager.Data.AppDbContext(cs.GetDbOptions());
                    var company = await db.Companies
                        .Where(c => c.IsActive && !string.IsNullOrEmpty(c.ErganiBaseUrl))
                        .FirstOrDefaultAsync();
                    if (company != null) baseUrl = company.ErganiBaseUrl;
                }
                catch { /* DB may not be ready yet */ }
            }

            // Ping with empty credentials — just checking reachability
            var creds = new ErganiCredentials
            {
                Username = string.Empty,
                Password = string.Empty,
                BaseUrl  = baseUrl
            };

            var status = await _erganiHealth.CheckAsync(creds);

            (ErganiStatusIcon, ErganiStatusText, ErganiStatusColor) = status switch
            {
                ErganiServiceStatus.Online  => ("🟢", "Ergani API: Online",  "#4CAF50"),
                ErganiServiceStatus.Offline => ("🔴", "Ergani API: Offline", "#EF5350"),
                _                           => ("⚪", "Ergani API: Unknown", "#888888")
            };
        }
        catch (Exception ex)
        {
            ErganiStatusIcon  = "❌";
            ErganiStatusText  = $"Ergani API: Error — {ex.Message}";
            ErganiStatusColor = "#EF5350";
        }
        finally
        {
            IsCheckingErgani = false;
        }
    }
}
