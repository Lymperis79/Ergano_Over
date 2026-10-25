using CommunityToolkit.Mvvm.ComponentModel;
using ErganiManager.UI.Localization;

namespace ErganiManager.UI.ViewModels;

/// <summary>
/// One entry of a code drop-down (Ergani Usertype, late reason f_aitiologia...). The stored value is the
/// two-digit <see cref="Code"/> (01/02/03); the UI shows a localized description.
/// </summary>
public sealed class LocalizedCodeOption : ObservableObject
{
    private readonly string _key;
    private readonly ILocalizationService _loc;

    public LocalizedCodeOption(string code, string key, ILocalizationService loc)
    {
        Code = code;
        _key = key;
        _loc = loc;
    }

    public string Code { get; }

    public string Display => $"{Code} - {_loc[_key]}";

    public void Refresh() => OnPropertyChanged(nameof(Display));

    public override string ToString() => Display;
}
