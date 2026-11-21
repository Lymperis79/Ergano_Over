using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Microsoft.EntityFrameworkCore;
using SPharmacyLog.Data;
using SPharmacyLog.Models;

namespace SPharmacyLog.Views;

public sealed class MainWindow : Window
{
    private readonly PharmacyUser _user;
    private readonly ContentControl _content = new();
    private readonly TextBlock _status = new();
    private readonly StackPanel _calendarGrid = new() { Spacing = 3 };
    private readonly ListBox _expiring = new();
    private DateTime _month = new(DateTime.Today.Year, DateTime.Today.Month, 1);
    private DateTime _selectedDate = DateTime.Today;
    private Dictionary<DateTime, int> _expiryCounts = new();

    public MainWindow(PharmacyUser user)
    {
        _user = user;
        Title = $"S Pharmacy Log — {_user.Username}";
        Width = 1120; Height = 760; MinWidth = 900; MinHeight = 600;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        var header = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, Margin = new Thickness(16) };
        header.Children.Add(new TextBlock { Text = "S Pharmacy Log", FontSize = 24, FontWeight = FontWeight.Bold, VerticalAlignment = VerticalAlignment.Center });
        header.Children.Add(new TextBlock { Text = $"User: {_user.Username} ({_user.Role})", VerticalAlignment = VerticalAlignment.Center, Foreground = Brushes.Gray });
        var menu = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        foreach (var (label, action) in new[] { ("Expiry calendar", ShowExpiryCalendar), ("Customers", ShowCustomers), ("Borrowing", ShowBorrowings), ("Subscriptions", ShowSubscriptions), ("Payments & ledger", ShowLedger), ("Users", ShowUsers) })
        {
            var btn = new Button { Content = label };
            btn.Click += (_, _) => action();
            if (_user.Role != PharmacyRole.Admin && label == "Users") btn.IsVisible = false;
            menu.Children.Add(btn);
        }
        var root = new DockPanel();
        DockPanel.SetDock(header, Dock.Top); root.Children.Add(header);
        DockPanel.SetDock(menu, Dock.Top); root.Children.Add(menu);
        root.Children.Add(_content);
        Content = root;
        ShowExpiryCalendar();
    }

    private void ShowExpiryCalendar()
    {
        var panel = new StackPanel { Margin = new Thickness(18), Spacing = 10 };
        panel.Children.Add(new TextBlock { Text = "Prescription expiry calendar", FontSize = 24, FontWeight = FontWeight.Bold });
        panel.Children.Add(new TextBlock { Text = "Each day displays the number of active prescriptions expiring on that date. Select a day to see the details.", Foreground = Brushes.Gray });
        var monthHeader = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Spacing = 12 };
        var prev = new Button { Content = "‹ Previous" };
        var next = new Button { Content = "Next ›" };
        var monthTitle = new TextBlock { FontSize = 18, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center, MinWidth = 180, TextAlignment = TextAlignment.Center };
        prev.Click += async (_, _) => { _month = _month.AddMonths(-1); await RenderCalendarAsync(monthTitle); };
        next.Click += async (_, _) => { _month = _month.AddMonths(1); await RenderCalendarAsync(monthTitle); };
        monthHeader.Children.Add(prev); monthHeader.Children.Add(monthTitle); monthHeader.Children.Add(next);
        panel.Children.Add(monthHeader);
        var weekHeader = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*,*,*,*,*,*") };
        var weekdays = new[] { "Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun" };
        for (int i = 0; i < weekdays.Length; i++)
        {
            var t = new TextBlock { Text = weekdays[i], HorizontalAlignment = HorizontalAlignment.Center, FontWeight = FontWeight.SemiBold, Margin = new Thickness(3) };
            Grid.SetColumn(t, i); weekHeader.Children.Add(t);
        }
        panel.Children.Add(weekHeader);
        _calendarGrid.Children.Clear();
        panel.Children.Add(_calendarGrid);
        _status.FontSize = 16;
        panel.Children.Add(_status);
        _expiring.MaxHeight = 250;
        panel.Children.Add(_expiring);
        _content.Content = panel;
        _ = RenderCalendarAsync(monthTitle);
    }

    private async Task RenderCalendarAsync(TextBlock title)
    {
        title.Text = _month.ToString("MMMM yyyy");
        await using var db = new PharmacyDbContext(Program.DbOptions());
        var monthStart = _month.Date;
        var monthEnd = monthStart.AddMonths(1);
        _expiryCounts = await db.Prescriptions.Where(p => p.Status == PrescriptionStatus.Active &&
                p.ExpiryDate >= monthStart && p.ExpiryDate < monthEnd)
            .GroupBy(p => p.ExpiryDate.Date).Select(g => new { Date = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Date, x => x.Count);

        _calendarGrid.Children.Clear();
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*,*,*,*,*,*"), RowDefinitions = new RowDefinitions("Auto,Auto,Auto,Auto,Auto,Auto") };
        var mondayOffset = ((int)_month.DayOfWeek + 6) % 7;
        var dayCount = DateTime.DaysInMonth(_month.Year, _month.Month);
        for (int cell = 0; cell < 42; cell++)
        {
            int day = cell - mondayOffset + 1;
            var cellStack = new StackPanel { Spacing = 2, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            var button = new Button { MinHeight = 58, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Center, Padding = new Thickness(4) };
            if (day >= 1 && day <= dayCount)
            {
                var date = new DateTime(_month.Year, _month.Month, day);
                var count = _expiryCounts.GetValueOrDefault(date);
                cellStack.Children.Add(new TextBlock { Text = day.ToString(), FontWeight = date == _selectedDate ? FontWeight.Bold : FontWeight.Normal, HorizontalAlignment = HorizontalAlignment.Center });
                cellStack.Children.Add(new TextBlock { Text = count > 0 ? $"⚠ {count} expiring" : "—", FontSize = 11, Foreground = count > 0 ? Brushes.Orange : Brushes.Gray, HorizontalAlignment = HorizontalAlignment.Center });
                button.Content = cellStack;
                button.Background = date == _selectedDate ? Brushes.SteelBlue : null;
                button.Click += async (_, _) => { _selectedDate = date; await RenderCalendarAsync(title); await LoadExpiringAsync(); };
            }
            else
            {
                button.IsEnabled = false;
                button.Opacity = 0.15;
            }
            Grid.SetColumn(button, cell % 7); Grid.SetRow(button, cell / 7);
            grid.Children.Add(button);
        }
        _calendarGrid.Children.Add(grid);
        await LoadExpiringAsync();
    }

    private async Task LoadExpiringAsync()
    {
        await using var db = new PharmacyDbContext(Program.DbOptions());
        var list = await db.Prescriptions.Include(p => p.Customer)
            .Where(p => p.ExpiryDate >= _selectedDate.Date && p.ExpiryDate < _selectedDate.Date.AddDays(1) && p.Status == PrescriptionStatus.Active)
            .OrderBy(p => p.Customer!.FullName).ThenBy(p => p.MedicineName).ToListAsync();
        _expiring.ItemsSource = list.Select(p => $"{p.Customer?.CustomerNumber} | {p.Customer?.FullName} | {p.MedicineName} | Rx: {p.PrescriptionNumber ?? "—"} | Expires: {p.ExpiryDate:dd/MM/yyyy}").ToList();
        _status.Text = $"{_selectedDate:dddd, dd MMMM yyyy} — {list.Count} active prescription(s) expiring";
        _status.FontWeight = FontWeight.SemiBold;
    }

    private void ShowCustomers() => ShowPlaceholder("Customers", "Customer register and search will be implemented next.");
    private void ShowBorrowings() => ShowPlaceholder("Medicine borrowing", "Barcode scan, loan tracking and return workflow will be implemented next.");
    private void ShowSubscriptions() => ShowPlaceholder("Subscriptions", "Recurring prescription/subscription records and upcoming delivery tracking will be implemented next.");
    private void ShowLedger() => ShowPlaceholder("Payments & ledger", "Payments, charges, balances and statements will be implemented next.");
    private void ShowUsers() => ShowPlaceholder("User management", "Admin-only user creation, roles and menu permissions will be implemented next.");

    private void ShowPlaceholder(string title, string text)
    {
        _content.Content = new StackPanel { Margin = new Thickness(24), Spacing = 12,
            Children = { new TextBlock { Text = title, FontSize = 24, FontWeight = FontWeight.Bold }, new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap } } };
    }
}