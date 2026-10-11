using System.Collections.ObjectModel;
using ErganiManager.UI.Localization;

namespace ErganiManager.UI.ViewModels;

/// <summary>The three Ergani f_aitiologia (late reason) choices, shown as text instead of 001/002/003.</summary>
public static class AitiologiaOptions
{
    public static ObservableCollection<LocalizedCodeOption> Create(ILocalizationService loc)
    {
        var list = new ObservableCollection<LocalizedCodeOption>
        {
            new("001", L.AitiologiaPower,    loc),
            new("002", L.AitiologiaEmployer, loc),
            new("003", L.AitiologiaErgani,   loc)
        };
        loc.LanguageChanged += (_, _) => { foreach (var o in list) o.Refresh(); };
        return list;
    }
}
