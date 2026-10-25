using System;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ErganiManager.UI.ViewModels;

/// <summary>Editable predefined employee shift in Administration.</summary>
public partial class ShiftEdit : ObservableObject
{
    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty] private TimeSpan? _startTime;
    [ObservableProperty] private TimeSpan? _endTime;

    public ShiftEdit(string name, TimeSpan startTime, TimeSpan endTime)
    {
        _name = name;
        _startTime = startTime;
        _endTime = endTime;
    }

    public override string ToString() => Name;
}
