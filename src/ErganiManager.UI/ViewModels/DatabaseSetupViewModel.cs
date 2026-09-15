using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ErganiManager.Core.Interfaces;
using ErganiManager.Data;
using ErganiManager.LocalCache;

namespace ErganiManager.UI.ViewModels;

public partial class DatabaseSetupViewModel : ViewModelBase
{
    private readonly IConnectionStateService _connectionState;
    private readonly IAdminBootstrapService _adminBootstrap;

    public event EventHandler? SetupCompleted;

    public ObservableCollection<DatabaseProvider> AvailableProviders { get; } =
        new(Enum.GetValues<DatabaseProvider>());

    [ObservableProperty]
    private DatabaseProvider _selectedProvider = DatabaseProvider.Sqlite;

    // SQL Server fields
    [ObservableProperty] private string _sqlServerHost = @"localhost\SQLEXPRESS";
    [ObservableProperty] private string _sqlServerDatabase = "ErganiManager";
    [ObservableProperty] private bool _sqlServerUseWindowsAuth = true;
    [ObservableProperty] private string _sqlServerUsername = string.Empty;
    [ObservableProperty] private string _sqlServerPassword = string.Empty;

    // MariaDB fields
    [ObservableProperty] private string _mariaDbHost = "localhost";
    [ObservableProperty] private string _mariaDbDatabase = "erganimanager";
    [ObservableProperty] private string _mariaDbUsername = "root";
    [ObservableProperty] private string _mariaDbPassword = string.Empty;

    [ObservableProperty] private string _statusMessage = string.Empty;
    [ObservableProperty] private bool _isTesting;
    [ObservableProperty] private bool _connectionVerified;

    // Step 2: first admin creation (shown after DB connection succeeds)
    [ObservableProperty] private bool _isDatabaseStepComplete;
    [ObservableProperty] private string _adminUsername = string.Empty;
    [ObservableProperty] private string _adminPassword = string.Empty;
    [ObservableProperty] private string _adminPasswordConfirm = string.Empty;
    [ObservableProperty] private string _adminStatusMessage = string.Empty;
    [ObservableProperty] private bool _isCreatingAdmin;

    public bool IsSqlite => SelectedProvider == DatabaseProvider.Sqlite;
    public bool IsSqlServer => SelectedProvider == DatabaseProvider.SqlServer;
    public bool IsMariaDb => SelectedProvider == DatabaseProvider.MariaDb;

    public DatabaseSetupViewModel(IConnectionStateService connectionState, IAdminBootstrapService adminBootstrap)
    {
        _connectionState = connectionState;
        _adminBootstrap = adminBootstrap;

        // If a config already exists (retry after failed schema creation),
        // pre-populate the form so the user sees their previous settings.
        var existingConfig = connectionState.LoadConfig();
        if (existingConfig != null)
            PrePopulateFromConfig(existingConfig);
    }

    private void PrePopulateFromConfig(DbConfig config)
    {
        SelectedProvider = config.DatabaseProvider;

        if (config.DatabaseProvider == DatabaseProvider.SqlServer && config.ConnectionString != null)
        {
            // Best-effort parse of the connection string back to fields.
            // Exact format depends on what BuildConfig() generated — see below.
            SqlServerHost = ParseConnStringValue(config.ConnectionString, "Server") ?? SqlServerHost;
            SqlServerDatabase = ParseConnStringValue(config.ConnectionString, "Database") ?? SqlServerDatabase;
            SqlServerUseWindowsAuth = config.ConnectionString.Contains("Trusted_Connection=True",
                StringComparison.OrdinalIgnoreCase);
        }
        else if (config.DatabaseProvider == DatabaseProvider.MariaDb && config.ConnectionString != null)
        {
            MariaDbHost = ParseConnStringValue(config.ConnectionString, "Server") ?? MariaDbHost;
            MariaDbDatabase = ParseConnStringValue(config.ConnectionString, "Database") ?? MariaDbDatabase;
            MariaDbUsername = ParseConnStringValue(config.ConnectionString, "User") ?? MariaDbUsername;
        }
    }

    private static string? ParseConnStringValue(string connString, string key)
    {
        foreach (var part in connString.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = part.IndexOf('=');
            if (eq < 0) continue;
            if (part[..eq].Trim().Equals(key, StringComparison.OrdinalIgnoreCase))
                return part[(eq + 1)..].Trim();
        }
        return null;
    }

    partial void OnSelectedProviderChanged(DatabaseProvider value)
    {
        OnPropertyChanged(nameof(IsSqlite));
        OnPropertyChanged(nameof(IsSqlServer));
        OnPropertyChanged(nameof(IsMariaDb));
        ConnectionVerified = false;
        StatusMessage = string.Empty;
    }

    private DbConfig BuildConfig()
    {
        return SelectedProvider switch
        {
            DatabaseProvider.Sqlite => DbConfig.DefaultSqlite(AppPaths.GetAppDataFolder()),

            DatabaseProvider.SqlServer => new DbConfig
            {
                DatabaseProvider = DatabaseProvider.SqlServer,
                ConnectionString = SqlServerUseWindowsAuth
                    ? $"Server={SqlServerHost};Database={SqlServerDatabase};Trusted_Connection=True;TrustServerCertificate=True;"
                    : $"Server={SqlServerHost};Database={SqlServerDatabase};User Id={SqlServerUsername};Password={SqlServerPassword};TrustServerCertificate=True;"
            },

            DatabaseProvider.MariaDb => new DbConfig
            {
                DatabaseProvider = DatabaseProvider.MariaDb,
                ConnectionString = $"Server={MariaDbHost};Database={MariaDbDatabase};User={MariaDbUsername};Password={MariaDbPassword};"
            },

            _ => throw new InvalidOperationException("Unknown provider selected.")
        };
    }

    [RelayCommand]
    private async Task TestConnectionAsync()
    {
        IsTesting = true;
        StatusMessage = "Testing connection...";
        ConnectionVerified = false;

        try
        {
            var config = BuildConfig();
            var (success, error) = await DbProviderFactory.TestConnectionAsync(config);

            if (success)
            {
                StatusMessage = "✅ Connection successful.";
                ConnectionVerified = true;
            }
            else
            {
                StatusMessage = $"❌ Connection failed: {error}";
                ConnectionVerified = false;
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"❌ Connection failed: {ex.Message}";
            ConnectionVerified = false;
        }
        finally
        {
            IsTesting = false;
        }
    }

    [RelayCommand]
    private async Task SaveAndContinueAsync()
    {
        IsTesting = true;
        try
        {
            var config = BuildConfig();

            // Always re-verify right before saving — the user may have edited
            // a field after the last successful test.
            var (success, error) = await DbProviderFactory.TestConnectionAsync(config);
            if (!success)
            {
                StatusMessage = $"❌ Cannot save — connection failed: {error}";
                ConnectionVerified = false;
                return;
            }

            // Apply schema BEFORE saving connection.json so that if EnsureCreated
            // fails the app doesn't get into a state where connection.json exists
            // but the schema is broken — which would skip this wizard on next launch.
            var optionsBuilder = new Microsoft.EntityFrameworkCore.DbContextOptionsBuilder<AppDbContext>();
            DbProviderFactory.Configure(optionsBuilder, config);
            await using var db = new AppDbContext(optionsBuilder.Options);
            await db.Database.EnsureCreatedAsync();

            // Schema is confirmed — now safe to persist the connection config.
            _connectionState.SaveConfig(config);

            StatusMessage = "✅ Database configured and schema applied.";

            // If users already exist (e.g. re-pointing to an existing DB),
            // skip straight to login instead of showing the admin creation step.
            var needsAdmin = await _adminBootstrap.NeedsFirstAdminAsync();
            if (needsAdmin)
            {
                IsDatabaseStepComplete = true;
            }
            else
            {
                SetupCompleted?.Invoke(this, EventArgs.Empty);
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"❌ Setup failed: {ex.Message}";
        }
        finally
        {
            IsTesting = false;
        }
    }

    [RelayCommand]
    private async Task CreateFirstAdminAsync()
    {
        AdminStatusMessage = string.Empty;

        if (string.IsNullOrWhiteSpace(AdminUsername))
        {
            AdminStatusMessage = "Please enter a username.";
            return;
        }

        if (AdminPassword.Length < 8)
        {
            AdminStatusMessage = "Password must be at least 8 characters.";
            return;
        }

        if (AdminPassword != AdminPasswordConfirm)
        {
            AdminStatusMessage = "Passwords do not match.";
            return;
        }

        IsCreatingAdmin = true;
        try
        {
            var result = await _adminBootstrap.CreateFirstAdminAsync(new Core.Interfaces.CreateFirstAdminRequest
            {
                Username = AdminUsername,
                Password = AdminPassword
            });

            if (result.Success)
            {
                SetupCompleted?.Invoke(this, EventArgs.Empty);
            }
            else
            {
                AdminStatusMessage = $"❌ {result.ErrorMessage}";
            }
        }
        finally
        {
            IsCreatingAdmin = false;
        }
    }
}
