using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
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

            // Fire-and-forget — transitions away from splash once ready.
            _ = BootstrapAsync(desktop, splash);
        }

        base.OnFrameworkInitializationCompleted();
    }

    // ── Bootstrap ─────────────────────────────────────────────────────────────

    private static Window BuildSplash()
    {
        var splash = new Window
        {
            Title = "Ergani Manager",
            Width = 340,
            Height = 160,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            CanResize = false,
            Content = new Avalonia.Controls.TextBlock
            {
                Text = "Starting…",
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
            Log.Information("Startup connection state: {State}.", state);

            switch (state)
            {
                case AppConnectionState.FirstRun:
                case AppConnectionState.SchemaIncomplete:
                    ShowDatabaseSetup(desktop,
                        isRetry: state == AppConnectionState.SchemaIncomplete);
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
                tb.Text = $"Startup error:\n{ex.Message}";
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
        }
        Environment.Exit(0);
    }

    public void ShowDatabaseSetup(
        IClassicDesktopStyleApplicationLifetime desktop, bool isRetry = false)
    {
        var vm = Program.Services.GetRequiredService<DatabaseSetupViewModel>();

        if (isRetry)
            vm.StatusMessage =
                "⚠️ A previous setup attempt failed. Fix the settings below and try again.";

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
            next = new TerminalView { DataContext = vm };
        }
        else
        {
            var vm = Program.Services.GetRequiredService<AdminShellViewModel>();
            vm.Initialize(session);
            next = new AdminShellView { DataContext = vm };
        }

        TransitionTo(desktop, next);
    }
}
