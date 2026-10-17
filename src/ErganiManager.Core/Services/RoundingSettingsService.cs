using System.Text.Json;
using ErganiManager.Core.Interfaces;
using ErganiManager.LocalCache;

namespace ErganiManager.Core.Services;

public class RoundingSettingsService : IRoundingSettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    // Static on purpose: code that cannot take the service through DI (e.g. the Ergani submission
    // path reading the on-time window) must see the same, always up-to-date rules.
    private static readonly object Gate = new();
    private static RoundingRules? _shared;

    private static string FilePath => Path.Combine(AppPaths.GetAppDataFolder(), "rounding.json");

    /// <summary>The rules currently in effect, loaded from disk on first use.</summary>
    public static RoundingRules Active
    {
        get
        {
            lock (Gate)
            {
                if (_shared != null) return _shared;
                try
                {
                    if (File.Exists(FilePath))
                    {
                        var loaded = JsonSerializer.Deserialize<RoundingRules>(File.ReadAllText(FilePath));
                        if (loaded is { Bands.Count: > 0 }) return _shared = loaded;
                    }
                }
                catch { /* unreadable file: fall back to the defaults */ }
                return _shared = new RoundingRules();
            }
        }
    }

    public RoundingRules Current => Active;

    public void Save(RoundingRules rules)
    {
        lock (Gate)
        {
            File.WriteAllText(FilePath, JsonSerializer.Serialize(rules, JsonOptions));
            _shared = rules;
        }
    }
}
