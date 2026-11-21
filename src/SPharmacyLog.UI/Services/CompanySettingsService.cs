using System.Text.Json;

namespace SPharmacyLog.UI.Services;

public sealed class CompanySettings
{
    public string Name { get; set; } = "";
    public string? LogoFile { get; set; }
}

public static class CompanySettingsService
{
    private static readonly string Folder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SPharmacyLog");
    private static readonly string SettingsPath = Path.Combine(Folder, "company.json");

    public static CompanySettings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
                return JsonSerializer.Deserialize<CompanySettings>(File.ReadAllText(SettingsPath)) ?? new CompanySettings();
        }
        catch { }
        return new CompanySettings();
    }

    public static CompanySettings Save(string name, byte[]? logoBytes, string? extension, bool removeLogo)
    {
        Directory.CreateDirectory(Folder);
        var settings = Load();
        settings.Name = name.Trim();

        if (removeLogo && !string.IsNullOrWhiteSpace(settings.LogoFile))
        {
            try { if (File.Exists(settings.LogoFile)) File.Delete(settings.LogoFile); } catch { }
            settings.LogoFile = null;
        }

        if (logoBytes is { Length: > 0 })
        {
            var safeExtension = extension?.ToLowerInvariant() switch
            {
                ".jpg" or ".jpeg" or ".png" or ".bmp" => extension.ToLowerInvariant(),
                _ => ".png"
            };
            var logoPath = Path.Combine(Folder, "company-logo" + safeExtension);
            File.WriteAllBytes(logoPath, logoBytes);
            settings.LogoFile = logoPath;
        }

        File.WriteAllText(SettingsPath, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
        return settings;
    }
}
