using System;
using System.Linq;
using System.Threading.Tasks;
using System.IO;
using System.IO.Pipes;
using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Threading;
using ErganiManager.Core.Interfaces;
using ErganiManager.Core.Models;
using ErganiManager.UI.ViewModels;
using ErganiManager.UI.Views;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace ErganiManager.UI;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        // The colours the user picked (background style + accent), before any window is shown.
        ErganiManager.UI.Services.AppearanceService.Load();
        ErganiManager.UI.Services.AppearanceService.Apply();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Listen for a second launch and foreground this existing application.
            _ = ListenForActivationRequestsAsync();

            // Exit when the user closes the main window.
            desktop.ShutdownMode = ShutdownMode.OnMainWindowClose;

            // We must set MainWindow SYNCHRONOUSLY before returning from this
            // method — Avalonia checks it immediately. Use a minimal splash
            // window while BootstrapAsync runs (DB probe, EF health check).
            var splash = BuildSplash();
            desktop.MainWindow = splash;
            splash.Show();

            // Fire-and-forget — transitions away from splash once ready.
            _ = BootstrapAsync(desktop, splash);
        }

        base.OnFrameworkInitializationCompleted();
    }


    private async Task ListenForActivationRequestsAsync()
    {
        while (true)
        {
            try
            {
                using var server = new NamedPipeServerStream(
                    Program.ActivationPipeName,
                    PipeDirection.In,
                    1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous);

                await server.WaitForConnectionAsync();
                using var reader = new StreamReader(server);
                var command = await reader.ReadLineAsync();
                if (string.Equals(command, "ACTIVATE", StringComparison.Ordinal))
                {
                    Dispatcher.UIThread.Post(ActivateExistingWindow);
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Single-instance activation listener encountered an error.");
                await Task.Delay(500);
            }
        }
    }

    private static void ActivateExistingWindow()
    {
        if (Application.Current?.ApplicationLifetime
            is not IClassicDesktopStyleApplicationLifetime desktop)
            return;

        // Prefer the topmost currently visible window (for example, an activation
        // dialog during startup); otherwise fall back to the main window.
        var window = desktop.Windows.LastOrDefault(w => w.IsVisible)
                     ?? desktop.MainWindow;
        if (window == null)
            return;

        try
        {
            if (window.WindowState == WindowState.Minimized)
                window.WindowState = WindowState.Normal;

            if (!window.IsVisible)
                window.Show();

            window.Activate();
            window.Topmost = true;
            window.Topmost = false;
            window.Focus();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Could not bring the existing ErganiManager window to the foreground.");
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
            // License gate: a PC without a valid license key cannot go on (only when a public key is built in).
            ErganiManager.UI.Services.LicenseManager.Load();
            if (!ErganiManager.Licensing.LicenseState.Enforced)
                Serilog.Log.Warning("License check is OFF: no public key is built into this program (LicenseKeys.cs). Nobody is asked for a license key.");
            if (!ErganiManager.UI.Services.LicenseManager.IsOk)
            {
                var activated = await new ActivationWindow(startup: true).ShowDialog<bool>(splash);
                if (!activated)
                {
                    desktop.Shutdown();
                    return;
                }
            }

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

            // A license that limits the PCs running at the same time: take a seat now when the shared database is
            // reachable, and in any case keep reporting every minute (a PC that starts while the database is
            // unreachable is counted as soon as the database is back).
            if (!await EnsureLicenseSeatAsync(desktop, splash, checkNow: state == AppConnectionState.Normal))
                return;

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
        // Saved passwords of older versions are converted so that every PC of this database can use them.
        _ = Task.Run(async () =>
        {
            try { await Program.Services.GetRequiredService<ICompanyService>().MigrateCredentialsAsync(); }
            catch { /* tried again at the next start */ }
        });

        Window next;

        if (session.Role == AppUserRole.Operator && !ErganiManager.Licensing.LicenseState.IsModuleLicensed(ErganiManager.Licensing.LicenseModules.Terminal))
        {
            _ = ShowModuleNotLicensedAsync(desktop, session);
            return;
        }

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

    // -- Licenses with a limit of PCs at the same time ------------------------------------------------

    private static async Task ShowMessageAsync(Window? owner, string text)
    {
        var loc = Program.Services.GetRequiredService<ILocalizationService>();
        var ok = new Button { Content = "OK", HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center, Margin = new Avalonia.Thickness(0, 16, 0, 0) };
        var panel = new StackPanel { Margin = new Avalonia.Thickness(24) };
        panel.Children.Add(new TextBlock { Text = text, TextWrapping = Avalonia.Media.TextWrapping.Wrap });
        panel.Children.Add(ok);

        var dialog = new Window
        {
            Title = loc["LicenseTitle"], Width = 520, SizeToContent = SizeToContent.Height, CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterScreen, Content = panel
        };
        ok.Click += (_, _) => dialog.Close();

        if (owner != null) await dialog.ShowDialog(owner); else dialog.Show();
    }

    /// <summary>
    /// Takes the seat of this PC when the license limits the PCs that may run at the same time, and keeps it by
    /// reporting every minute. Returns false (after a message) when all licensed PCs are in use.
    /// </summary>
    private async Task<bool> EnsureLicenseSeatAsync(IClassicDesktopStyleApplicationLifetime desktop, Window owner, bool checkNow)
    {
        var license = ErganiManager.Licensing.LicenseState.Current;
        if (!ErganiManager.Licensing.LicenseState.Enforced || license == null || !license.HasSeatLimit)
            return true;

        var loc = Program.Services.GetRequiredService<ILocalizationService>();
        var seats = Program.Services.GetRequiredService<ErganiManager.Core.Services.LicenseSeatService>();

        if (checkNow)
        {
            var result = await seats.AcquireAsync(license);
            if (result.Status == ErganiManager.Core.Services.SeatStatus.Denied)
            {
                await ShowMessageAsync(owner, string.Format(loc["LicenseSeatsFull"], result.Limit, string.Join(", ", result.InUseBy)));
                desktop.Shutdown();
                return false;
            }
        }

        // Report every minute; give the seat back when the program closes.
        var timer = new Avalonia.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(60) };
        timer.Tick += async (_, _) =>
        {
            var again = await seats.AcquireAsync(license);
            if (again.Status != ErganiManager.Core.Services.SeatStatus.Denied) return;

            timer.Stop();
            await ShowMessageAsync(desktop.MainWindow, string.Format(loc["LicenseSeatsLost"], again.Limit));
            desktop.Shutdown();
        };
        timer.Start();

        desktop.Exit += (_, _) => seats.ReleaseAsync().Wait(TimeSpan.FromSeconds(3));
        return true;
    }

    /// <summary>The terminal is not part of the license: say so and go back to the login screen.</summary>
    private async Task ShowModuleNotLicensedAsync(IClassicDesktopStyleApplicationLifetime desktop, UserSession session)
    {
        var loc = Program.Services.GetRequiredService<ILocalizationService>();
        var ok = new Button { Content = "OK", HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center, Margin = new Avalonia.Thickness(0, 16, 0, 0) };
        var panel = new StackPanel { Margin = new Avalonia.Thickness(24) };
        panel.Children.Add(new TextBlock { Text = loc["LicenseModuleMissing"], TextWrapping = Avalonia.Media.TextWrapping.Wrap });
        panel.Children.Add(ok);

        var dialog = new Window
        {
            Title = loc["LicenseTitle"], Width = 460, SizeToContent = SizeToContent.Height, CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterScreen, Content = panel
        };
        ok.Click += (_, _) => dialog.Close();

        if (desktop.MainWindow != null) await dialog.ShowDialog(desktop.MainWindow); else dialog.Show();
        SignOut(desktop, session);
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
