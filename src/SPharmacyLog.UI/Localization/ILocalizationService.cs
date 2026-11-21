using System.ComponentModel;

namespace SPharmacyLog.UI.Localization;

public interface ILocalizationService : INotifyPropertyChanged
{
    AppLanguage CurrentLanguage { get; }
    string this[string key] { get; }
    void SetLanguage(AppLanguage language);
    event EventHandler? LanguageChanged;
}