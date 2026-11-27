namespace SPharmacyLog.UI.Services;

public sealed class ApplicationSettings
{
    // Preserve the existing calendar behaviour for installations that do not yet have this setting.
    public bool ShowOfficialHolidays { get; set; } = true;
}

public static class ApplicationSettingsService
{
    private static readonly string Folder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SPharmacyLog");
    private static readonly string SettingsPath = Path.Combine(Folder, "application.json");

    public static ApplicationSettings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
                return System.Text.Json.JsonSerializer.Deserialize<ApplicationSettings>(File.ReadAllText(SettingsPath))
                    ?? new ApplicationSettings();
        }
        catch { }
        return new ApplicationSettings();
    }

    public static void Save(ApplicationSettings settings)
    {
        Directory.CreateDirectory(Folder);
        File.WriteAllText(SettingsPath, System.Text.Json.JsonSerializer.Serialize(settings,
            new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
    }
}
