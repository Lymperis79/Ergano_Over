using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input.Platform;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using ErganiManager.Core.Interfaces;
using ErganiManager.Core.Models;
using ErganiManager.UI.ViewModels;
using ErganiManager.UI.Views;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace ErganiManager.UI;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Exit when the user closes the main window.
            desktop.ShutdownMode = ShutdownMode.OnMainWindowClose;

            // We must set MainWindow SYNCHRONOUSLY before returning from this
            // method — Avalonia checks it immediately. Use a minimal splash
            // window while BootstrapAsync runs (DB probe, EF health check).
            var splash = BuildSplash();
            desktop.MainWindow = splash;
            splash.Show();
            Log.Information("UI thread apartment state: {Apt}",
            System.Threading.Thread.CurrentThread.GetApartmentState());
            _ = TestClipboardAsync(splash);

            // Fire-and-forget — transitions away from splash once ready.
            _ = BootstrapAsync(desktop, splash);
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static async Task TestClipboardAsync(Window window)
    {
        try
        {
            var clipboard = TopLevel.GetTopLevel(window)?.Clipboard;
            if (clipboard is null)
            {
                Log.Warning("Clipboard test: Clipboard is null.");
                return;
            }

            await clipboard.SetTextAsync("ergani-clipboard-test");
            var back = await clipboard.TryGetTextAsync();
            Log.Information("Clipboard test round trip returned: {Back}", back);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Clipboard test failed.");
        }
    }
    // ── Bootstrap ─────────────────────────────────────────────────────────────

    private static Window BuildSplash()
    {
        var splash = new Window
        {
            Title = "Ergani Manager",
            Icon = new WindowIcon(Avalonia.Platform.AssetLoader.Open(
                new Uri("avares://ErganiManager.UI/Assets/icon.png"))),
            Width = 340,
            Height = 160,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            CanResize = false,
            Content = new Avalonia.Controls.TextBlock
            {
                Text = Program.Services.GetRequiredService<ILocalizationService>()["Vm.Starting…"],
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                VerticalAlignment   = Avalonia.Layout.VerticalAlignment.Center,
                FontSize = 16
            }
        };
        return splash;
    }

    private async Task BootstrapAsync(
        IClassicDesktopStyleApplicationLifetime desktop, Window splash)
    {
        try
        {
            var connectionState = Program.Services.GetRequiredService<IConnectionStateService>();
            var state = await Task.Run(() => connectionState.EvaluateAsync());

            // Never allow a fresh installation into the offline login screen.
            // This is an explicit startup guard in addition to EvaluateAsync(),
            // so a stale/corrupt cache or a future state change cannot route a
            // new installation into the "previously synced users" login path.
            if (state == AppConnectionState.Degraded &&
                !await connectionState.HasCachedUsersAsync())
            {
                Log.Information("Database unavailable and no cached users exist; routing to database setup.");
                state = AppConnectionState.FirstRun;
            }

            // Database exists but setup stopped before the first administrator was created:
            // a login screen could never succeed, so resume the wizard at the user step.
            if (state == AppConnectionState.Normal &&
                await Program.Services.GetRequiredService<IAdminBootstrapService>().NeedsFirstAdminAsync())
            {
                Log.Information("Database ready but no users exist; resuming setup at the administrator step.");
                ShowDatabaseSetup(desktop, resumeAtAdmin: true);
                return;
            }

            Log.Information("Startup connection state: {State}. Reason: {Reason}",
                state, connectionState.LastFailureReason);

            switch (state)
            {
                case AppConnectionState.FirstRun:
                case AppConnectionState.SchemaIncomplete:
                    ShowDatabaseSetup(desktop,
                        isRetry: state == AppConnectionState.SchemaIncomplete,
                        failureReason: connectionState.LastFailureReason);
                    break;

                case AppConnectionState.Normal:
                case AppConnectionState.Degraded:
                    ShowLogin(desktop,
                        isDegraded: state == AppConnectionState.Degraded);
                    break;
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Fatal error during bootstrap.");
            // Show the error in the splash window so the user sees something.
            if (splash.Content is Avalonia.Controls.TextBlock tb)
                tb.Text = string.Format(Program.Services.GetRequiredService<ILocalizationService>()["Vm.Startup error:\n{0}"], ex.Message);
        }
    }

    // ── Window transitions ────────────────────────────────────────────────────

    /// <summary>
    /// Atomically sets the new MainWindow, shows it, then closes the previous one.
    /// Closing the previous window AFTER the new one is shown avoids a race where
    /// closing the only open window triggers OnMainWindowClose shutdown.
    /// </summary>
    private static void TransitionTo(
        IClassicDesktopStyleApplicationLifetime desktop, Window next)
    {
        var previous = desktop.MainWindow;

        // Unsubscribe any close-all handler the previous window had,
        // so closing it doesn't cascade to the new window.
        if (previous != null)
            previous.Closed -= OnMainWindowClosed;

        desktop.MainWindow = next;
        next.Show();

        // Only the final app window should close secondary windows on exit.
        // We attach/reattach on every transition so it always tracks the current main window.
        next.Closed += OnMainWindowClosed;

        previous?.Close();
    }

    private static void OnMainWindowClosed(object? sender, EventArgs e)
    {
        // Close any secondary windows (e.g. BarcodeScanWindow) when the main window closes.
        // Use Environment.Exit as a safety net in case windows don't close cleanly.
        if (Application.Current?.ApplicationLifetime
            is IClassicDesktopStyleApplicationLifetime desktop)
        {
            foreach (var w in desktop.Windows.ToList())
            {
                if (w != sender) w.Close();
            }

            // Selenium browsers/drivers are external processes. Close them before
            // terminating the application so ChromeDriver/EdgeDriver is not left behind.
            try
            {
                ErganiManager.ErganiApi.Services.ErganiPortalService.ShutdownAllBrowsers();
            }
            catch { /* shutdown must never prevent application exit */ }
        }
        Environment.Exit(0);
    }

    public void ShowDatabaseSetup(
        IClassicDesktopStyleApplicationLifetime desktop, bool isRetry = false,
        bool resumeAtAdmin = false, string? failureReason = null)
    {
        var vm = Program.Services.GetRequiredService<DatabaseSetupViewModel>();

        if (resumeAtAdmin)
            vm.ResumeAtAdminStep();
        else if (isRetry)
            vm.StatusMessage =
                vm.Loc["Vm.⚠️ The database schema is incomplete"] +
                (string.IsNullOrWhiteSpace(failureReason) ? "" : $" ({failureReason})") +
                vm.Loc["Vm.. Check the settings below and continue to repair it."];

        vm.SetupCompleted += (_, _) => ShowLogin(desktop, isDegraded: false);

        TransitionTo(desktop, new DatabaseSetupView { DataContext = vm });
    }

    public void ShowLogin(
        IClassicDesktopStyleApplicationLifetime desktop, bool isDegraded)
    {
        var vm = Program.Services.GetRequiredService<LoginViewModel>();
        vm.IsDegradedMode = isDegraded;
        vm.LoginSucceeded += (_, session) => OnLoginSucceeded(desktop, session);

        TransitionTo(desktop, new LoginView { DataContext = vm });
    }

    private void OnLoginSucceeded(
        IClassicDesktopStyleApplicationLifetime desktop, UserSession session)
    {
        Window next;

        if (session.Role == AppUserRole.Operator)
        {
            var vm = Program.Services.GetRequiredService<TerminalViewModel>();
            vm.Initialize(session);
            vm.LogoutRequested += (_, _) => SignOut(desktop, session);
            vm.ExitRequested += (_, _) => desktop.MainWindow?.Close();
            next = new TerminalView { DataContext = vm };
        }
        else
        {
            var vm = Program.Services.GetRequiredService<AdminShellViewModel>();
            vm.Initialize(session);
            vm.LogoutRequested += (_, _) => SignOut(desktop, session);
            next = new AdminShellView { DataContext = vm };
        }

        TransitionTo(desktop, next);
    }

    /// <summary>
    /// Signs the user out: ends the session, closes the other windows (e.g. the always-on-top scan window,
    /// which belongs to this session) and shows the login screen again.
    /// </summary>
    private void SignOut(IClassicDesktopStyleApplicationLifetime desktop, UserSession session)
    {
        if (desktop.MainWindow?.DataContext is TerminalViewModel terminal)
            terminal.Shutdown();

        foreach (var window in desktop.Windows.ToList())
            if (window != desktop.MainWindow) window.Close();

        Program.Services.GetRequiredService<IAuthService>().Logout();
        Log.Information("User {Username} signed out.", session.Username);

        ShowLogin(desktop, isDegraded: session.IsOfflineSession);
    }
}
