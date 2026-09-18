using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ErganiManager.UI.ViewModels;

public partial class LanguageSelectorViewModel : ViewModelBase
{
    public ObservableCollection<AppLanguage> AvailableLanguages { get; }

    [ObservableProperty]
    private AppLanguage _selectedLanguage;

    public LanguageSelectorViewModel()
    {
        AvailableLanguages = new ObservableCollection<AppLanguage>(Loc.AvailableLanguages);
        _selectedLanguage = Loc.CurrentLanguage;
    }

    partial void OnSelectedLanguageChanged(AppLanguage value)
    {
        if (value != Loc.CurrentLanguage)
            Loc.SetLanguage(value);
    }
}
