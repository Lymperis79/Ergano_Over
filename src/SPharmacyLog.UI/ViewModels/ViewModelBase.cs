using System.ComponentModel;
using System.Runtime.CompilerServices;
using SPharmacyLog.UI.Localization;

namespace SPharmacyLog.UI.ViewModels;

public abstract class ViewModelBase : INotifyPropertyChanged
{
    protected ViewModelBase()
    {
        Loc = Tr.Loc;
        Loc.LanguageChanged += (_, _) => OnPropertyChanged(string.Empty);
    }

    public ILocalizationService Loc { get; }
    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}