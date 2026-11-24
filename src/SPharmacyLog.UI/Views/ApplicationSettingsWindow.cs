using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;\nusing Avalonia.Platform.Storage;\nusing Avalonia.Controls.Primitives;
using SPharmacyLog.UI.Services;

namespace SPharmacyLog.UI.Views;

public sealed class ApplicationSettingsWindow : UserControl
{
    public event Action? SettingsSaved;
    private readonly TextBlock saveStatus = new() { Foreground = Brushes.LightGray };
    private readonly CheckBox showOfficialHolidays;\n    private readonly TextBlock transferStatus = new() { Foreground = Brushes.LightGray, TextWrapping = TextWrapping.Wrap };

    public ApplicationSettingsWindow()
    {
        var settings = ApplicationSettingsService.Load();
        Background = new SolidColorBrush(Color.Parse("#202733"));

        showOfficialHolidays = new CheckBox
        {
            Content = "Show official Greek public holidays in the calendar",
            IsChecked = settings.ShowOfficialHolidays
        };

        var save = new Button { Content = "Save", Classes = { "accent" }, MinWidth = 100 };
        save.Click += (_, _) =>
        {
            ApplicationSettingsService.Save(new ApplicationSettings
            {
                ShowOfficialHolidays = showOfficialHolidays.IsChecked == true
            });
            saveStatus.Text = SPharmacyLog.UI.Localization.Tr.Loc.TranslateText("Settings saved.");
            SettingsSaved?.Invoke();
        };

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
            Children = { save }
        };
        Content = new StackPanel
        {
            Margin = new Thickness(22),
            Spacing = 16,
            Children =
            {
                new TextBlock { Text = "Application settings", FontSize = 21, FontWeight = FontWeight.SemiBold },
                new TextBlock
                {
                    Text = "Control which built-in information is displayed in the application calendar.",
                    TextWrapping = TextWrapping.Wrap,
                    Foreground = Brushes.LightGray
                },
                showOfficialHolidays,
                buttons,
                saveStatus
            }
        };
    }
}
