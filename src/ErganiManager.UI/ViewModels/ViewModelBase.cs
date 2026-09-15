using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.DependencyInjection;

namespace ErganiManager.UI.ViewModels;

public abstract class ViewModelBase : ObservableObject
{
    /// <summary>
    /// Exposes the localization service to every ViewModel via a shorthand
    /// property. AXAML bindings use {Binding Loc[KeyName]} — the indexer
    /// on ILocalizationService returns the translated string for KeyName.
    /// </summary>
    public ILocalizationService Loc { get; }

    protected ViewModelBase()
    {
        Loc = Program.Services.GetRequiredService<ILocalizationService>();

        // Re-raise PropertyChanged for Loc whenever the language changes
        // so every {Binding Loc[xxx]} in the UI refreshes automatically.
        Loc.LanguageChanged += (_, _) => OnPropertyChanged(nameof(Loc));
    }
}
