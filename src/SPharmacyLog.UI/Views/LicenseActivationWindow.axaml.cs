using System;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using SPharmacyLog.UI.Localization;
using ErganiManager.Licensing;
using SPharmacyLog.UI.Services;

namespace SPharmacyLog.UI.Views;

public partial class LicenseActivationWindow : Window
{
    private readonly bool startup;
    private bool isApplyingTranslations;
    private ComboBox languageBox = null!;

    public LicenseActivationWindow()
        : this(true)
    {
    }

    public LicenseActivationWindow(bool startup)
    {
        this.startup = startup;

        InitializeComponent();
        languageBox = this.FindControl<ComboBox>("LanguageBox")!;
        Tr.Loc.LanguageChanged += (_, _) => ApplyTranslations();

        PcCodeBox.Text = PharmacyLicenseManager.PcCode;
        ExitButton.Content = startup ? "Exit" : "Close";

        if (!startup && PharmacyLicenseManager.Result.Payload is { } payload)
        {
            InfoBorder.IsVisible = true;

            var validity = payload.IsPermanent
                ? "Permanent"
                : $"Valid until {payload.ExpiresDate:dd/MM/yyyy}";

            var modules = payload.Modules.Contains(LicenseModules.All)
                ? "All modules"
                : string.Join(", ", payload.Modules);

            InfoText.Text =
                $"Licensee: {payload.Licensee}\n{validity}\nModules: {modules}";
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
                LicenseCheck.ModuleNotLicensed =>
                    "This licence does not include the SPharmacyLog module.",
                _ => "Please activate this application."
            };
        }
        ApplyTranslations();
    }

    private void ApplyTranslations()
    {
        isApplyingTranslations = true;
        Title = Tr.Loc.TranslateText("S Pharmacy Log — Licence activation");
        languageBox.SelectedIndex = Tr.Loc.CurrentLanguage == AppLanguage.Greek ? 1 : 0;
        foreach (var control in this.GetVisualDescendants().OfType<Control>())
        {
            switch (control)
            {
                case TextBlock textBlock when !string.IsNullOrEmpty(textBlock.Text):
                    textBlock.Text = Tr.Loc.TranslateText(textBlock.Text);
                    break;
                case Button button when button.Content is string buttonText:
                    button.Content = Tr.Loc.TranslateText(buttonText);
                    break;
            }
        }
        isApplyingTranslations = false;
    }

    private void OnLanguageChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (isApplyingTranslations) return;
        if (languageBox?.SelectedIndex == 1) Tr.Loc.SetLanguage(AppLanguage.Greek);
        else if (languageBox?.SelectedIndex == 0) Tr.Loc.SetLanguage(AppLanguage.English);
    }

    private void OnCopyClick(object? sender, RoutedEventArgs e)
    {
        // Avalonia 12's clipboard API is not available through this window's
        // IClipboard interface in the current target. Select the value so the
        // user can copy it with Ctrl+C without relying on a platform clipboard API.
        PcCodeBox.Focus();
        PcCodeBox.SelectAll();
        StatusText.Text = "PC code selected. Press Ctrl+C to copy it.";
        ApplyTranslations();
    }

    private async void OnLoadClick(object? sender, RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(
            new FilePickerOpenOptions
            {
                Title = "Load licence file",
                AllowMultiple = false,
                FileTypeFilter =
                [
                    new FilePickerFileType("Licence files")
                    {
                        Patterns = ["*.lic", "*.key", "*.txt"]
                    },
                    FilePickerFileTypes.All
                ]
            });

        var path = files.FirstOrDefault()?.TryGetLocalPath();

        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        try
        {
            KeyBox.Text = await File.ReadAllTextAsync(path);
        }
        catch (Exception ex)
        {
            StatusText.Text = ex.Message;
            ApplyTranslations();
        }
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
                LicenseCheck.ModuleNotLicensed =>
                    "The licence does not include SPharmacyLog.",
                _ => $"Activation failed: {result.Check}"
            };
            ApplyTranslations();
            return;
        }

        StatusText.Text = "Licence activated successfully.";
        ApplyTranslations();

        if (startup)
        {
            Close();
        }
    }

    private void OnExitClick(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}
