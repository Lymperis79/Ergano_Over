using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Avalonia;
using Avalonia.Media;
using ErganiManager.LocalCache;

namespace ErganiManager.UI.Services;

/// <summary>One background style: the colours of the window, menu, header, cards, hover and borders.</summary>
public sealed record ThemePreset(string Key, string Window, string Sidebar, string Panel, string Card, string Hover, string Line);

/// <summary>
/// The colours of the interface. The user picks a background style and an accent colour (the 🎨 button in
/// the header); the choice is applied at once, saved on this PC and restored at the next start.
/// The XAML uses the named brushes (WindowBrush, CardBrush, AccentBrush, ...), which are replaced here.
/// </summary>
public static class AppearanceService
{
    public const string DefaultTheme = "DarkBlue";
    public const string DefaultAccent = "#2D9CDB";

    public static readonly IReadOnlyList<ThemePreset> Presets = new[]
    {
        new ThemePreset("DarkBlue", "#202020", "#16181D", "#1E2128", "#1A1D23", "#22252C", "#3A3F4B"),   // the original look
        new ThemePreset("Graphite", "#1C1C1C", "#141414", "#232323", "#262626", "#2E2E2E", "#424242"),
        new ThemePreset("Forest",   "#17201B", "#101813", "#1B2A21", "#1E2D24", "#26382D", "#35503F"),
        new ThemePreset("Purple",   "#1F1A26", "#16111C", "#271F31", "#2A2235", "#33293F", "#4B3C5E"),
        new ThemePreset("Wine",     "#261A1C", "#1B1113", "#2F2023", "#332326", "#3D2B2F", "#5A3C41"),
        new ThemePreset("Midnight", "#0B0B0F", "#050508", "#101016", "#13131A", "#1B1B24", "#2B2B38"),
    };

    public static readonly IReadOnlyList<string> AccentColors = new[]
    {
        "#2D9CDB", "#26A69A", "#43A047", "#FB8C00", "#E53935", "#8E24AA", "#D81B60", "#8D6E63"
    };

    public static string Theme { get; private set; } = DefaultTheme;
    public static string Accent { get; private set; } = DefaultAccent;

    private sealed class Stored
    {
        public string Theme { get; set; } = DefaultTheme;
        public string Accent { get; set; } = DefaultAccent;
    }

    private static string FilePath => Path.Combine(AppPaths.GetAppDataFolder(), "appearance.json");

    /// <summary>Reads the saved choice (the defaults when there is none or the file is damaged).</summary>
    public static void Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return;
            var stored = JsonSerializer.Deserialize<Stored>(File.ReadAllText(FilePath));
            if (stored == null) return;
            if (Presets.Any(p => p.Key == stored.Theme)) Theme = stored.Theme;
            if (Color.TryParse(stored.Accent, out _)) Accent = stored.Accent;
        }
        catch { /* unreadable file: keep the defaults */ }
    }

    private static void Save()
    {
        try { File.WriteAllText(FilePath, JsonSerializer.Serialize(new Stored { Theme = Theme, Accent = Accent })); }
        catch { /* the choice just is not remembered */ }
    }

    public static void Choose(string? theme = null, string? accent = null)
    {
        if (theme != null && Presets.Any(p => p.Key == theme)) Theme = theme;
        if (accent != null && Color.TryParse(accent, out _)) Accent = accent;
        Apply();
        Save();
    }

    public static void Reset() => Choose(DefaultTheme, DefaultAccent);

    /// <summary>Replaces the named brushes of the application; every window updates at once.</summary>
    public static void Apply()
    {
        var app = Application.Current;
        if (app == null) return;

        var preset = Presets.FirstOrDefault(p => p.Key == Theme) ?? Presets[0];
        var accent = Color.Parse(Accent);
        var resources = app.Resources;

        void Brush(string key, Color color) => resources[key] = new SolidColorBrush(color);

        Brush("WindowBrush",  Color.Parse(preset.Window));
        Brush("SidebarBrush", Color.Parse(preset.Sidebar));
        Brush("PanelBrush",   Color.Parse(preset.Panel));
        Brush("CardBrush",    Color.Parse(preset.Card));
        Brush("HoverBrush",   Color.Parse(preset.Hover));
        Brush("LineBrush",    Color.Parse(preset.Line));

        Brush("AccentBrush",      accent);
        Brush("AccentHoverBrush", Mix(accent, Colors.Black, 0.15));
        Brush("AccentLightBrush", Mix(accent, Colors.White, 0.40));
        Brush("AccentSoftBrush",  Color.FromArgb(0x33, accent.R, accent.G, accent.B));
        Brush("AccentFaintBrush", Color.FromArgb(0x1A, accent.R, accent.G, accent.B));

        // The open menu item: the menu colour with a good part of the accent in it.
        var sidebar = Color.Parse(preset.Sidebar);
        Brush("NavActiveBrush",      Mix(sidebar, accent, 0.35));
        Brush("NavActiveHoverBrush", Mix(sidebar, accent, 0.45));

        // The theme's own accent (selection, focus, check boxes, toggle switches, ...).
        resources["SystemAccentColor"]       = accent;
        resources["SystemAccentColorLight1"] = Mix(accent, Colors.White, 0.15);
        resources["SystemAccentColorLight2"] = Mix(accent, Colors.White, 0.30);
        resources["SystemAccentColorLight3"] = Mix(accent, Colors.White, 0.45);
        resources["SystemAccentColorDark1"]  = Mix(accent, Colors.Black, 0.15);
        resources["SystemAccentColorDark2"]  = Mix(accent, Colors.Black, 0.30);
        resources["SystemAccentColorDark3"]  = Mix(accent, Colors.Black, 0.45);
    }

    /// <summary>From <paramref name="from"/> towards <paramref name="to"/> (0 = from, 1 = to).</summary>
    private static Color Mix(Color from, Color to, double amount) => Color.FromRgb(
        (byte)Math.Round(from.R + (to.R - from.R) * amount),
        (byte)Math.Round(from.G + (to.G - from.G) * amount),
        (byte)Math.Round(from.B + (to.B - from.B) * amount));
}
