using System.Text.Json;

namespace SPharmacyLog.UI;

/// <summary>
/// Central application branding. Change ApplicationName in application-settings.json
/// to rename the application without editing source code.
/// </summary>
internal static class ApplicationBranding
{
    private const string DefaultName = "PharmaPoint";
    private static readonly Lazy<string> configuredName = new(LoadName);

    public static string Name => configuredName.Value;

    private static string LoadName()
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "application-settings.json");
            if (!File.Exists(path)) return DefaultName;
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            if (document.RootElement.TryGetProperty("ApplicationName", out var value))
            {
                var name = value.GetString()?.Trim();
                if (!string.IsNullOrWhiteSpace(name)) return name;
            }
        }
        catch
        {
            // Optional settings must never prevent the application from starting.
        }
        return DefaultName;
    }
}
