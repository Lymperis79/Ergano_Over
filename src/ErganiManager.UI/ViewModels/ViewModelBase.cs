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

        // Raising PropertyChanged("Loc") alone isn't enough — Avalonia sees the same
        // ILocalizationService reference and skips re-evaluating the indexer.
        // Raising PropertyChanged(string.Empty) tells Avalonia ALL properties changed,
        // forcing every {Binding Loc[xxx]} across the entire ViewModel to refresh.
        Loc.LanguageChanged += (_, _) => OnPropertyChanged(string.Empty);
    }
}