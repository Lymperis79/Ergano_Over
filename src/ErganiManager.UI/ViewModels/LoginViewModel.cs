using System;
using System.Linq;
using System.IO;
using System.Text.Json;
using System.Text;
using System.Security.Cryptography;
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
    private readonly IConnectionStateService _connectionState;
    private static readonly string SavedLoginPath = Path.Combine(ErganiManager.LocalCache.AppPaths.GetAppDataFolder(), "saved-login.dat");

    public event EventHandler<UserSession>? LoginSucceeded;

    [ObservableProperty] private string _username = string.Empty;
    [ObservableProperty] private string _password = string.Empty;
    [ObservableProperty] private string _errorMessage = string.Empty;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private bool _isDegradedMode;
    [ObservableProperty] private bool _rememberCredentials;

    /// <summary>Shown when new tables/columns could not be added to the database (see the log for details).</summary>
    [ObservableProperty] private string _upgradeWarning = string.Empty;

    // Ergani status
    [ObservableProperty] private string _erganiStatusIcon  = "⚪";
    [ObservableProperty] private string _erganiStatusText  = string.Empty;  // set in the constructor (needs Loc)
    [ObservableProperty] private string _erganiStatusColor = "#888888";
    [ObservableProperty] private bool   _isCheckingErgani;

    public string DegradedBannerText =>
        Loc["Vm.⚠️ Database unavailable — working in offline mode. Only previously synced users can log in."];

    public LoginViewModel(IAuthService authService, IErganiHealthCheckService erganiHealth,
        IConnectionStateService connectionState)
    {
        _authService  = authService;
        _erganiHealth = erganiHealth;
        _connectionState = connectionState;
        LoadSavedCredentials();
        UpgradeWarning = connectionState.UpgradeWarning ?? string.Empty;
        ErganiStatusText = Loc["Vm.Ergani API: Not checked"];
    }

    [RelayCommand]
    private async Task LoginAsync()
    {
        ErrorMessage = string.Empty;

        if (string.IsNullOrWhiteSpace(Username) || string.IsNullOrWhiteSpace(Password))
        {
            ErrorMessage = Loc["Vm.Please enter both username and password."];
            return;
        }

        IsBusy = true;
        try
        {
            var result = await _authService.LoginAsync(Username, Password);
            if (result.Success && result.Session != null)
            {
                SaveCredentials();
                LoginSucceeded?.Invoke(this, result.Session);
            }
            else
                ErrorMessage = result.ErrorMessage ?? Loc["Vm.Login failed."];
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "Unexpected error during login.");
            ErrorMessage = string.Format(Loc["Vm.Login error: {0}"], ex.Message);
        }
        finally
        {
            UpgradeWarning = _connectionState.UpgradeWarning ?? string.Empty;
            IsBusy = false;
        }
    }


    private sealed class SavedLogin
    {
        public string Username { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
    }

    private void LoadSavedCredentials()
    {
        try
        {
            if (!File.Exists(SavedLoginPath)) return;
            var encrypted = Convert.FromBase64String(File.ReadAllText(SavedLoginPath));
            var plainBytes = ProtectedData.Unprotect(encrypted, optionalEntropy: null, scope: DataProtectionScope.CurrentUser);
            var json = Encoding.UTF8.GetString(plainBytes);
            var saved = JsonSerializer.Deserialize<SavedLogin>(json);
            if (saved == null || string.IsNullOrWhiteSpace(saved.Username) || string.IsNullOrEmpty(saved.Password))
                return;

            Username = saved.Username;
            Password = saved.Password;
            RememberCredentials = true;
        }
        catch (Exception ex)
        {
            // A credential encrypted on another Windows account/installation must
            // never prevent the login screen from opening.
            Serilog.Log.Warning(ex, "Could not load saved login credentials.");
            RememberCredentials = false;
        }
    }

    partial void OnRememberCredentialsChanged(bool value)
    {
        if (!value) DeleteSavedCredentials();
    }

    private void DeleteSavedCredentials()
    {
        try
        {
            if (File.Exists(SavedLoginPath)) File.Delete(SavedLoginPath);
        }
        catch (Exception ex)
        {
            Serilog.Log.Warning(ex, "Could not delete saved login credentials.");
        }
    }

    private void SaveCredentials()
    {
        try
        {
            if (!RememberCredentials)
            {
                DeleteSavedCredentials();
                return;
            }

            var json = JsonSerializer.Serialize(new SavedLogin
            {
                Username = Username.Trim(),
                Password = Password
            });
            var plainBytes = Encoding.UTF8.GetBytes(json);
            var encrypted = Convert.ToBase64String(ProtectedData.Protect(plainBytes, optionalEntropy: null, scope: DataProtectionScope.CurrentUser));
            var temporaryPath = SavedLoginPath + ".tmp";
            File.WriteAllText(temporaryPath, encrypted);
            File.Move(temporaryPath, SavedLoginPath, overwrite: true);
        }
        catch (Exception ex)
        {
            // Authentication has already succeeded; inability to remember the
            // credentials must not turn a successful login into a failed one.
            Serilog.Log.Warning(ex, "Could not save login credentials.");
        }
    }

    [RelayCommand]
    private async Task CheckErganiStatusAsync()
    {
        IsCheckingErgani = true;
        ErganiStatusIcon  = "⏳";
        ErganiStatusText  = Loc["Vm.Ergani API: Checking…"];
        ErganiStatusColor = "#888888";

        try
        {
            // The company record is the only source of the Ergani API URL.
            // Do not silently fall back to a trial endpoint when it is not configured.
            var cs = Program.Services.GetRequiredService<IConnectionStateService>();
            string baseUrl = string.Empty;

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
                ErganiServiceStatus.Online  => ("🟢", Loc["Vm.Ergani API: Online"],  "#4CAF50"),
                ErganiServiceStatus.Offline => ("🔴", Loc["Vm.Ergani API: Offline"], "#EF5350"),
                _                           => ("⚪", Loc["Vm.Ergani API: Unknown"], "#888888")
            };
        }
        catch (Exception ex)
        {
            ErganiStatusIcon  = "❌";
            ErganiStatusText  = string.Format(Loc["Vm.Ergani API: Error — {0}"], ex.Message);
            ErganiStatusColor = "#EF5350";
        }
        finally
        {
            IsCheckingErgani = false;
        }
    }
}
