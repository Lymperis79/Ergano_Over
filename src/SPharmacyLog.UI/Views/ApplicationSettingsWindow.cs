using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using SPharmacyLog.UI.Services;

namespace SPharmacyLog.UI.Views;

public sealed class ApplicationSettingsWindow : UserControl
{
    public event Action? SettingsSaved;
    private readonly TextBlock saveStatus = new() { Foreground = Brushes.LightGray };
    private readonly TextBlock transferStatus = new() { Foreground = Brushes.LightGray, TextWrapping = TextWrapping.Wrap };
    private readonly CheckBox showOfficialHolidays;\n    private readonly bool allowTransfer;

    public ApplicationSettingsWindow()
    {
        var settings = ApplicationSettingsService.Load();
        Background = new SolidColorBrush(Color.Parse("#202733"));
        showOfficialHolidays = new CheckBox { Content = "Show official Greek public holidays in the calendar", IsChecked = settings.ShowOfficialHolidays };
        var save = new Button { Content = "Save", Classes = { "accent" }, MinWidth = 100 };
        save.Click += (_, _) =>
        {
            ApplicationSettingsService.Save(new ApplicationSettings { ShowOfficialHolidays = showOfficialHolidays.IsChecked == true });
            saveStatus.Text = SPharmacyLog.UI.Localization.Tr.Loc.TranslateText("Settings saved.");
            SettingsSaved?.Invoke();
        };
        var export = new Button { Content = "Export installation settings", MinWidth = 190 };
        var import = new Button { Content = "Import installation settings", MinWidth = 190 };
        export.Click += async (_, _) => await ExportSettingsAsync();
        import.Click += async (_, _) => await ImportSettingsAsync();
        Content = new StackPanel
        {
            Margin = new Thickness(22), Spacing = 16,
            Children =
            {
                new TextBlock { Text = "Application settings", FontSize = 21, FontWeight = FontWeight.SemiBold },
                new TextBlock { Text = "Control which built-in information is displayed in the application calendar.", TextWrapping = TextWrapping.Wrap, Foreground = Brushes.LightGray },
                showOfficialHolidays,
                new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8, Children = { save } },
                new TextBlock { Text = "Transfer installation settings to another client PC", FontSize = 16, FontWeight = FontWeight.SemiBold, IsVisible = allowTransfer },
                new TextBlock { Text = "Encrypted package includes database connection, customer database synchronization, weather, company branding/logo and application preferences. It does not contain database records or user accounts. Restart PharmaPoint after import.", TextWrapping = TextWrapping.Wrap, Foreground = Brushes.LightGray, IsVisible = allowTransfer },
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { export, import }, IsVisible = allowTransfer },
                transferStatus, saveStatus
            }
        };
    }

    private async Task ExportSettingsAsync()
    {
        var top = TopLevel.GetTopLevel(this);
        if (top?.StorageProvider is not { } storage) return;
        var file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export PharmaPoint settings",
            SuggestedFileName = "PharmaPoint-Settings.sphcfg",
            FileTypeChoices = new[] { new FilePickerFileType("Encrypted PharmaPoint settings") { Patterns = new[] { "*.sphcfg" } } }
        });
        var path = file?.TryGetLocalPath();
        if (string.IsNullOrWhiteSpace(path)) return;
        var password = await PasswordPromptAsync("Create package password", "Password (minimum 8 characters):");
        if (string.IsNullOrWhiteSpace(password)) return;
        if (password != await PasswordPromptAsync("Confirm package password", "Re-enter the password:"))
        { transferStatus.Text = "Passwords do not match. Export cancelled."; return; }
        try { await InstallationSettingsTransfer.ExportAsync(path, password); transferStatus.Text = "Encrypted configuration package exported successfully."; }
        catch (Exception ex) { transferStatus.Text = "Export failed: " + ex.Message; }
    }

    private async Task ImportSettingsAsync()
    {
        var top = TopLevel.GetTopLevel(this);
        if (top?.StorageProvider is not { } storage) return;
        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Import PharmaPoint settings", AllowMultiple = false,
            FileTypeFilter = new[] { new FilePickerFileType("Encrypted PharmaPoint settings") { Patterns = new[] { "*.sphcfg" } } }
        });
        var path = files.FirstOrDefault()?.TryGetLocalPath();
        if (string.IsNullOrWhiteSpace(path) || !await ConfirmImportAsync()) return;
        var password = await PasswordPromptAsync("Import settings", "Enter the package password:");
        if (string.IsNullOrWhiteSpace(password)) return;
        try
        {
            var imported = await InstallationSettingsTransfer.ImportAsync(path, password);
            transferStatus.Text = "Imported: " + string.Join(", ", imported) + ". Restart PharmaPoint to apply all settings.";
        }
        catch (Exception ex) { transferStatus.Text = "Import failed: " + ex.Message; }
    }

    private async Task<bool> ConfirmImportAsync()
    {
        var owner = TopLevel.GetTopLevel(this) as Window;
        if (owner == null) return false;
        var dialog = new Window { Title = "Confirm settings import", Width = 470, Height = 230, WindowStartupLocation = WindowStartupLocation.CenterOwner, CanResize = false };
        var apply = new Button { Content = "Import and replace local settings", IsDefault = true };
        var cancel = new Button { Content = "Cancel", IsCancel = true };
        apply.Click += (_, _) => dialog.Close(true); cancel.Click += (_, _) => dialog.Close(false);
        dialog.Content = new StackPanel { Margin = new Thickness(20), Spacing = 14, Children = { new TextBlock { Text = "This will replace this PC's local configuration, including database connection and customer synchronization settings. Database records and user accounts are not imported. Continue?", TextWrapping = TextWrapping.Wrap }, new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8, Children = { cancel, apply } } } };
        return await dialog.ShowDialog<bool>(owner);
    }

    private async Task<string?> PasswordPromptAsync(string title, string label)
    {
        var owner = TopLevel.GetTopLevel(this) as Window;
        if (owner == null) return null;
        var box = new TextBox { PasswordChar = '•', MinWidth = 300 };
        var ok = new Button { Content = "Continue", IsDefault = true, MinWidth = 90 };
        var cancel = new Button { Content = "Cancel", IsCancel = true, MinWidth = 90 };
        var dialog = new Window { Title = title, Width = 430, Height = 190, WindowStartupLocation = WindowStartupLocation.CenterOwner, CanResize = false };
        ok.Click += (_, _) => dialog.Close(box.Text); cancel.Click += (_, _) => dialog.Close(null);
        dialog.Content = new StackPanel { Margin = new Thickness(20), Spacing = 12, Children = { new TextBlock { Text = label }, box, new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8, Children = { cancel, ok } } } };
        return await dialog.ShowDialog<string?>(owner);
    }
}
