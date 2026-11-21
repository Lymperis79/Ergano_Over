using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using SPharmacyLog.UI.Services;

namespace SPharmacyLog.UI.Views;

public sealed class ApplicationSettingsWindow : Window
{
    private readonly CheckBox showOfficialHolidays;

    public ApplicationSettingsWindow()
    {
        var settings = ApplicationSettingsService.Load();
        Title = "Application settings";
        Width = 460;
        Height = 260;
        MinWidth = 420;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = new SolidColorBrush(Color.Parse("#202733"));

        showOfficialHolidays = new CheckBox
        {
            Content = "Show official Greek public holidays in the calendar",
            IsChecked = settings.ShowOfficialHolidays
        };

        var save = new Button { Content = "Save", Classes = { "accent" }, MinWidth = 100 };
        var cancel = new Button { Content = "Cancel", MinWidth = 100 };
        save.Click += (_, _) =>
        {
            ApplicationSettingsService.Save(new ApplicationSettings
            {
                ShowOfficialHolidays = showOfficialHolidays.IsChecked == true
            });
            Close(true);
        };
        cancel.Click += (_, _) => Close(false);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
            Children = { cancel, save }
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
                buttons
            }
        };
    }
}
