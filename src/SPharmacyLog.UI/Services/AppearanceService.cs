using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Avalonia;
using Avalonia.Media;

namespace SPharmacyLog.UI.Services;

/// <summary>Appearance presets and accent palette modeled after ErganiManager.</summary>
public sealed record ThemePreset(string Key, string Window, string Sidebar, string Panel, string Card, string Hover, string Line);

public static class AppearanceService
{
    public const string DefaultTheme = "DarkBlue";
    public const string DefaultAccent = "#21A1D8";

    public static readonly IReadOnlyList<ThemePreset> Presets = new[]
    {
        new ThemePreset("DarkBlue", "#151922", "#11151C", "#202733", "#202733", "#293342", "#354152"),
        new ThemePreset("Graphite", "#1C1C1C", "#141414", "#232323", "#262626", "#2E2E2E", "#424242"),
        new ThemePreset("Forest", "#17201B", "#101813", "#1B2A21", "#1E2D24", "#26382D", "#35503F"),
        new ThemePreset("Purple", "#1F1A26", "#16111C", "#271F31", "#2A2235", "#33293F", "#4B3C5E"),
        new ThemePreset("Wine", "#261A1C", "#1B1113", "#2F2023", "#332326", "#3D2B2F", "#5A3C41"),
        new ThemePreset("Midnight", "#0B0B0F", "#050508", "#101016", "#13131A", "#1B1B24", "#2B2B38")
    };

    public static readonly IReadOnlyList<string> AccentColors = new[]
    {
        "#21A1D8", "#26A69A", "#43A047", "#FB8C00", "#E53935", "#8E24AA", "#D81B60", "#8D6E63"
    };

    public static string Theme { get; private set; } = DefaultTheme;
    public static string Accent { get; private set; } = DefaultAccent;

    private sealed class Stored { public string Theme { get; set; } = DefaultTheme; public string Accent { get; set; } = DefaultAccent; }
    private static string Folder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SPharmacyLog");
    private static string FilePath => Path.Combine(Folder, "appearance.json");

    public static void Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return;
            var data = JsonSerializer.Deserialize<Stored>(File.ReadAllText(FilePath));
            if (data == null) return;
            if (Presets.Any(p => p.Key == data.Theme)) Theme = data.Theme;
            if (Color.TryParse(data.Accent, out _)) Accent = data.Accent;
        }
        catch { }
    }

    public static void Choose(string? theme = null, string? accent = null)
    {
        if (theme != null && Presets.Any(p => p.Key == theme)) Theme = theme;
        if (accent != null && Color.TryParse(accent, out _)) Accent = accent;
        Apply();
        Save();
    }

    public static void Reset() => Choose(DefaultTheme, DefaultAccent);

    private static void Save()
    {
        try { Directory.CreateDirectory(Folder); File.WriteAllText(FilePath, JsonSerializer.Serialize(new Stored { Theme = Theme, Accent = Accent })); }
        catch { }
    }

    public static void Apply()
    {
        var app = Application.Current;
        if (app == null) return;
        var preset = Presets.First(p => p.Key == Theme);
        var accent = Color.Parse(Accent);
        void Set(string key, Color color) => app.Resources[key] = new SolidColorBrush(color);
        Set("WindowBrush", Color.Parse(preset.Window));
        Set("SidebarBrush", Color.Parse(preset.Sidebar));
        Set("PanelBrush", Color.Parse(preset.Panel));
        Set("CardBrush", Color.Parse(preset.Card));
        Set("HoverBrush", Color.Parse(preset.Hover));
        Set("LineBrush", Color.Parse(preset.Line));
        Set("AccentBrush", accent);
        Set("AccentHoverBrush", Mix(accent, Colors.Black, .15));
        Set("AccentLightBrush", Mix(accent, Colors.White, .40));
        Set("AccentSoftBrush", Color.FromArgb(0x33, accent.R, accent.G, accent.B));
        Set("AccentFaintBrush", Color.FromArgb(0x1A, accent.R, accent.G, accent.B));
        var side = Color.Parse(preset.Sidebar);
        Set("NavActiveBrush", Mix(side, accent, .35));
        Set("NavActiveHoverBrush", Mix(side, accent, .45));
        app.Resources["SystemAccentColor"] = accent;
        app.Resources["SystemAccentColorLight1"] = Mix(accent, Colors.White, .15);
        app.Resources["SystemAccentColorLight2"] = Mix(accent, Colors.White, .30);
        app.Resources["SystemAccentColorLight3"] = Mix(accent, Colors.White, .45);
        app.Resources["SystemAccentColorDark1"] = Mix(accent, Colors.Black, .15);
        app.Resources["SystemAccentColorDark2"] = Mix(accent, Colors.Black, .30);
        app.Resources["SystemAccentColorDark3"] = Mix(accent, Colors.Black, .45);
    }

    private static Color Mix(Color from, Color to, double amount) => Color.FromRgb(
        (byte)Math.Round(from.R + (to.R - from.R) * amount),
        (byte)Math.Round(from.G + (to.G - from.G) * amount),
        (byte)Math.Round(from.B + (to.B - from.B) * amount));
}
