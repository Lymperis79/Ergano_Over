using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using ErganiManager.LocalCache;

namespace ErganiManager.UI.Localization;

public class LocalizationService : ILocalizationService
{
    private static readonly string SettingsPath =
        Path.Combine(AppPaths.GetAppDataFolder(), "language.json");

    private Dictionary<string, string> _current = EnglishStrings.Strings;

    public AppLanguage CurrentLanguage { get; private set; } = AppLanguage.English;

    public IReadOnlyList<AppLanguage> AvailableLanguages { get; } =
        new[] { AppLanguage.English, AppLanguage.Greek };

    public event EventHandler? LanguageChanged;

    public LocalizationService()
    {
        // Restore persisted language choice on startup
        try
        {
            if (File.Exists(SettingsPath))
            {
                var json = File.ReadAllText(SettingsPath);
                var saved = JsonSerializer.Deserialize<LanguageSettings>(json);
                if (saved != null && Enum.TryParse<AppLanguage>(saved.Language, out var lang))
                    ApplyLanguage(lang);
            }
        }
        catch
        {
            // Corrupt settings file — default to English silently
        }
    }

    public string this[string key]
    {
        get
        {
            if (_current.TryGetValue(key, out var value))
                return value;

            // Fallback to English if a key is missing in the selected language
            if (EnglishStrings.Strings.TryGetValue(key, out var fallback))
                return fallback;

            // Return the key name itself so missing strings are visible during dev
            return $"[{key}]";
        }
    }

    public void SetLanguage(AppLanguage language)
    {
        if (language == CurrentLanguage) return;
        ApplyLanguage(language);
        Persist(language);
        LanguageChanged?.Invoke(this, EventArgs.Empty);
    }

    private void ApplyLanguage(AppLanguage language)
    {
        CurrentLanguage = language;
        _current = language switch
        {
            AppLanguage.Greek   => GreekStrings.Strings,
            _                   => EnglishStrings.Strings
        };
    }

    private static void Persist(AppLanguage language)
    {
        try
        {
            var json = JsonSerializer.Serialize(new LanguageSettings { Language = language.ToString() });
            File.WriteAllText(SettingsPath, json);
        }
        catch { /* Non-critical — next launch defaults to English */ }
    }

    private class LanguageSettings
    {
        public string Language { get; set; } = nameof(AppLanguage.English);
    }
}
