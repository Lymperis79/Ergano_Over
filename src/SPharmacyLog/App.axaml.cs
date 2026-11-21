using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using SPharmacyLog.Data;
using SPharmacyLog.Views;

namespace SPharmacyLog;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override async void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            try
            {
                await using var db = new PharmacyDbContext(Program.DbOptions());
                await db.Database.EnsureCreatedAsync();
                desktop.MainWindow = new LoginWindow();
            }
            catch (Exception ex)
            {
                desktop.MainWindow = new Window
                {
                    Title = "S Pharmacy Log - Database startup error",
                    Width = 600, Height = 240,
                    Content = new TextBlock { Text = ex.Message, Margin = new Thickness(24), TextWrapping = Avalonia.Media.TextWrapping.Wrap }
                };
            }
            desktop.MainWindow?.Show();
        }
        base.OnFrameworkInitializationCompleted();
    }
}