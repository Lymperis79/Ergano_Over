using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ErganiManager.Core.Interfaces;
using ErganiManager.Data;
using ErganiManager.LocalCache;
using Microsoft.EntityFrameworkCore;
using Serilog;

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
                // The database itself may simply not exist yet — that is fine on a fresh
                // installation, it is created by "Save and continue".
                var (serverOk, serverError) = await DbProviderFactory.TestServerAsync(config);
                if (serverOk)
                {
                    StatusMessage = "✅ Server reachable. The database does not exist yet and will be created when you continue.";
                    ConnectionVerified = true;
                }
                else
                {
                    StatusMessage = $"❌ Connection failed: {serverError ?? error}";
                    ConnectionVerified = false;
                }
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

    /// <summary>Resumes a setup that stopped after the database step (database ready, no user yet).</summary>
    public void ResumeAtAdminStep()
    {
        IsDatabaseStepComplete = true;
        AdminStatusMessage = "The database is ready. Create the first administrator to finish setup.";
    }

    [RelayCommand]
    private async Task SaveAndContinueAsync()
    {
        IsTesting = true;
        try
        {
            var config = BuildConfig();

            // STEP 1 — create the database (server-side database / SQLite file). No-op if it exists.
            StatusMessage = "Step 1/3: creating the database…";
            try
            {
                await DbProviderFactory.EnsureDatabaseExistsAsync(config);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Setup: cannot create/access the database.");
                StatusMessage = $"❌ Cannot create/access the database: {ex.Message}";
                ConnectionVerified = false;
                return;
            }

            // The database must now exist and accept connections.
            var (success, error) = await DbProviderFactory.TestConnectionAsync(config);
            if (!success)
            {
                Log.Error("Setup: cannot connect to the database: {Error}", error);
                StatusMessage = $"❌ Cannot connect to the database: {error}";
                ConnectionVerified = false;
                return;
            }

            // STEP 2 — create the tables and verify them BEFORE saving connection.json, so a
            // failure never leaves a config behind that skips this wizard on the next launch.
            StatusMessage = "Step 2/3: creating the tables…";
            await DatabaseMigrator.ApplyAsync(config);

            var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();
            DbProviderFactory.Configure(optionsBuilder, config);
            await using (var db = new AppDbContext(optionsBuilder.Options))
            {
                _ = await db.Companies.AnyAsync();
                _ = await db.Users.AnyAsync();
            }

            // STEP 3 — the schema is confirmed, persist the connection configuration.
            _connectionState.SaveConfig(config);
            StatusMessage = "✅ Database created. Step 3/3: administrator account.";

            // If users already exist (e.g. re-pointing to an existing DB), make sure the offline
            // copy exists and go straight to login; otherwise ask for the first administrator.
            var needsAdmin = await _adminBootstrap.NeedsFirstAdminAsync();
            if (needsAdmin)
            {
                IsDatabaseStepComplete = true;
            }
            else
            {
                try { await _adminBootstrap.SeedOfflineCacheAsync(); }
                catch (Exception ex) { Log.Error(ex, "Setup: could not initialize the offline database."); }
                SetupCompleted?.Invoke(this, EventArgs.Empty);
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Setup failed.");
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
                // Only now — main database AND first administrator exist — create the offline
                // database and copy the administrator into it. If this fails the administrator
                // is still valid: the offline copy is rebuilt on the first successful login.
                try
                {
                    await _adminBootstrap.SeedOfflineCacheAsync();
                    AdminStatusMessage = "✅ Administrator created and offline database initialized.";
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Setup: administrator created but the offline database could not be initialized.");
                    AdminStatusMessage = $"⚠️ Administrator created, but the offline database could not be initialized: {ex.Message}";
                }

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
