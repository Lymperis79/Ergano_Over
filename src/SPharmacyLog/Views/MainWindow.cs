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
    private readonly DatePicker _expiryDate = new() { SelectedDate = DateTime.Today };
    private readonly ListBox _expiring = new();

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
        var panel = new StackPanel { Margin = new Thickness(18), Spacing = 12 };
        panel.Children.Add(new TextBlock { Text = "Prescription expiry calendar", FontSize = 24, FontWeight = FontWeight.Bold });
        panel.Children.Add(new TextBlock { Text = "Select a date to see prescriptions expiring that day. Active prescriptions are indexed by expiry date.", Foreground = Brushes.Gray });
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        var refresh = new Button { Content = "Show expiring on selected day" };
        refresh.Click += async (_, _) => await LoadExpiringAsync();
        row.Children.Add(_expiryDate); row.Children.Add(refresh);
        panel.Children.Add(row); panel.Children.Add(_status); panel.Children.Add(_expiring);
        _content.Content = panel;
        _ = LoadExpiringAsync();
    }

    private async Task LoadExpiringAsync()
    {
        var date = _expiryDate.SelectedDate?.Date ?? DateTime.Today;
        await using var db = new PharmacyDbContext(Program.DbOptions());
        var list = await db.Prescriptions.Include(p => p.Customer)
            .Where(p => p.ExpiryDate.Date == date && p.Status == PrescriptionStatus.Active)
            .OrderBy(p => p.Customer!.FullName).ThenBy(p => p.MedicineName).ToListAsync();
        _expiring.ItemsSource = list.Select(p => $"{p.Customer?.CustomerNumber} | {p.Customer?.FullName} | {p.MedicineName} | Rx: {p.PrescriptionNumber ?? "—"} | Expires: {p.ExpiryDate:dd/MM/yyyy}").ToList();
        _status.Text = $"{date:dddd, dd MMMM yyyy} — {list.Count} active prescription(s) expiring";
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