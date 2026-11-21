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
                Title = "S Pharmacy Log",
                Width = 1,
                Height = 1,
                Opacity = 0,
                ShowInTaskbar = false
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
                    Title = "S Pharmacy Log startup error",
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
                                Text = "S Pharmacy Log could not complete startup. Your existing database has not been deleted.",
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
