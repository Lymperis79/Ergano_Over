using Avalonia;
using ErganiManager.Core;
using ErganiManager.ErganiApi;
using ErganiManager.LocalCache;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using System;
using System.Threading;
using System.IO.Pipes;
using System.IO;
using System.Threading.Tasks;

namespace ErganiManager.UI;

internal static class Program
{
    // Built once at startup, used by App.axaml.cs to resolve services for views.
    public static IServiceProvider Services { get; private set; } = null!;

    // Keep these names stable between launches. The mutex prevents duplicate
    // processes; the pipe tells the already-running process to foreground its UI.
    public const string SingleInstanceMutexName = @"Global\ErganiManager_SingleInstance_7D34A92F";
    public const string ActivationPipeName = "ErganiManager_Activate_7D34A92F";
    private static Mutex? _singleInstanceMutex;

    [STAThread]
    public static void Main(string[] args)
    {
        bool createdNew;
        try
        {
            _singleInstanceMutex = new Mutex(
                initiallyOwned: true,
                name: OperatingSystem.IsWindows()
                    ? SingleInstanceMutexName
                    : "ErganiManager_SingleInstance_7D34A92F",
                createdNew: out createdNew);
        }
        catch (UnauthorizedAccessException)
        {
            // A global mutex may be inaccessible across Windows security contexts.
            // Fall back to a per-user/session mutex rather than failing to start.
            _singleInstanceMutex = new Mutex(
                initiallyOwned: true,
                name: "ErganiManager_SingleInstance_7D34A92F",
                createdNew: out createdNew);
        }

        if (!createdNew)
        {
            _singleInstanceMutex.Dispose();
            _singleInstanceMutex = null;
            RequestExistingInstanceActivation();
            return;
        }

        try
        {
            RunApplication(args);
        }
        finally
        {
            try { _singleInstanceMutex?.ReleaseMutex(); } catch (ApplicationException) { }
            _singleInstanceMutex?.Dispose();
            _singleInstanceMutex = null;
        }
    }

    private static void RequestExistingInstanceActivation()
    {
        // The first instance may still be initializing Avalonia, so retry briefly.
        for (int attempt = 0; attempt < 8; attempt++)
        {
            try
            {
                using var client = new NamedPipeClientStream(
                    ".", ActivationPipeName, PipeDirection.Out);
                client.Connect(400);
                using var writer = new StreamWriter(client) { AutoFlush = true };
                writer.WriteLine("ACTIVATE");
                return;
            }
            catch (TimeoutException) { Thread.Sleep(250); }
            catch (IOException) { Thread.Sleep(250); }
        }
    }

    private static void RunApplication(string[] args)
    {
        ConfigureLogging();

        try
        {
            Log.Information("ErganiManager starting up. OS: {OS}", Environment.OSVersion);

            Services = BuildServiceProvider();

            // Leave type settings are shared through the main database; the JSON file is only an offline cache.
            ErganiManager.Core.Models.LeaveTypes.ConfigureDatabase(() =>
                Services.GetRequiredService<ErganiManager.Core.Interfaces.IConnectionStateService>().GetDbOptions());

            // Messages produced by the lower layers (Data / Core / ErganiApi) are written in English
            // and translated here, in the language that is active when the message is created.
            var localization = Services.GetRequiredService<ILocalizationService>();
            ErganiManager.Data.UiText.Translator = english => localization["Core." + english];

            // Start the background Ergani retry service before the UI opens
            var retryService = Services.GetRequiredService<ErganiManager.ErganiApi.Services.ErganiRetryService>();
            retryService.Start();
            var autoClockOutMonitor = Services.GetRequiredService<ErganiManager.UI.Services.AutoClockOutMonitor>();
            autoClockOutMonitor.Start();
            var automaticDatabaseBackup = Services.GetRequiredService<ErganiManager.UI.Services.AutomaticDatabaseBackupService>();
            automaticDatabaseBackup.Start();
            var dailyReconciliation = Services.GetRequiredService<ErganiManager.UI.Services.DailyReconciliationService>();
            dailyReconciliation.Start();

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
            retryService.StopAsync().GetAwaiter().GetResult();
            Services.GetRequiredService<ErganiManager.UI.Services.AutoClockOutMonitor>().StopAsync().GetAwaiter().GetResult();
            Services.GetRequiredService<ErganiManager.UI.Services.AutomaticDatabaseBackupService>().Stop();
            Services.GetRequiredService<ErganiManager.UI.Services.DailyReconciliationService>().StopAsync().GetAwaiter().GetResult();
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
        services.AddSingleton<ErganiManager.UI.Services.CompanyLogoService>();
        services.AddSingleton<ErganiManager.UI.Services.AutoClockOutMonitor>();
        services.AddSingleton<ErganiManager.UI.Services.AutomaticDatabaseBackupService>();
        services.AddSingleton<ErganiManager.UI.Services.DailyReconciliationService>();
        services.AddErganiApiClient();

        // ViewModels are transient — a fresh instance each time a view is requested.
        services.AddTransient<ViewModels.LanguageSelectorViewModel>();
        services.AddTransient<ViewModels.DatabaseSetupViewModel>();
        services.AddTransient<ViewModels.LoginViewModel>();
        services.AddTransient<ViewModels.TerminalViewModel>();
        services.AddTransient<ViewModels.AdminShellViewModel>();
        services.AddTransient<ViewModels.DashboardViewModel>();
        services.AddTransient<ViewModels.CompaniesViewModel>();
        services.AddTransient<ViewModels.BranchesViewModel>();
        services.AddTransient<ViewModels.EmployeesViewModel>();
        services.AddTransient<ViewModels.UsersViewModel>();
        services.AddTransient<ViewModels.SchedulesViewModel>();
        services.AddTransient<ViewModels.WorkCardHistoryViewModel>();
        services.AddTransient<ViewModels.WorkCardScanViewModel>();
        services.AddTransient<ViewModels.TodayBoardViewModel>();
        services.AddTransient<ViewModels.OvertimeViewModel>();
        services.AddTransient<ViewModels.ReportsViewModel>();
        services.AddTransient<ViewModels.SubmissionLogViewModel>();
        services.AddTransient<ViewModels.EmailLogViewModel>();
        services.AddTransient<ViewModels.AdministrationViewModel>();
        services.AddTransient<ViewModels.HelpViewModel>();

        return services.BuildServiceProvider();
    }
}
