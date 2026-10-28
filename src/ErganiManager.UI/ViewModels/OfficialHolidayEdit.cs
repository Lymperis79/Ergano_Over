using CommunityToolkit.Mvvm.ComponentModel;

namespace ErganiManager.UI.ViewModels;

/// <summary>One official holiday row in the company administration screen.</summary>
public partial class OfficialHolidayEdit : ObservableObject
{
    public DateOnly Date { get; }
    public string Name { get; }
    public string DateLabel => Date.ToString("ddd, dd MMM yyyy");

    [ObservableProperty]
    private bool _isEnabled;

    public OfficialHolidayEdit(DateOnly date, string name)
    {
        Date = date;
        Name = name;
    }
}
