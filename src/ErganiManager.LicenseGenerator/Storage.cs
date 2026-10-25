using System.Text.Json;

namespace ErganiManager.LicenseGenerator;

internal sealed class Settings
{
    public string PrivateKeyPath { get; set; } = string.Empty;
    public bool Greek { get; set; } = true;

    private static string Folder
    {
        get
        {
            var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ErganiManager.LicenseGenerator");
            Directory.CreateDirectory(folder);
            return folder;
        }
    }

    public static string SettingsFile => Path.Combine(Folder, "settings.json");
    public static string HistoryFile => Path.Combine(Folder, "history.json");

    public static Settings Load()
    {
        try { return File.Exists(SettingsFile) ? JsonSerializer.Deserialize<Settings>(File.ReadAllText(SettingsFile)) ?? new Settings() : new Settings(); }
        catch { return new Settings(); }
    }

    public void Save()
    {
        try { File.WriteAllText(SettingsFile, JsonSerializer.Serialize(this)); } catch { }
    }
}

/// <summary>One license that was created (kept so a key can be sent again later).</summary>
internal sealed class IssuedLicense
{
    public DateTime IssuedAt { get; set; }
    public string Licensee { get; set; } = string.Empty;
    public string PcCode { get; set; } = string.Empty;
    /// <summary>0 = no limit (or locked to one PC), 1 = single user, 2+ = multi-user.</summary>
    public int Seats { get; set; }
    public string? Expires { get; set; }
    public string Modules { get; set; } = string.Empty;
    public string Key { get; set; } = string.Empty;
}

internal static class History
{
    public static List<IssuedLicense> Load()
    {
        try
        {
            return File.Exists(Settings.HistoryFile)
                ? JsonSerializer.Deserialize<List<IssuedLicense>>(File.ReadAllText(Settings.HistoryFile)) ?? new()
                : new();
        }
        catch { return new(); }
    }

    public static void Add(IssuedLicense entry)
    {
        var all = Load();
        all.Add(entry);
        try { File.WriteAllText(Settings.HistoryFile, JsonSerializer.Serialize(all, new JsonSerializerOptions { WriteIndented = true })); } catch { }
    }
}
