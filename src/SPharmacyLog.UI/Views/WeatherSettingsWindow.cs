using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using SPharmacyLog.UI.Services;

namespace SPharmacyLog.UI.Views;

public sealed class WeatherSettingsWindow : UserControl
{
    public event Action? SettingsSaved;
    private readonly TextBlock saveStatus = new() { Foreground = Brushes.LightGray };
    private readonly TextBox location;
    private readonly CheckBox showLocation, temperature, condition, feelsLike, humidity, wind;

    public WeatherSettingsWindow()
    {
        var settings = WeatherSettingsService.Load();
        Background = new SolidColorBrush(Color.Parse("#202733"));

        location = new TextBox { Text = settings.Location, PlaceholderText = "City, country or latitude, longitude", MinHeight = 38 };
        showLocation = Check("Show location", settings.ShowLocation);
        temperature = Check("Show temperature", settings.ShowTemperature);
        condition = Check("Show weather condition", settings.ShowCondition);
        feelsLike = Check("Show feels-like temperature", settings.ShowFeelsLike);
        humidity = Check("Show humidity", settings.ShowHumidity);
        wind = Check("Show wind speed", settings.ShowWind);

        var save = new Button { Content = "Save", Classes = { "accent" }, MinWidth = 100 };
        save.Click += (_, _) =>
        {
            WeatherSettingsService.Save(new WeatherSettings
            {
                Location = location.Text?.Trim() ?? "",
                ShowLocation = showLocation.IsChecked == true,
                ShowTemperature = temperature.IsChecked == true,
                ShowCondition = condition.IsChecked == true,
                ShowFeelsLike = feelsLike.IsChecked == true,
                ShowHumidity = humidity.IsChecked == true,
                ShowWind = wind.IsChecked == true
            });
            saveStatus.Text = SPharmacyLog.UI.Localization.Tr.Loc.TranslateText("Settings saved.");
            SettingsSaved?.Invoke();
        };

        var content = new StackPanel { Margin = new Thickness(22), Spacing = 12 };
        content.Children.Add(new TextBlock { Text = "Weather display", FontSize = 21, FontWeight = FontWeight.SemiBold });
        content.Children.Add(new TextBlock { Text = "Choose a city or enter coordinates as latitude, longitude.", TextWrapping = TextWrapping.Wrap, Foreground = Brushes.LightGray });
        content.Children.Add(location);
        content.Children.Add(new TextBlock { Text = "Fields shown on the dashboard", FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 8, 0, 0) });
        foreach (var check in new[] { showLocation, temperature, condition, feelsLike, humidity, wind }) content.Children.Add(check);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8, Margin = new Thickness(0, 10, 0, 0) };
        buttons.Children.Add(save);
        content.Children.Add(buttons);
        content.Children.Add(saveStatus);
        Content = content;
    }

    private static CheckBox Check(string label, bool value) => new() { Content = label, IsChecked = value };
}
