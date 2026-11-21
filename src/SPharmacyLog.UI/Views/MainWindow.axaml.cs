using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Microsoft.EntityFrameworkCore;
using SPharmacyLog.Data;
using SPharmacyLog.Data.Entities;
using SPharmacyLog.UI.Localization;
using SPharmacyLog.UI.ViewModels;

namespace SPharmacyLog.UI.Views;

public partial class MainWindow : Window
{
    private readonly MainWindowViewModel viewModel;
    private ComboBox languageBox = null!;
    private readonly ContentControl pageHost;
    private TextBlock dailySummary = new();
    private ListBox dayRecords = new();
    private StackPanel calendarPanel = new();
    private DateTime displayedMonth = new(DateTime.Today.Year, DateTime.Today.Month, 1);

    public MainWindow(PharmacyUser user)
    {
        InitializeComponent();
        languageBox = this.FindControl<ComboBox>("LanguageBox")
            ?? throw new InvalidOperationException("LanguageBox was not found in MainWindow.axaml. Check the XAML control name and build action.");
        viewModel = new MainWindowViewModel(user);
        DataContext = viewModel;
        UserLabel.Text = viewModel.UserDisplay;
        Tr.Loc.LanguageChanged += (_, _) => ApplyTranslations();
        ApplyTranslations();
        pageHost = PageHost;

        var navigation = new (string Label, Action Action)[]
        {
            (Tr.Loc["Navigation.ExpiryCalendar"], ShowExpiryCalendar),
            (Tr.Loc["Navigation.Customers"], () => ShowPlaceholder(Tr.Loc["Navigation.Customers"])),
            (Tr.Loc["Navigation.Borrowing"], () => ShowPlaceholder(Tr.Loc["Navigation.Borrowing"])),
            (Tr.Loc["Navigation.Subscriptions"], () => ShowPlaceholder(Tr.Loc["Navigation.Subscriptions"])),
            (Tr.Loc["Navigation.Payments"], () => ShowPlaceholder(Tr.Loc["Navigation.Payments"])),
            (Tr.Loc["Navigation.Users"], () => ShowPlaceholder(Tr.Loc["Navigation.Users"]))
        };

        foreach (var item in navigation)
        {
            var button = new Button { Content = item.Label, Classes = { "nav" }, MinWidth = 190 };
            button.Click += (_, _) => item.Action();
            if (item.Label == Tr.Loc["Navigation.Users"] && user.Role != PharmacyRole.Admin)
                button.IsVisible = false;
            NavigationPanel.Children.Add(button);
        }

        ShowExpiryCalendar();
    }

    private void ApplyTranslations()
    {
        Title = Tr.Loc["App.Title"];
        if (languageBox is not null)
            languageBox.SelectedIndex = Tr.Loc.CurrentLanguage == AppLanguage.Greek ? 1 : 0;
    }

    private void OnLanguageChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (languageBox?.SelectedIndex == 1) Tr.Loc.SetLanguage(AppLanguage.Greek);
        else if (languageBox?.SelectedIndex == 0) Tr.Loc.SetLanguage(AppLanguage.English);
    }

    private void ShowExpiryCalendar()
    {
        // Recreate these controls whenever the calendar page is opened. Avalonia
        // controls can only have one visual parent, and the previous page content
        // may still own the controls from the last calendar instance.
        calendarPanel = new StackPanel();
        dailySummary = new TextBlock();
        dayRecords = new ListBox();

        var content = new StackPanel { Margin = new Thickness(22), Spacing = 12 };
        content.Children.Add(new TextBlock { Text = Tr.Loc["Calendar.Title"], FontSize = 24, FontWeight = FontWeight.Bold, Margin = new Thickness(0, 0, 0, 4) });
        content.Children.Add(new TextBlock { Text = Tr.Loc["Calendar.Description"] });

        var monthBar = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Spacing = 10 };
        var previous = new Button { Content = "‹" };
        var monthTitle = new TextBlock { FontSize = 18, MinWidth = 160, VerticalAlignment = VerticalAlignment.Center, TextAlignment = TextAlignment.Center };
        var next = new Button { Content = "›" };

        previous.Click += async (_, _) => { displayedMonth = displayedMonth.AddMonths(-1); await RenderCalendarAsync(monthTitle); };
        next.Click += async (_, _) => { displayedMonth = displayedMonth.AddMonths(1); await RenderCalendarAsync(monthTitle); };

        monthBar.Children.Add(previous);
        monthBar.Children.Add(monthTitle);
        monthBar.Children.Add(next);
        content.Children.Add(monthBar);

        var weekdayGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*,*,*,*,*,*") };
        string[] weekdays = { "Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun" };
        for (var i = 0; i < weekdays.Length; i++)
        {
            var label = new TextBlock { Text = weekdays[i], HorizontalAlignment = HorizontalAlignment.Center };
            Grid.SetColumn(label, i);
            weekdayGrid.Children.Add(label);
        }
        content.Children.Add(weekdayGrid);

        calendarPanel.Children.Clear();
        content.Children.Add(calendarPanel);
        dayRecords.MaxHeight = 260;
        content.Children.Add(dailySummary);
        content.Children.Add(dayRecords);
        pageHost.Content = content;
        _ = RenderCalendarAsync(monthTitle);
    }

    private async Task RenderCalendarAsync(TextBlock monthTitle)
    {
        monthTitle.Text = displayedMonth.ToString("MMMM yyyy");
        await using var db = new PharmacyDbContext(PharmacyDb.Options());
        var start = displayedMonth;
        var end = displayedMonth.AddMonths(1);

        var expiryCounts = await db.Prescriptions
            .Where(p => p.Status == PrescriptionStatus.Active && p.ExpiryDate >= start && p.ExpiryDate < end)
            .GroupBy(p => p.ExpiryDate.Date)
            .Select(g => new { Date = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Date, x => x.Count);

        calendarPanel.Children.Clear();
        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,*,*,*,*,*,*"),
            RowDefinitions = new RowDefinitions("Auto,Auto,Auto,Auto,Auto,Auto")
        };

        var offset = ((int)displayedMonth.DayOfWeek + 6) % 7;
        var daysInMonth = DateTime.DaysInMonth(displayedMonth.Year, displayedMonth.Month);

        for (var cell = 0; cell < 42; cell++)
        {
            var dayNumber = cell - offset + 1;
            var button = new Button { MinHeight = 70, Padding = new Thickness(5), HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Center, Margin = new Thickness(3) };
            if (dayNumber is >= 1 && dayNumber <= daysInMonth)
            {
                var date = new DateTime(displayedMonth.Year, displayedMonth.Month, dayNumber);
                var count = expiryCounts.GetValueOrDefault(date);
                button.Content = new StackPanel
                {
                    Spacing = 1,
                    Children =
                    {
                        new TextBlock { Text = dayNumber.ToString(), HorizontalAlignment = HorizontalAlignment.Center, FontWeight = date == SelectedCalendarDate ? FontWeight.Bold : FontWeight.Normal },
                        new TextBlock { Text = count > 0 ? $"{count} {Tr.Loc["Calendar.Expiring"]}" : Tr.Loc["Calendar.None"], FontSize = 11, Foreground = count > 0 ? Brushes.Orange : Brushes.Gray, HorizontalAlignment = HorizontalAlignment.Center }
                    }
                };
                if (date == SelectedCalendarDate) { button.Background = new SolidColorBrush(Color.Parse("#243C52")); button.BorderBrush = new SolidColorBrush(Color.Parse("#21A1D8")); button.BorderThickness = new Thickness(2); }
                button.Click += async (_, _) => { SelectedCalendarDate = date; await RenderCalendarAsync(monthTitle); await LoadSelectedDayAsync(); };
            }
            else button.IsEnabled = false;
            Grid.SetColumn(button, cell % 7);
            Grid.SetRow(button, cell / 7);
            grid.Children.Add(button);
        }

        calendarPanel.Children.Add(grid);
        await LoadSelectedDayAsync();
    }

    private DateTime SelectedCalendarDate { get; set; } = DateTime.Today;

    private async Task LoadSelectedDayAsync()
    {
        await using var db = new PharmacyDbContext(PharmacyDb.Options());
        var records = await db.Prescriptions.Include(p => p.Customer)
            .Where(p => p.Status == PrescriptionStatus.Active && p.ExpiryDate >= SelectedCalendarDate.Date && p.ExpiryDate < SelectedCalendarDate.Date.AddDays(1))
            .OrderBy(p => p.Customer!.FullName).ToListAsync();

        dayRecords.ItemsSource = records.Select(p =>
            $"{p.Customer?.CustomerNumber} | {p.Customer?.FullName} | {p.MedicineName} | Rx {p.PrescriptionNumber ?? "—"} | expires {p.ExpiryDate:dd/MM/yyyy}").ToList();
        dailySummary.Text = string.Format(Tr.Loc["Calendar.DaySummary"], SelectedCalendarDate.ToString("dddd, dd MMM yyyy"), records.Count);
        dailySummary.FontWeight = FontWeight.SemiBold;
    }

    private void ShowPlaceholder(string title)
    {
        pageHost.Content = new StackPanel
        {
            Margin = new Thickness(20),
            Children = { new TextBlock { Text = title, FontSize = 24, FontWeight = FontWeight.Bold }, new TextBlock { Text = Tr.Loc["Common.Queued"] } }
        };
    }

}