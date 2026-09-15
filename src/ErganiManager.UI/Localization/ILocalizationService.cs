using System;
using System.Collections.Generic;

namespace ErganiManager.UI.Localization;

public enum AppLanguage
{
    English,
    Greek
}

public interface ILocalizationService
{
    AppLanguage CurrentLanguage { get; }
    string this[string key] { get; }
    void SetLanguage(AppLanguage language);
    IReadOnlyList<AppLanguage> AvailableLanguages { get; }
    event EventHandler? LanguageChanged;
}
