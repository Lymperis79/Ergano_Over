using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media;
using Avalonia.Markup.Xaml;
using Microsoft.EntityFrameworkCore;
using SPharmacyLog.Data;
using SPharmacyLog.UI.Services;
using SPharmacyLog.UI.Views;

namespace SPharmacyLog.UI;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override async void OnFrameworkInitializationCompleted()
    {
        AppearanceService.Load();
        AppearanceService.Apply();
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var startupWindow = new Window
            {
                Title =  $"Starting {ApplicationBranding.Name}",
                Width = 440,
                Height = 220,
                MinWidth = 440,
                MinHeight = 220,
                MaxWidth = 440,
                MaxHeight = 220,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                ShowInTaskbar = false,
                CanResize = false,
                Content = new Border
                {
                    Padding = new Thickness(28),
                    Background = new SolidColorBrush(Color.Parse("#202733")),
                    Child = new StackPanel
                    {
                        VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                        Spacing = 14,
                        Children =
                        {
                            new TextBlock
                            {
                                Text = ApplicationBranding.Name,
                                FontSize = 24,
                                FontWeight = FontWeight.Bold,
                                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center
                            },
                            new TextBlock
                            {
                                Text = "Starting application...\nPlease wait.",
                                FontSize = 16,
                                TextAlignment = TextAlignment.Center,
                                TextWrapping = TextWrapping.Wrap,
                                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center
                            }
                        }
                    }
                }
            };

            desktop.MainWindow = startupWindow;
            startupWindow.Show();

            try
            {
                if (!PharmacyLicenseManager.Load())
                {
                    var activationWindow = new LicenseActivationWindow();
                    await activationWindow.ShowDialog(startupWindow);

                    if (!PharmacyLicenseManager.Result.IsValid)
                    {
                        desktop.Shutdown();
                        return;
                    }
                }

                var config = PharmacyDb.LoadConfig();
                if (config is null)
                {
                    await ShowDatabaseSetupAsync(startupWindow);
                    config = PharmacyDb.LoadConfig();
                }
                else
                {
                    var connection = await DbProviderFactory.TestConnectionAsync(config);
                    if (!connection.Success)
                    {
                        await ShowDatabaseSetupAsync(startupWindow);
                        config = PharmacyDb.LoadConfig();
                    }
                }

                if (config is null)
                {
                    desktop.Shutdown();
                    return;
                }

                // Apply pending schema changes on every start. EF only applies migrations
                // that are not already recorded; it never drops or recreates existing data.
                try
                {
                    await PharmacyDb.ApplyMigrationsAsync(config);
                }
                catch
                {
                    // Re-open the setup screen with the saved settings so the user can
                    // correct a connection or retry an interrupted schema upgrade.
                    await ShowDatabaseSetupAsync(startupWindow);
                    config = PharmacyDb.LoadConfig();
                    if (config is null) throw;
                    await PharmacyDb.ApplyMigrationsAsync(config);
                }

                if (!await PharmacyDb.HasUsersAsync(config))
                {
                    await ShowDatabaseSetupAsync(startupWindow, resumeAtAdmin: true);
                    config = PharmacyDb.LoadConfig();
                    if (config is null || !await PharmacyDb.HasUsersAsync(config))
                    {
                        desktop.Shutdown();
                        return;
                    }
                }

                var loginWindow = new LoginWindow();
                desktop.MainWindow = loginWindow;
                loginWindow.Show();
                startupWindow.Close();
            }
            catch (Exception exception)
            {
                var errorWindow = new Window
                {
                    Title =  $"{ApplicationBranding.Name} startup error",
                    Width = 620,
                    SizeToContent = SizeToContent.Height,
                    Content = new StackPanel
                    {
                        Margin = new Thickness(24),
                        Spacing = 14,
                        Children =
                        {
                            new TextBlock
                            {
                                Text =  $"{ApplicationBranding.Name} could not complete startup. Your existing database has not been deleted.",
                                FontWeight = FontWeight.SemiBold,
                                TextWrapping = TextWrapping.Wrap
                            },
                            new TextBlock
                            {
                                Text = exception.Message,
                                TextWrapping = TextWrapping.Wrap
                            },
                            new Button
                            {
                                Content = "Close",
                                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right
                            }
                        }
                    }
                };
                if (errorWindow.Content is StackPanel panel && panel.Children[2] is Button close)
                    close.Click += (_, _) => desktop.Shutdown();
                desktop.MainWindow = errorWindow;
                errorWindow.Show();
                startupWindow.Close();
            }
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static async Task ShowDatabaseSetupAsync(Window owner, bool resumeAtAdmin = false)
    {
        var setup = new DatabaseSetupWindow(resumeAtAdmin);
        await setup.ShowDialog(owner);
    }
}
