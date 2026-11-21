using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Microsoft.Data.SqlClient;
using MySqlConnector;
using SPharmacyLog.Core;
using SPharmacyLog.Data;
using SPharmacyLog.Data.Entities;

namespace SPharmacyLog.UI.Views;

/// <summary>
/// First-run database wizard, following the same safe setup sequence as ErganiManager:
/// connect to server, create only the missing database, apply migrations, then bootstrap
/// the first administrator. Existing databases are never dropped or recreated.
/// </summary>
public sealed class DatabaseSetupWindow : Window
{
    private readonly ComboBox provider = new() { ItemsSource = Enum.GetValues<DatabaseProvider>(), SelectedIndex = 0 };
    private readonly TextBox sqlitePath = new() { Text = PharmacyDb.DefaultSqlitePath };
    private readonly TextBox sqlHost = new() { Text = @"localhost\\SQLEXPRESS" };
    private readonly TextBox sqlDatabase = new() { Text = "SPharmacyLog" };
    private readonly CheckBox windowsAuth = new() { Content = "Use Windows authentication", IsChecked = true };
    private readonly TextBox sqlUsername = new();
    private readonly TextBox sqlPassword = new() { PasswordChar = '•' };
    private readonly TextBox mariaHost = new() { Text = "localhost" };
    private readonly TextBox mariaDatabase = new() { Text = "spharmacylog" };
    private readonly TextBox mariaUsername = new() { Text = "root" };
    private readonly TextBox mariaPassword = new() { PasswordChar = '•' };
    private readonly TextBox adminUsername = new();
    private readonly TextBox adminPassword = new() { PasswordChar = '•' };
    private readonly TextBox adminConfirm = new() { PasswordChar = '•' };
    private readonly TextBlock status = new() { TextWrapping = Avalonia.Media.TextWrapping.Wrap };
    private readonly StackPanel databaseFields = new() { Spacing = 10 };
    private readonly StackPanel adminFields = new() { Spacing = 10 };
    private readonly Button testButton = new() { Content = "Test connection" };
    private readonly Button continueButton = new() { Content = "Create / connect and continue" };
    private readonly Button createAdminButton = new() { Content = "Create administrator" };

    public DatabaseSetupWindow(bool resumeAtAdmin = false)
    {
        Title = "S Pharmacy Log — Database setup";
        Width = 590;
        SizeToContent = SizeToContent.Height;
        MinHeight = 500;
        CanResize = true;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        var existing = PharmacyDb.LoadConfig();
        if (existing is not null) Populate(existing);

        var root = new StackPanel { Margin = new Thickness(28), Spacing = 14 };
        root.Children.Add(new TextBlock { Text = "Database setup", FontSize = 24, FontWeight = Avalonia.Media.FontWeight.Bold });
        root.Children.Add(new TextBlock
        {
            Text = "Choose where S Pharmacy Log stores its data. You can use a local SQLite file or a shared SQL Server / MariaDB database.",
            TextWrapping = Avalonia.Media.TextWrapping.Wrap
        });

        databaseFields.Children.Add(Label("Database provider"));
        databaseFields.Children.Add(provider);
        databaseFields.Children.Add(Label("SQLite database file"));
        databaseFields.Children.Add(sqlitePath);
        databaseFields.Children.Add(Label("SQL Server host / instance"));
        databaseFields.Children.Add(sqlHost);
        databaseFields.Children.Add(Label("SQL Server database"));
        databaseFields.Children.Add(sqlDatabase);
        databaseFields.Children.Add(windowsAuth);
        databaseFields.Children.Add(Label("SQL Server username"));
        databaseFields.Children.Add(sqlUsername);
        databaseFields.Children.Add(Label("SQL Server password"));
        databaseFields.Children.Add(sqlPassword);
        databaseFields.Children.Add(Label("MariaDB server"));
        databaseFields.Children.Add(mariaHost);
        databaseFields.Children.Add(Label("MariaDB database"));
        databaseFields.Children.Add(mariaDatabase);
        databaseFields.Children.Add(Label("MariaDB username"));
        databaseFields.Children.Add(mariaUsername);
        databaseFields.Children.Add(Label("MariaDB password"));
        databaseFields.Children.Add(mariaPassword);
        root.Children.Add(databaseFields);

        adminFields.Children.Add(new TextBlock { Text = "Create the first administrator", FontSize = 20, FontWeight = Avalonia.Media.FontWeight.Bold });
        adminFields.Children.Add(new TextBlock { Text = "The administrator account is created once, after the database schema is ready." });
        adminFields.Children.Add(Label("Administrator username"));
        adminFields.Children.Add(adminUsername);
        adminFields.Children.Add(Label("Password (at least 8 characters)"));
        adminFields.Children.Add(adminPassword);
        adminFields.Children.Add(Label("Confirm password"));
        adminFields.Children.Add(adminConfirm);
        adminFields.IsVisible = false;
        root.Children.Add(adminFields);

        root.Children.Add(status);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 10 };
        actions.Children.Add(testButton);
        actions.Children.Add(continueButton);
        root.Children.Add(actions);
        root.Children.Add(createAdminButton);
        createAdminButton.IsVisible = false;
        Content = new ScrollViewer { Content = root, Padding = new Thickness(0), MaxHeight = 780 };

        provider.SelectionChanged += (_, _) => RefreshProviderFields();
        windowsAuth.IsCheckedChanged += (_, _) => RefreshProviderFields();
        testButton.Click += async (_, _) => await TestConnectionAsync();
        continueButton.Click += async (_, _) => await SaveAndContinueAsync();
        createAdminButton.Click += async (_, _) => await CreateAdminAsync();

        if (resumeAtAdmin)
            ShowAdminStep();
        else
            RefreshProviderFields();
    }

    private static TextBlock Label(string text) =>
        new() { Text = text, FontWeight = Avalonia.Media.FontWeight.SemiBold };

    private void Populate(DbConfig config)
    {
        provider.SelectedItem = config.DatabaseProvider;
        if (config.DatabaseProvider == DatabaseProvider.Sqlite)
            sqlitePath.Text = config.SqlitePath ?? PharmacyDb.DefaultSqlitePath;
        if (config.DatabaseProvider == DatabaseProvider.SqlServer && !string.IsNullOrWhiteSpace(config.ConnectionString))
        {
            var parsed = new SqlConnectionStringBuilder(config.ConnectionString);
            sqlHost.Text = parsed.DataSource;
            sqlDatabase.Text = parsed.InitialCatalog;
            windowsAuth.IsChecked = parsed.IntegratedSecurity;
            sqlUsername.Text = parsed.UserID;
            sqlPassword.Text = parsed.Password;
        }
        if (config.DatabaseProvider == DatabaseProvider.MariaDb && !string.IsNullOrWhiteSpace(config.ConnectionString))
        {
            var parsed = new MySqlConnectionStringBuilder(config.ConnectionString);
            mariaHost.Text = parsed.Server;
            mariaDatabase.Text = parsed.Database;
            mariaUsername.Text = parsed.UserID;
            mariaPassword.Text = parsed.Password;
        }
    }

    private void RefreshProviderFields()
    {
        var selected = provider.SelectedItem is DatabaseProvider value ? value : DatabaseProvider.Sqlite;
        sqlitePath.IsVisible = selected == DatabaseProvider.Sqlite;
        sqlHost.IsVisible = sqlDatabase.IsVisible = selected == DatabaseProvider.SqlServer;
        windowsAuth.IsVisible = selected == DatabaseProvider.SqlServer;
        sqlUsername.IsVisible = sqlPassword.IsVisible = selected == DatabaseProvider.SqlServer && windowsAuth.IsChecked != true;
        mariaHost.IsVisible = mariaDatabase.IsVisible = mariaUsername.IsVisible = mariaPassword.IsVisible = selected == DatabaseProvider.MariaDb;
        databaseFields.Children[2].IsVisible = databaseFields.Children[3].IsVisible = selected == DatabaseProvider.Sqlite;
        for (var i = 4; i <= 12; i++) databaseFields.Children[i].IsVisible = selected == DatabaseProvider.SqlServer;
        databaseFields.Children[9].IsVisible = databaseFields.Children[10].IsVisible = selected == DatabaseProvider.SqlServer && windowsAuth.IsChecked != true;
        databaseFields.Children[11].IsVisible = databaseFields.Children[12].IsVisible = selected == DatabaseProvider.SqlServer && windowsAuth.IsChecked != true;
        for (var i = 13; i <= 20; i++) databaseFields.Children[i].IsVisible = selected == DatabaseProvider.MariaDb;
    }
    private DbConfig BuildConfig()
    {
        var selected = provider.SelectedItem is DatabaseProvider value ? value : DatabaseProvider.Sqlite;
        return selected switch
        {
            DatabaseProvider.Sqlite => new DbConfig
            {
                DatabaseProvider = selected,
                SqlitePath = string.IsNullOrWhiteSpace(sqlitePath.Text) ? PharmacyDb.DefaultSqlitePath : sqlitePath.Text.Trim()
            },
            DatabaseProvider.SqlServer => new DbConfig
            {
                DatabaseProvider = selected,
                ConnectionString = windowsAuth.IsChecked == true
                    ? new SqlConnectionStringBuilder { DataSource = sqlHost.Text?.Trim(), InitialCatalog = sqlDatabase.Text?.Trim(), IntegratedSecurity = true, TrustServerCertificate = true }.ConnectionString
                    : new SqlConnectionStringBuilder { DataSource = sqlHost.Text?.Trim(), InitialCatalog = sqlDatabase.Text?.Trim(), UserID = sqlUsername.Text?.Trim(), Password = sqlPassword.Text, TrustServerCertificate = true }.ConnectionString
            },
            DatabaseProvider.MariaDb => new DbConfig
            {
                DatabaseProvider = selected,
                ConnectionString = new MySqlConnectionStringBuilder
                {
                    Server = mariaHost.Text?.Trim(),
                    Database = mariaDatabase.Text?.Trim(),
                    UserID = mariaUsername.Text?.Trim(),
                    Password = mariaPassword.Text
                }.ConnectionString
            },
            _ => throw new InvalidOperationException("Select a supported database provider.")
        };
    }

    private async Task TestConnectionAsync()
    {
        testButton.IsEnabled = continueButton.IsEnabled = false;
        try
        {
            var config = BuildConfig();
            var result = await DbProviderFactory.TestConnectionAsync(config);
            if (result.Success)
                status.Text = "Connection successful. The database is reachable.";
            else
            {
                var server = await DbProviderFactory.TestServerAsync(config);
                status.Text = server.Success
                    ? "Database server is reachable. The application database does not exist yet; it will be created when you continue."
                    : $"Connection failed: {server.Error ?? result.Error}";
            }
        }
        catch (Exception ex) { status.Text = $"Connection test failed: {ex.Message}"; }
        finally { testButton.IsEnabled = continueButton.IsEnabled = true; }
    }

    private async Task SaveAndContinueAsync()
    {
        testButton.IsEnabled = continueButton.IsEnabled = false;
        try
        {
            var config = BuildConfig();
            status.Text = "Checking the database...";
            var databaseExists = (await DbProviderFactory.TestConnectionAsync(config)).Success;
            if (!databaseExists)
            {
                var server = await DbProviderFactory.TestServerAsync(config);
                if (!server.Success)
                    throw new InvalidOperationException(server.Error ?? "The database server could not be reached.");
                status.Text = "Creating the database if it does not already exist...";
                await DbProviderFactory.EnsureDatabaseExistsAsync(config);
            }

            var connection = await DbProviderFactory.TestConnectionAsync(config);
            if (!connection.Success)
                throw new InvalidOperationException(connection.Error ?? "Cannot connect to the database.");

            status.Text = "Applying database migrations. Existing data will be preserved...";
            await PharmacyDb.ApplyMigrationsAsync(config);

            // Verify essential tables before persisting the configuration, so an incomplete
            // setup never causes the next launch to bypass this wizard.
            await using (var db = new PharmacyDbContext(PharmacyDb.CreateOptions(config)))
            {
                _ = await db.Users.AnyAsync();
                _ = await db.Customers.AnyAsync();
            }

            PharmacyDb.SaveConfig(config);
            if (await PharmacyDb.HasUsersAsync(config))
            {
                status.Text = "Database is ready. Existing user accounts were found.";
                Close();
                return;
            }

            status.Text = "Database is ready. Create the first administrator to finish setup.";
            ShowAdminStep();
        }
        catch (Exception ex) { status.Text = $"Setup could not be completed: {ex.Message}"; }
        finally { testButton.IsEnabled = continueButton.IsEnabled = true; }
    }

    private void ShowAdminStep()
    {
        databaseFields.IsVisible = false;
        testButton.IsVisible = false;
        continueButton.IsVisible = false;
        adminFields.IsVisible = true;
        createAdminButton.IsVisible = true;
        status.Text = "The database is ready. Create the first administrator to finish setup.";
    }

    private async Task CreateAdminAsync()
    {
        createAdminButton.IsEnabled = false;
        try
        {
            var username = adminUsername.Text?.Trim() ?? string.Empty;
            var password = adminPassword.Text ?? string.Empty;
            if (string.IsNullOrWhiteSpace(username))
                throw new InvalidOperationException("Enter an administrator username.");
            if (password.Length < 8)
                throw new InvalidOperationException("The password must contain at least 8 characters.");
            if (password != adminConfirm.Text)
                throw new InvalidOperationException("The passwords do not match.");

            var config = PharmacyDb.LoadConfig()
                ?? throw new InvalidOperationException("Database configuration is missing. Return to database setup.");
            await using var db = new PharmacyDbContext(PharmacyDb.CreateOptions(config));
            if (await db.Users.AnyAsync())
                throw new InvalidOperationException("An account already exists. Close setup and sign in.");
            db.Users.Add(new PharmacyUser
            {
                Username = username,
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(password),
                Role = PharmacyRole.Admin,
                IsActive = true
            });
            await db.SaveChangesAsync();
            status.Text = "Administrator created successfully. You can now sign in.";
            Close();
        }
        catch (Exception ex) { status.Text = ex.Message; }
        finally { createAdminButton.IsEnabled = true; }
    }
}
