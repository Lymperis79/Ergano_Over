using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;\nusing Avalonia.Media;
using ErganiManager.UI.ViewModels;

namespace ErganiManager.UI.Views;

public partial class AdministrationView : UserControl
{
    public AdministrationView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (DataContext is AdministrationViewModel vm)
            {
                vm.DatabaseDeleted -= OnDatabaseDeleted;
                vm.DatabaseDeleted += OnDatabaseDeleted;
            }
        };
    }

    private async void OnBackupClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not AdministrationViewModel vm) return;
        var top = TopLevel.GetTopLevel(this);
        if (top == null) return;

        var file = await top.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = Tr.Loc["Vm.Backup database"],
            SuggestedFileName = vm.SuggestedBackupName,
            ShowOverwritePrompt = true
        });

        var path = file?.TryGetLocalPath();
        if (!string.IsNullOrEmpty(path))
            await vm.BackupToAsync(path);
    }

    private async void OnExportSettingsClick(object? sender, RoutedEventArgs e)
    {
        var top = TopLevel.GetTopLevel(this);
        if (top?.StorageProvider is not { } storage) return;
        var file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export Ergani Manager installation settings",
            SuggestedFileName = "ErganiManager-Settings.ergcfg",
            FileTypeChoices = new[] { new FilePickerFileType("Encrypted Ergani settings") { Patterns = new[] { "*.ergcfg" } } }
        });
        var path = file?.TryGetLocalPath();
        if (string.IsNullOrWhiteSpace(path)) return;
        var password = await PasswordPromptAsync("Create package password", "Enter a password (minimum 8 characters):");
        if (string.IsNullOrWhiteSpace(password)) return;
        var confirm = await PasswordPromptAsync("Confirm package password", "Re-enter the password:");
        if (password != confirm) { SettingsTransferStatus.Text = "Passwords do not match. Export cancelled."; return; }
        try { await ErganiManager.UI.Services.InstallationSettingsTransfer.ExportAsync(path, password); SettingsTransferStatus.Text = "Encrypted configuration package exported."; }
        catch (Exception ex) { SettingsTransferStatus.Text = "Export failed: " + ex.Message; }
    }

    private async void OnImportSettingsClick(object? sender, RoutedEventArgs e)
    {
        var top = TopLevel.GetTopLevel(this);
        if (top?.StorageProvider is not { } storage) return;
        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Import Ergani Manager installation settings",
            AllowMultiple = false,
            FileTypeFilter = new[] { new FilePickerFileType("Encrypted Ergani settings") { Patterns = new[] { "*.ergcfg" } } }
        });
        var path = files.FirstOrDefault()?.TryGetLocalPath();
        if (string.IsNullOrWhiteSpace(path)) return;
        if (!await ConfirmImportAsync()) return;\n        var password = await PasswordPromptAsync("Import settings", "Enter the package password:");
        if (string.IsNullOrWhiteSpace(password)) return;
        try
        {
            var imported = await Services.InstallationSettingsTransfer.ImportAsync(path, password);
            SettingsTransferStatus.Text = "Imported: " + string.Join(", ", imported) + ". Restart Ergani Manager to apply the configuration.";
        }
        catch (Exception ex) { SettingsTransferStatus.Text = "Import failed: " + ex.Message; }
    }

    private async Task<bool> ConfirmImportAsync()
    {
        var owner = TopLevel.GetTopLevel(this) as Window;
        if (owner == null) return false;
        var dialog = new Window { Title = "Confirm settings import", Width = 470, Height = 230, WindowStartupLocation = WindowStartupLocation.CenterOwner, CanResize = false };
        var apply = new Button { Content = "Import and replace local settings", IsDefault = true };
        var cancel = new Button { Content = "Cancel", IsCancel = true };
        apply.Click += (_, _) => dialog.Close(true);
        cancel.Click += (_, _) => dialog.Close(false);
        dialog.Content = new StackPanel { Margin = new Thickness(20), Spacing = 14, Children = { new TextBlock { Text = "This will replace this PC's local configuration, including database connection and portal settings. Local database files and user accounts are not imported. Continue?", TextWrapping = TextWrapping.Wrap }, new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8, Children = { cancel, apply } } } };
        return await dialog.ShowDialog<bool>(owner);
    }

    private async Task<string?> PasswordPromptAsync(string title, string label)
    {
        var owner = TopLevel.GetTopLevel(this) as Window;
        if (owner == null) return null;
        var input = new TextBox { PasswordChar = '•', MinWidth = 300 };
        var ok = new Button { Content = "Continue", IsDefault = true, MinWidth = 90 };
        var cancel = new Button { Content = "Cancel", IsCancel = true, MinWidth = 90 };
        var dialog = new Window { Title = title, Width = 430, Height = 190, WindowStartupLocation = WindowStartupLocation.CenterOwner, CanResize = false };
        ok.Click += (_, _) => dialog.Close(input.Text);
        cancel.Click += (_, _) => dialog.Close(null);
        dialog.Content = new StackPanel { Margin = new Thickness(20), Spacing = 12, Children = { new TextBlock { Text = label }, input, new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8, Children = { cancel, ok } } } };
        return await dialog.ShowDialog<string?>(owner);
    }

    private void OnDatabaseDeleted(object? sender, EventArgs e)
    {
        // Back to the first-run wizard, which recreates the schema.
        if (Application.Current is App app &&
            Application.Current.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            app.ShowDatabaseSetup(desktop);
    }
}
