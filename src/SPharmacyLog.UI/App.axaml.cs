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
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override async void OnFrameworkInitializationCompleted()
    {
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

                await using (var database = new PharmacyDbContext(PharmacyDb.Options()))
                {
                    await database.Database.MigrateAsync();
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
                    Width = 600,
                    Height = 240,
                    Content = new TextBlock
                    {
                        Text = exception.Message,
                        Margin = new Thickness(24),
                        TextWrapping = TextWrapping.Wrap
                    }
                };

                desktop.MainWindow = errorWindow;
                errorWindow.Show();
                startupWindow.Close();
            }
        }

        base.OnFrameworkInitializationCompleted();
    }
}
