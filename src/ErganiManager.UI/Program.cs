using Avalonia;
using ErganiManager.Core;
using ErganiManager.ErganiApi;
using ErganiManager.LocalCache;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using System;
using System.Threading.Tasks;

namespace ErganiManager.UI;

internal static class Program
{
    // Built once at startup, used by App.axaml.cs to resolve services for views.
    public static IServiceProvider Services { get; private set; } = null!;

    [STAThread]
    public static async Task Main(string[] args)
    {
        ConfigureLogging();

        try
        {
            Log.Information("ErganiManager starting up. OS: {OS}", Environment.OSVersion);

            Services = BuildServiceProvider();

            // Start the background Ergani retry service before the UI opens
            var retryService = Services.GetRequiredService<ErganiManager.ErganiApi.Services.ErganiRetryService>();
            retryService.Start();

            // Pre-warm EF Core model compilation on a background thread.
            // First AppDbContext construction compiles the entity model (300-800ms).
            // Doing it here in parallel with the splash screen means the first
            // admin screen opens without delay.
            _ = Task.Run(async () =>
            {
                try
                {
                    var cs = Services.GetRequiredService<ErganiManager.Core.Interfaces.IConnectionStateService>();
                    if (cs.ConfigExists())
                    {
                        await using var db = new ErganiManager.Data.AppDbContext(cs.GetDbOptions());
                        _ = db.Model; // triggers EF Core model compilation
                    }
                }
                catch { /* ignore — DB may not be configured yet */ }
            });

            BuildAvaloniaApp()
                .StartWithClassicDesktopLifetime(args);

            // Graceful shutdown
            await retryService.StopAsync();
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Application terminated unexpectedly.");
        }
        finally
        {
            Log.CloseAndFlush();
        }
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();

    private static void ConfigureLogging()
    {
        var logsFolder = AppPaths.GetLogsFolder();
        var logFilePath = System.IO.Path.Combine(logsFolder, "ergani-.log");

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.File(
                logFilePath,
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 30,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
            .Enrich.FromLogContext()
            .CreateLogger();
    }

    private static IServiceProvider BuildServiceProvider()
    {
        var services = new ServiceCollection();

        services.AddLogging(builder => builder.AddSerilog(dispose: false));

        // Localization — singleton so the same instance is shared everywhere
        services.AddSingleton<ErganiManager.UI.Localization.ILocalizationService,
                              ErganiManager.UI.Localization.LocalizationService>();

        services.AddErganiManagerCore();
        services.AddErganiApiClient();

        // ViewModels are transient — a fresh instance each time a view is requested.
        services.AddTransient<ViewModels.LanguageSelectorViewModel>();
        services.AddTransient<ViewModels.DatabaseSetupViewModel>();
        services.AddTransient<ViewModels.LoginViewModel>();
        services.AddTransient<ViewModels.TerminalViewModel>();
        services.AddTransient<ViewModels.AdminShellViewModel>();
        services.AddTransient<ViewModels.CompaniesViewModel>();
        services.AddTransient<ViewModels.BranchesViewModel>();
        services.AddTransient<ViewModels.EmployeesViewModel>();
        services.AddTransient<ViewModels.UsersViewModel>();
        services.AddTransient<ViewModels.SchedulesViewModel>();
        services.AddTransient<ViewModels.WorkCardHistoryViewModel>();
        services.AddTransient<ViewModels.WorkCardScanViewModel>();
        services.AddTransient<ViewModels.OvertimeViewModel>();
        services.AddTransient<ViewModels.SubmissionLogViewModel>();

        return services.BuildServiceProvider();
    }
}
