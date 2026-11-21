using SPharmacyLog.Data.Entities;
using SPharmacyLog.UI.Localization;

namespace SPharmacyLog.UI.ViewModels;

public sealed class MainWindowViewModel : ViewModelBase
{
    public MainWindowViewModel(PharmacyUser user)
    {
        User = user;
        SelectedDate = DateTime.Today;
        DisplayedMonth = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
    }

    public PharmacyUser User { get; }
    public DateTime SelectedDate { get; private set; }
    public DateTime DisplayedMonth { get; private set; }
    public string UserDisplay => $"{User.Username} ({User.Role})";
    public string MonthTitle => DisplayedMonth.ToString("MMMM yyyy");

    public void SelectDate(DateTime date)
    {
        SelectedDate = date.Date;
        OnPropertyChanged(nameof(SelectedDate));
    }

    public void ChangeMonth(int offset)
    {
        DisplayedMonth = DisplayedMonth.AddMonths(offset);
        OnPropertyChanged(nameof(DisplayedMonth));
        OnPropertyChanged(nameof(MonthTitle));
    }

    public string DaySummary(int count) =>
        string.Format(Loc["Calendar.DaySummary"], SelectedDate.ToString("dddd, dd MMM yyyy"), count);
}