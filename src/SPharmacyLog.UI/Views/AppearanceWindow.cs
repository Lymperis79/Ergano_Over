using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Controls.Primitives;
using SPharmacyLog.UI.Services;

namespace SPharmacyLog.UI.Views;

/// <summary>Theme and accent picker with immediate preview and local persistence.</summary>
public sealed class AppearanceWindow : Window
{
    public AppearanceWindow()
    {
        Title = "Appearance";
        Width = 520;
        Height = 510;
        MinWidth = 480;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = new SolidColorBrush(Color.Parse("#151922"));
        var root = new StackPanel { Margin = new Thickness(22), Spacing = 14 };
        root.Children.Add(new TextBlock { Text = "Interface appearance", FontSize = 20, FontWeight = FontWeight.SemiBold });
        root.Children.Add(new TextBlock { Text = "Background style", FontSize = 13, FontWeight = FontWeight.Bold });
        var themes = new WrapPanel { Spacing = 8 };
        foreach (var preset in AppearanceService.Presets)
        {
            var button = new Button { Content = preset.Key, Padding = new Thickness(12, 8), MinWidth = 105 };
            button.Click += (_, _) => AppearanceService.Choose(theme: preset.Key);
            themes.Children.Add(button);
        }
        root.Children.Add(themes);
        root.Children.Add(new TextBlock { Text = "Accent color", FontSize = 13, FontWeight = FontWeight.Bold, Margin = new Thickness(0, 6, 0, 0) });
        var accents = new WrapPanel { Spacing = 10 };
        foreach (var hex in AppearanceService.AccentColors)
        {
            var color = Color.Parse(hex);
            var swatch = new Border { Width = 42, Height = 42, CornerRadius = new CornerRadius(21), Background = new SolidColorBrush(color), BorderBrush = new SolidColorBrush(Color.Parse("#D5DEE8")), BorderThickness = new Thickness(1) };
            var button = new Button { Content = swatch, Padding = new Thickness(3), Background = Brushes.Transparent, BorderThickness = new Thickness(1) };
            button.Click += (_, _) => AppearanceService.Choose(accent: hex);
            accents.Children.Add(button);
        }
        root.Children.Add(accents);
        root.Children.Add(new TextBlock { Text = "Changes apply immediately and are saved on this computer.", TextWrapping = TextWrapping.Wrap, Foreground = new SolidColorBrush(Color.Parse("#A9B7C8")) });
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8 };
        var reset = new Button { Content = "Reset defaults" };
        reset.Click += (_, _) => AppearanceService.Reset();
        var close = new Button { Content = "Close", Classes = { "accent" } };
        close.Click += (_, _) => Close();
        actions.Children.Add(reset); actions.Children.Add(close);
        root.Children.Add(actions);
        Content = root;
    }
}
