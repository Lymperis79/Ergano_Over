using System;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using ErganiManager.Licensing;
using SPharmacyLog.UI.Services;

namespace SPharmacyLog.UI.Views;

public partial class LicenseActivationWindow : Window
{
    private readonly bool startup;
    public LicenseActivationWindow() : this(true) { }

    public LicenseActivationWindow(bool startup)
    {
        this.startup = startup;
        InitializeComponent();
        PcCodeBox.Text = PharmacyLicenseManager.PcCode;
        ExitButton.Content = startup ? "Exit" : "Close";
        if (!startup && PharmacyLicenseManager.Result.Payload is { } payload)
        {
            InfoBorder.IsVisible = true;
            var validity = payload.IsPermanent ? "Permanent" : $"Valid until {payload.ExpiresDate:dd/MM/yyyy}";
            var modules = payload.Modules.Contains(LicenseModules.All)
                ? "All modules"
                : string.Join(", ", payload.Modules);
            InfoText.Text = $"Licensee: {payload.Licensee}\n{validity}\nModules: {modules}";
        }
        else
        {
            StatusText.Text = PharmacyLicenseManager.Result.Check switch
            {
                LicenseCheck.Missing => "A licence is required to continue.",
                LicenseCheck.WrongMachine => "This licence belongs to a different PC.",
                LicenseCheck.Expired => "The licence has expired.",
                LicenseCheck.BadSignature => "The licence signature is invalid.",
                LicenseCheck.InvalidFormat => "The licence format is invalid.",
                LicenseCheck.ModuleNotLicensed => "This licence does not include the SPharmacyLog module.",
                _ => "Please activate this application."
            };
        }
    }

    private void InitializeComponent() => Avalonia.Markup.Xaml.AvaloniaXamlLoader.Load(this);

    private async void OnCopyClick(object? sender, RoutedEventArgs e)
    {
        try { if (Clipboard is not null) await Clipboard.SetTextAsync(PharmacyLicenseManager.PcCode); }
        catch { StatusText.Text = "Select and copy the PC code manually."; }
    }

    private async void OnLoadClick(object? sender, RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Load licence file",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("Licence files") { Patterns = new[] { "*.lic", "*.key", "*.txt" } },
                FilePickerFileTypes.All
            }
        });
        var path = files.FirstOrDefault()?.TryGetLocalPath();
        if (string.IsNullOrWhiteSpace(path)) return;
        try { KeyBox.Text = await File.ReadAllTextAsync(path); }
        catch (Exception ex) { StatusText.Text = ex.Message; }
    }

    private void OnActivateClick(object? sender, RoutedEventArgs e)
    {
        var result = PharmacyLicenseManager.Activate(KeyBox.Text);
        if (!result.IsValid)
        {
            StatusText.Text = result.Check switch
            {
                LicenseCheck.WrongMachine => "This licence belongs to a different PC.",
                LicenseCheck.Expired => "The licence has expired.",
                LicenseCheck.BadSignature => "The licence signature is invalid.",
                LicenseCheck.InvalidFormat => "The licence format is invalid.",
                LicenseCheck.ModuleNotLicensed => "The licence does not include SPharmacyLog.",
                _ => $"Activation failed: {result.Check}"
            };
            return;
        }
        StatusText.Text = "Licence activated successfully.";
        if (startup) Close();
    }

    private void OnExitClick(object? sender, RoutedEventArgs e) => Close();
}