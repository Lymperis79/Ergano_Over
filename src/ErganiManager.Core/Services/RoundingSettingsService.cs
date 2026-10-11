using System.Text.Json;
using ErganiManager.Core.Interfaces;
using ErganiManager.LocalCache;

namespace ErganiManager.Core.Services;

public class RoundingSettingsService : IRoundingSettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly object _gate = new();
    private RoundingRules? _current;

    private static string FilePath => Path.Combine(AppPaths.GetAppDataFolder(), "rounding.json");

    public RoundingRules Current
    {
        get
        {
            lock (_gate)
            {
                if (_current != null) return _current;
                try
                {
                    if (File.Exists(FilePath))
                    {
                        var loaded = JsonSerializer.Deserialize<RoundingRules>(File.ReadAllText(FilePath));
                        if (loaded is { Bands.Count: > 0 }) return _current = loaded;
                    }
                }
                catch { /* unreadable file: fall back to the defaults */ }
                return _current = new RoundingRules();
            }
        }
    }

    public void Save(RoundingRules rules)
    {
        lock (_gate)
        {
            File.WriteAllText(FilePath, JsonSerializer.Serialize(rules, JsonOptions));
            _current = rules;
        }
    }
}
