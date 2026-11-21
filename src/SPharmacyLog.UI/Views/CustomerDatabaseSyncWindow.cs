using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Microsoft.EntityFrameworkCore;
using SPharmacyLog.Data;
using SPharmacyLog.UI.Services;

namespace SPharmacyLog.UI.Views;

public sealed class CustomerDatabaseSyncWindow : Window
{
    private readonly TextBox server = new() { Watermark = "SQL Server / instance" };
    private readonly TextBox database = new() { Watermark = "Database name" };
    private readonly CheckBox integrated = new() { Content = "Windows authentication", IsChecked = true };
    private readonly TextBox username = new() { Watermark = "SQL username" };
    private readonly TextBox password = new() { Watermark = "SQL password", PasswordChar = '●' };
    private readonly ComboBox table = new() { MinWidth = 300 };
    private readonly ComboBox filterColumn = new() { MinWidth = 190 };
    private readonly TextBox filterValue = new() { Text = "ΠΕΛ", Width = 150 };
    private readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap };
    private readonly StackPanel mappingsPanel = new() { Spacing = 7 };
    private readonly Dictionary<string, ComboBox> mappingBoxes = new(StringComparer.OrdinalIgnoreCase);
    private IReadOnlyList<string> availableColumns = Array.Empty<string>();

    private static readonly (string Target, string Label, string SuggestedSource)[] MappingDefinitions =
    {
        ("CustomerNumber", "Customer number (match key)", "PEL_PRO_CODE"),
        ("FullName", "Full name", "EPONYM"),
        ("FirstName", "First name", ""),
        ("Surname", "Surname", ""),
        ("Address", "Address", "ODOS"),
        ("City", "City", "POLH"),
        ("Postcode", "Postcode", "TAX_KOD"),
        ("Telephone", "Telephone", "THLEF"),
        ("Mobile", "Mobile", ""),
        ("Phone", "Phone (legacy field)", ""),
        ("Email", "Email", ""),
        ("Amka", "AMKA", "PEL_PRO_AMKA"),
        ("Insurance", "Insurance code", "PEL_PRO_CODE_AT")
    };

    public CustomerDatabaseSyncWindow()
    {
        Title = "Database synchronization — Customers";
        Width = 850;
        Height = 850;
        MinWidth = 760;
        MinHeight = 650;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = new SolidColorBrush(Color.Parse("#17212D"));

        var root = new StackPanel { Margin = new Thickness(22), Spacing = 12 };
        root.Children.Add(new TextBlock { Text = "Import customers from an external SQL Server", FontSize = 22, FontWeight = FontWeight.Bold });
        root.Children.Add(new TextBlock
        {
            Text = "One-way import into S Pharmacy Log. The source database is only read; customer history and related records are never imported or deleted.",
            TextWrapping = TextWrapping.Wrap,
            Foreground = Brushes.LightGray
        });

        var connectionPanel = new StackPanel { Spacing = 8 };
        connectionPanel.Children.Add(new TextBlock { Text = "SQL SERVER CONNECTION", FontWeight = FontWeight.SemiBold });
        connectionPanel.Children.Add(Row("Server / instance", server, "Database", database));
        connectionPanel.Children.Add(integrated);
        connectionPanel.Children.Add(Row("Username", username, "Password", password));
        var testButton = new Button { Content = "Test connection", MinWidth = 130 };
        var loadTablesButton = new Button { Content = "Load tables", MinWidth = 110 };
        var saveButton = new Button { Content = "Save connection settings", MinWidth = 170 };
        var connectionButtons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { testButton, loadTablesButton, saveButton } };
        connectionPanel.Children.Add(connectionButtons);
        root.Children.Add(Card(connectionPanel));

        var sourcePanel = new StackPanel { Spacing = 8 };
        sourcePanel.Children.Add(new TextBlock { Text = "SOURCE TABLE AND FILTER", FontWeight = FontWeight.SemiBold });
        sourcePanel.Children.Add(Row("Source table", table, "Filter column", filterColumn));
        sourcePanel.Children.Add(Row("Filter value", filterValue));
        sourcePanel.Children.Add(new TextBlock { Text = "Default filter: KPP_KATHGOR = ΠΕΛ. Leave the value empty to import all rows.", Foreground = Brushes.LightGray, TextWrapping = TextWrapping.Wrap });
        root.Children.Add(Card(sourcePanel));

        var mappingScroll = new ScrollViewer { Content = mappingsPanel, MaxHeight = 330, VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto };
        var mappingPanel = new StackPanel { Spacing = 8, Children =
        {
            new TextBlock { Text = "FIELD MAPPING", FontWeight = FontWeight.SemiBold },
            new TextBlock { Text = "Map each S Pharmacy Log customer field to a column in the source table. Customer number is required for matching.", Foreground = Brushes.LightGray, TextWrapping = TextWrapping.Wrap },
            mappingScroll
        }};
        root.Children.Add(Card(mappingPanel));

        var bottom = new StackPanel { Spacing = 8 };
        var previewButton = new Button { Content = "Preview matching source rows", MinWidth = 210 };
        var importButton = new Button { Content = "Import customers", MinWidth = 150 };
        bottom.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { previewButton, importButton } });
        bottom.Children.Add(status);
        root.Children.Add(bottom);

        Content = new ScrollViewer { Content = root, VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto };

        integrated.IsCheckedChanged += (_, _) => UpdateAuthenticationFields();
        testButton.Click += async (_, _) => await RunAsync(async settings =>
        {
            await CustomerDatabaseSyncService.TestConnectionAsync(settings);
            status.Text = "Connection successful.";
        });
        loadTablesButton.Click += async (_, _) => await RunAsync(async settings =>
        {
            await CustomerDatabaseSyncService.TestConnectionAsync(settings);
            var tables = await CustomerDatabaseSyncService.GetTablesAsync(settings);
            table.ItemsSource = tables;
            if (!string.IsNullOrWhiteSpace(settings.Table) && tables.Contains(settings.Table))
                table.SelectedItem = settings.Table;
            else if (tables.Count > 0)
                table.SelectedIndex = 0;
            await LoadColumnsAsync(settings.Table);
            status.Text = $"Loaded {tables.Count} table(s). Select the source table and review the field mappings.";
        });
        table.SelectionChanged += async (_, _) =>
        {
            if (table.SelectedItem is string selected)
                await RunAsync(async settings => { await LoadColumnsAsync(selected); status.Text = $"Loaded {availableColumns.Count} source column(s)."; });
        };
        saveButton.Click += async (_, _) => await RunAsync(settings =>
        {
            CustomerDatabaseSyncService.SaveSettings(settings);
            status.Text = "Connection and mapping settings saved securely for this Windows user.";
            return Task.CompletedTask;
        });
        previewButton.Click += async (_, _) => await RunAsync(async settings =>
        {
            var count = await CustomerDatabaseSyncService.PreviewCountAsync(settings);
            status.Text = $"Preview: {count:N0} source customer row(s) match the selected filter. Inactive matching customers will be skipped without any changes.";
        });
        importButton.Click += async (_, _) => await RunAsync(async settings =>
        {
            var confirm = await new Window
            {
                Title = "Confirm customer import",
                Width = 480,
                Height = 210,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Content = new StackPanel
                {
                    Margin = new Thickness(20),
                    Spacing = 14,
                    Children =
                    {
                        new TextBlock { Text = "Import the selected customers into S Pharmacy Log? Existing active customers will be updated. Existing inactive customers will be left completely unchanged.", TextWrapping = TextWrapping.Wrap },
                        new StackPanel
                        {
                            Orientation = Orientation.Horizontal,
                            HorizontalAlignment = HorizontalAlignment.Right,
                            Spacing = 8,
                            Children =
                            {
                                new Button { Content = "Cancel", MinWidth = 90, IsCancel = true },
                                new Button { Content = "Import", MinWidth = 90, IsDefault = true, Name = "ConfirmImport" }
                            }
                        }
                    }
                }
            }.ShowDialog<bool>(this);
            if (!confirm) return;
            await using var db = new PharmacyDbContext(PharmacyDb.Options());
            var result = await CustomerDatabaseSyncService.ImportAsync(settings, db);
            status.Text = $"Import completed. Added: {result.Added}. Updated: {result.Updated}. Inactive skipped (unchanged): {result.InactiveSkipped}. Invalid rows skipped: {result.InvalidSkipped}.";
        });

        LoadSavedSettings();
        UpdateAuthenticationFields();
        BuildMappingRows();
        Opened += (_, _) => { };
    }

    private static Border Card(Control content) => new()
    {
        Background = new SolidColorBrush(Color.Parse("#202B38")),
        BorderBrush = new SolidColorBrush(Color.Parse("#354456")),
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(9),
        Padding = new Thickness(14),
        Child = content
    };

    private static StackPanel Row(string firstLabel, Control first, string? secondLabel = null, Control? second = null)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        row.Children.Add(new TextBlock { Text = firstLabel, Width = 120, VerticalAlignment = VerticalAlignment.Center });
        first.Width = first.Width > 0 ? first.Width : 190;
        row.Children.Add(first);
        if (second is not null)
        {
            row.Children.Add(new TextBlock { Text = secondLabel, Width = 95, VerticalAlignment = VerticalAlignment.Center });
            second.Width = second.Width > 0 ? second.Width : 190;
            row.Children.Add(second);
        }
        return row;
    }

    private void BuildMappingRows()
    {
        mappingsPanel.Children.Clear();
        mappingBoxes.Clear();
        foreach (var definition in MappingDefinitions)
        {
            var combo = new ComboBox { MinWidth = 280, Width = 300, PlaceholderText = "— Do not import —" };
            combo.ItemsSource = new[] { "— Do not import —" };
            combo.SelectedIndex = 0;
            mappingBoxes[definition.Target] = combo;
            mappingsPanel.Children.Add(Row(definition.Label, combo));
        }
        UpdateMappingChoices();
    }

    private void UpdateMappingChoices()
    {
        foreach (var definition in MappingDefinitions)
        {
            var combo = mappingBoxes[definition.Target];
            var current = combo.SelectedItem?.ToString();
            var options = new[] { "— Do not import —" }.Concat(availableColumns).ToArray();
            combo.ItemsSource = options;
            var desired = !string.IsNullOrWhiteSpace(current) && current != "— Do not import —"
                ? current
                : definition.SuggestedSource;
            combo.SelectedItem = options.FirstOrDefault(option => string.Equals(option, desired, StringComparison.OrdinalIgnoreCase))
                                 ?? options[0];
        }
        filterColumn.ItemsSource = availableColumns;
        if (availableColumns.Contains("KPP_KATHGOR", StringComparer.OrdinalIgnoreCase))
            filterColumn.SelectedItem = availableColumns.First(c => c.Equals("KPP_KATHGOR", StringComparison.OrdinalIgnoreCase));
        else if (availableColumns.Count > 0 && filterColumn.SelectedItem is null)
            filterColumn.SelectedIndex = 0;
    }

    private async Task LoadColumnsAsync(string selectedTable)
    {
        if (string.IsNullOrWhiteSpace(selectedTable)) return;
        var settings = ReadSettings();
        availableColumns = await CustomerDatabaseSyncService.GetColumnsAsync(settings, selectedTable);
        UpdateMappingChoices();
    }

    private void LoadSavedSettings()
    {
        var settings = CustomerDatabaseSyncService.LoadSettings();
        server.Text = settings.Server;
        database.Text = settings.Database;
        integrated.IsChecked = settings.IntegratedSecurity;
        username.Text = settings.Username;
        password.Text = settings.Password;
        filterValue.Text = settings.FilterValue;
        if (settings.FieldMappings is not null)
            foreach (var pair in settings.FieldMappings)
                if (mappingBoxes.TryGetValue(pair.Key, out var combo))
                    combo.SelectedItem = pair.Value;
        if (!string.IsNullOrWhiteSpace(settings.Table))
            table.SelectedItem = settings.Table;
        if (!string.IsNullOrWhiteSpace(settings.FilterColumn))
            filterColumn.SelectedItem = settings.FilterColumn;
    }

    private void UpdateAuthenticationFields()
    {
        var sqlAuthentication = integrated.IsChecked != true;
        username.IsEnabled = sqlAuthentication;
        password.IsEnabled = sqlAuthentication;
    }

    private CustomerDatabaseSyncSettings ReadSettings()
    {
        var settings = new CustomerDatabaseSyncSettings
        {
            Server = server.Text?.Trim() ?? string.Empty,
            Database = database.Text?.Trim() ?? string.Empty,
            IntegratedSecurity = integrated.IsChecked == true,
            Username = username.Text ?? string.Empty,
            Password = password.Text ?? string.Empty,
            Table = table.SelectedItem?.ToString() ?? string.Empty,
            FilterColumn = filterColumn.SelectedItem?.ToString() ?? "KPP_KATHGOR",
            FilterValue = filterValue.Text ?? string.Empty,
            FieldMappings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        };
        foreach (var pair in mappingBoxes)
        {
            var value = pair.Value.SelectedItem?.ToString();
            if (!string.IsNullOrWhiteSpace(value) && value != "— Do not import —")
                settings.FieldMappings[pair.Key] = value;
        }
        return settings;
    }

    private async Task RunAsync(Func<CustomerDatabaseSyncSettings, Task> action)
    {
        try
        {
            status.Text = "Working…";
            IsEnabled = false;
            await action(ReadSettings());
        }
        catch (Exception ex)
        {
            status.Text = "Operation failed: " + ex.Message;
        }
        finally { IsEnabled = true; }
    }
}
