using System;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using ErganiManager.Licensing;
using ErganiManager.UI.Localization;
using ErganiManager.UI.Services;

namespace ErganiManager.UI.Views;

/// <summary>
/// Asks for the license key. At startup (a PC that is not activated yet) it blocks the program until a valid key is
/// entered; opened from the header it shows the current license and lets the user enter a renewed key.
/// </summary>
public partial class ActivationWindow : Window
{
    private readonly bool _startup;

    public ActivationWindow() : this(true) { }

    public ActivationWindow(bool startup)
    {
        _startup = startup;
        InitializeComponent();

        var loc = Tr.Loc;
        Title = "Ergani Manager — " + loc["LicenseTitle"];
        Field<TextBlock>("TitleText").Text = startup ? loc["LicenseTitle"] : loc["LicenseInfoTitle"];
        Field<TextBlock>("IntroText").Text = startup ? loc["LicenseIntro"] : loc["LicenseChangeKey"];
        Field<TextBlock>("PcCodeLabel").Text = loc["LicensePcCode"];
        Field<TextBox>("PcCodeBox").Text = LicenseManager.MachineCodeText;
        Field<Button>("CopyButton").Content = loc["LicenseCopy"];
        Field<TextBlock>("KeyLabel").Text = loc["LicenseKeyLabel"];
        Field<TextBox>("KeyBox").PlaceholderText = loc["LicenseKeyHint"];
        Field<Button>("LoadButton").Content = loc["LicenseLoadFile"];
        Field<Button>("ActivateButton").Content = loc["LicenseActivate"];
        Field<Button>("ExitButton").Content = startup ? loc["LicenseExit"] : loc["LicenseClose"];

        WindowStartupLocation = startup ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner;
        CanResize = false;

        ShowCurrentLicense();

        if (startup)
        {
            var check = LicenseManager.Result.Check;
            Field<TextBlock>("StatusText").Text = check == LicenseCheck.Missing ? loc["LicenseMissing"] : Describe(check);
        }
    }

    private T Field<T>(string name) where T : Control => this.FindControl<T>(name)!;

    /// <summary>Licensee, validity and modules of the license that is active now.</summary>
    private void ShowCurrentLicense()
    {
        var payload = LicenseState.Current;
        if (payload == null || _startup) return;

        var loc = Tr.Loc;
        var today = DateOnly.FromDateTime(DateTime.Now);
        var valid = payload.IsPermanent ? loc["LicensePermanent"] : $"{loc["LicenseValidUntil"]} {payload.ExpiresDate:dd/MM/yyyy}";
        var modules = payload.Modules.Contains(LicenseModules.All)
            ? loc["LicenseAllModules"]
            : string.Join(", ", payload.Modules.Select(m => loc["Vm.Module " + m]));

        var kind = payload.IsMachineBound ? loc["LicenseTypeSingle"]
            : payload.Seats == 1 ? loc["LicenseTypeFloating"]
            : payload.Seats > 1 ? string.Format(loc["LicenseTypeMulti"], payload.Seats)
            : loc["LicenseTypeUnlimited"];

        var text = $"{loc["LicenseLicensee"]}: {payload.Licensee}\n{loc["LicenseTypeLabel"]}: {kind}\n{valid}\n{loc["LicenseModulesLabel"]}: {modules}";
        if (payload.DaysLeft(today) is { } left and <= 30)
            text += "\n" + string.Format(loc["Vm.License expires in {0} day(s)."], Math.Max(0, left));

        Field<TextBlock>("InfoText").Text = text;
        Field<Border>("InfoBorder").IsVisible = true;
    }

    private static string Describe(LicenseCheck check) => check switch
    {
        LicenseCheck.InvalidFormat  => Tr.Loc["LicenseInvalidFormat"],
        LicenseCheck.BadSignature   => Tr.Loc["LicenseBadSignature"],
        LicenseCheck.WrongMachine   => Tr.Loc["LicenseWrongMachine"],
        LicenseCheck.Expired        => Tr.Loc["LicenseExpired"],
        LicenseCheck.ClockTampered  => Tr.Loc["LicenseClock"],
        LicenseCheck.NotConfigured  => Tr.Loc["LicenseNotConfigured"],
        LicenseCheck.Missing        => Tr.Loc["LicenseMissing"],
        _                           => string.Empty
    };

    private async void OnCopyClick(object? sender, RoutedEventArgs e)
    {
        // (The code can also be selected in the box and copied with Ctrl+C.)
        try { if (Clipboard != null) await Clipboard.SetTextAsync(LicenseManager.MachineCodeText); }
        catch { /* the code stays selectable in the box */ }
    }

    private async void OnLoadClick(object? sender, RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = Tr.Loc["LicenseLoadFile"],
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("License") { Patterns = new[] { "*.lic", "*.key", "*.txt" } },
                FilePickerFileTypes.All
            }
        });

        var path = files.FirstOrDefault()?.TryGetLocalPath();
        if (string.IsNullOrEmpty(path)) return;

        try { Field<TextBox>("KeyBox").Text = await File.ReadAllTextAsync(path); }
        catch (Exception ex) { Field<TextBlock>("StatusText").Text = "❌ " + ex.Message; }
    }

    private void OnActivateClick(object? sender, RoutedEventArgs e)
    {
        var result = LicenseManager.Activate(Field<TextBox>("KeyBox").Text);
        var status = Field<TextBlock>("StatusText");

        if (!result.IsValid)
        {
            status.Text = Describe(result.Check);
            return;
        }

        if (_startup)
        {
            Close(true);
            return;
        }

        status.Text = Tr.Loc["LicenseActivatedRestart"];
        ShowCurrentLicense();
    }

    private void OnExitClick(object? sender, RoutedEventArgs e) => Close(false);
}
