using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using ErganiManager.Core.Interfaces;
using Microsoft.Toolkit.Uwp.Notifications;

namespace ErganiManager.UI.Services;

/// <summary>
/// Shows desktop alerts through the native Windows notification system.
/// Other desktop platforms retain the Avalonia pop-up fallback.
/// </summary>
public static class DesktopAlertNotificationWindow
{
    public static void Show(AlertMessage alert)
    {
        if (OperatingSystem.IsWindows())
        {
            try
            {
                var details = string.Join(" • ",
                    alert.Lines
                        .Where(line => !string.IsNullOrWhiteSpace(line.Value))
                        .Select(line => $"{line.Label}: {line.Value}"));

                var notification = new ToastContentBuilder()
                    .AddText("Ergani Manager")
                    .AddText(alert.Title);

                if (!string.IsNullOrWhiteSpace(details))
                    notification.AddText(details);

                // ToastNotificationManagerCompat automatically registers the
                // unpackaged Win32 application identity/activator on Windows.
                notification.Show();
                return;
            }
            catch
            {
                // Keep alerts visible if Windows toast services are unavailable.
                ShowAvaloniaFallback(alert);
                return;
            }
        }

        ShowAvaloniaFallback(alert);
    }

    private static void ShowAvaloniaFallback(AlertMessage alert)
    {
        Dispatcher.UIThread.Post(() =>
        {
            var window = new Window
            {
                Title = "Ergani Manager — Alert",
                Width = 390,
                SizeToContent = SizeToContent.Height,
                CanResize = false,
                ShowInTaskbar = false,
                Topmost = true,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                Background = new SolidColorBrush(Color.Parse("#202B3A")),
                Padding = new Thickness(0)
            };

            var details = string.Join(Environment.NewLine,
                alert.Lines.Select(line => $"{line.Label}: {line.Value}"));

            var close = new Button
            {
                Content = "Dismiss",
                HorizontalAlignment = HorizontalAlignment.Right,
                MinWidth = 90,
                Margin = new Thickness(0, 12, 0, 0)
            };
            close.Click += (_, _) => window.Close();

            window.Content = new Border
            {
                Background = new SolidColorBrush(Color.Parse("#202B3A")),
                BorderBrush = new SolidColorBrush(Color.Parse("#D69B35")),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(18),
                Child = new StackPanel
                {
                    Spacing = 9,
                    Children =
                    {
                        new TextBlock
                        {
                            Text = "⚠  ERGANI MANAGER ALERT",
                            Foreground = new SolidColorBrush(Color.Parse("#F0C674")),
                            FontWeight = FontWeight.Bold,
                            FontSize = 12
                        },
                        new TextBlock
                        {
                            Text = alert.Title,
                            Foreground = Brushes.White,
                            FontWeight = FontWeight.SemiBold,
                            FontSize = 16,
                            TextWrapping = TextWrapping.Wrap
                        },
                        new TextBlock
                        {
                            Text = details,
                            Foreground = new SolidColorBrush(Color.Parse("#E0E7EF")),
                            FontSize = 13,
                            TextWrapping = TextWrapping.Wrap
                        },
                        close
                    }
                }
            };

            window.Show();
            _ = CloseAfterDelayAsync(window);
        });
    }

    private static async Task CloseAfterDelayAsync(Window window)
    {
        await Task.Delay(TimeSpan.FromSeconds(12));
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (window.IsVisible) window.Close();
        });
    }
}
