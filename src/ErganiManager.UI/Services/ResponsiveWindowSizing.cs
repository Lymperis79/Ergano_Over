using Avalonia;
using Avalonia.Controls;

namespace ErganiManager.UI.Services;

/// <summary>
/// Applies screen-aware bounds to desktop windows so their title bars and contents
/// remain accessible on laptop and low-resolution displays.
/// </summary>
public static class ResponsiveWindowSizing
{
    public static void Apply(Window window, double preferredWidth, double preferredHeight,
        double minimumWidth = 420, double minimumHeight = 320, double margin = 24)
    {
        void ResizeToScreen()
        {
            var screen = window.Screens.ScreenFromWindow(window) ?? window.Screens.Primary;
            if (screen is null) return;

            var scale = screen.Scaling > 0 ? screen.Scaling : 1d;
            var area = screen.WorkingArea;
            var availableWidth = Math.Max(320, area.Width / scale - margin);
            var availableHeight = Math.Max(240, area.Height / scale - margin);

            // Keep the requested minimums flexible on compact screens.
            var minW = Math.Min(minimumWidth, Math.Max(320, availableWidth - 16));
            var minH = Math.Min(minimumHeight, Math.Max(240, availableHeight - 16));
            var maxW = Math.Max(minW, availableWidth);
            var maxH = Math.Max(minH, availableHeight);

            window.MinWidth = minW;
            window.MinHeight = minH;
            window.MaxWidth = maxW;
            window.MaxHeight = maxH;
            window.Width = Math.Clamp(preferredWidth, minW, maxW);
            window.Height = Math.Clamp(preferredHeight, minH, maxH);
            window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }

        window.Opened += (_, _) => ResizeToScreen();
        window.SizeToContent = SizeToContent.Manual;
    }
}
