using CommunityToolkit.Mvvm.ComponentModel;

namespace ErganiManager.UI.ViewModels;

/// <summary>One editable row of the rounding table in Administration.</summary>
public partial class RoundingBandEdit : ObservableObject
{
    [ObservableProperty] private decimal? _from;
    [ObservableProperty] private decimal? _to;
    [ObservableProperty] private decimal? _roundTo;

    public RoundingBandEdit(int from, int to, int roundTo)
    {
        _from = from;
        _to = to;
        _roundTo = roundTo;
    }
}
