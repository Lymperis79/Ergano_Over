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
    private readonly Dictionary<string, TextBox> customerFields = new();
    private ListBox customerList = null!;
    private TextBox customerSearch = null!;
    private TextBlock customerStatus = null!;
    private int? selectedCustomerId;

    public MainWindow(PharmacyUser user)
    {
        InitializeComponent();
        languageBox = this.FindControl<ComboBox>("LanguageBox") ?? throw new InvalidOperationException("LanguageBox was not found in MainWindow.axaml.");
        viewModel = new MainWindowViewModel(user);
        DataContext = viewModel;
        UserLabel.Text = viewModel.UserDisplay;
        Tr.Loc.LanguageChanged += (_, _) => ApplyTranslations();
        ApplyTranslations();
        pageHost = PageHost;

        var navigation = new (string Label, Action Action)[]
        {
            (Tr.Loc["Navigation.ExpiryCalendar"], ShowExpiryCalendar),
            (Tr.Loc["Navigation.Customers"], ShowCustomers),
            (Tr.Loc["Navigation.Borrowing"], () => ShowPlaceholder(Tr.Loc["Navigation.Borrowing"])),
            (Tr.Loc["Navigation.Subscriptions"], () => ShowPlaceholder(Tr.Loc["Navigation.Subscriptions"])),
            (Tr.Loc["Navigation.Payments"], () => ShowPlaceholder(Tr.Loc["Navigation.Payments"])),
            (Tr.Loc["Navigation.Users"], () => ShowPlaceholder(Tr.Loc["Navigation.Users"]))
        };
        foreach (var item in navigation)
        {
            var button = new Button { Content = item.Label, Classes = { "nav" }, MinWidth = 190 };
            button.Click += (_, _) => item.Action();
            if (item.Label == Tr.Loc["Navigation.Users"] && user.Role != PharmacyRole.Admin) button.IsVisible = false;
            NavigationPanel.Children.Add(button);
        }
        ShowExpiryCalendar();
    }

    private void ApplyTranslations()
    {
        Title = Tr.Loc["App.Title"];
        if (languageBox is not null) languageBox.SelectedIndex = Tr.Loc.CurrentLanguage == AppLanguage.Greek ? 1 : 0;
    }

    private void OnLanguageChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (languageBox?.SelectedIndex == 1) Tr.Loc.SetLanguage(AppLanguage.Greek);
        else if (languageBox?.SelectedIndex == 0) Tr.Loc.SetLanguage(AppLanguage.English);
    }

    private void ShowExpiryCalendar()
    {
        calendarPanel = new StackPanel();
        dailySummary = new TextBlock();
        dayRecords = new ListBox();
        var content = new StackPanel { Margin = new Thickness(22), Spacing = 12 };
        content.Children.Add(new TextBlock { Text = Tr.Loc["Calendar.Title"], FontSize = 24, FontWeight = FontWeight.Bold });
        content.Children.Add(new TextBlock { Text = Tr.Loc["Calendar.Description"] });
        var monthBar = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Spacing = 10 };
        var previous = new Button { Content = "‹" };
        var monthTitle = new TextBlock { FontSize = 18, MinWidth = 160, VerticalAlignment = VerticalAlignment.Center, TextAlignment = TextAlignment.Center };
        var next = new Button { Content = "›" };
        previous.Click += async (_, _) => { displayedMonth = displayedMonth.AddMonths(-1); await RenderCalendarAsync(monthTitle); };
        next.Click += async (_, _) => { displayedMonth = displayedMonth.AddMonths(1); await RenderCalendarAsync(monthTitle); };
        monthBar.Children.Add(previous); monthBar.Children.Add(monthTitle); monthBar.Children.Add(next);
        content.Children.Add(monthBar);
        var weekdayGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*,*,*,*,*,*") };
        string[] weekdays = { "Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun" };
        for (var i = 0; i < weekdays.Length; i++) { var label = new TextBlock { Text = weekdays[i], HorizontalAlignment = HorizontalAlignment.Center }; Grid.SetColumn(label, i); weekdayGrid.Children.Add(label); }
        content.Children.Add(weekdayGrid);
        content.Children.Add(calendarPanel);
        dayRecords.MaxHeight = 260;
        content.Children.Add(dailySummary); content.Children.Add(dayRecords);
        pageHost.Content = content;
        _ = RenderCalendarAsync(monthTitle);
    }

    private async Task RenderCalendarAsync(TextBlock monthTitle)
    {
        monthTitle.Text = displayedMonth.ToString("MMMM yyyy");
        await using var db = new PharmacyDbContext(PharmacyDb.Options());
        var start = displayedMonth; var end = displayedMonth.AddMonths(1);
        var expiryCounts = await db.Prescriptions.Where(p => p.Status == PrescriptionStatus.Active && p.ExpiryDate >= start && p.ExpiryDate < end)
            .GroupBy(p => p.ExpiryDate.Date).Select(g => new { Date = g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Date, x => x.Count);
        calendarPanel.Children.Clear();
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*,*,*,*,*,*"), RowDefinitions = new RowDefinitions("Auto,Auto,Auto,Auto,Auto,Auto") };
        var offset = ((int)displayedMonth.DayOfWeek + 6) % 7; var daysInMonth = DateTime.DaysInMonth(displayedMonth.Year, displayedMonth.Month);
        for (var cell = 0; cell < 42; cell++)
        {
            var dayNumber = cell - offset + 1;
            var button = new Button { MinHeight = 70, Padding = new Thickness(5), HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Center, Margin = new Thickness(3) };
            if (dayNumber >= 1 && dayNumber <= daysInMonth)
            {
                var date = new DateTime(displayedMonth.Year, displayedMonth.Month, dayNumber); var count = expiryCounts.GetValueOrDefault(date);
                button.Content = new StackPanel { Spacing = 1, Children = { new TextBlock { Text = dayNumber.ToString(), HorizontalAlignment = HorizontalAlignment.Center }, new TextBlock { Text = count > 0 ? $"{count} {Tr.Loc["Calendar.Expiring"]}" : Tr.Loc["Calendar.None"], FontSize = 11, Foreground = count > 0 ? Brushes.Orange : Brushes.Gray, HorizontalAlignment = HorizontalAlignment.Center } } };
                if (date == SelectedCalendarDate) { button.Background = new SolidColorBrush(Color.Parse("#243C52")); button.BorderBrush = new SolidColorBrush(Color.Parse("#21A1D8")); button.BorderThickness = new Thickness(2); }
                button.Click += async (_, _) => { SelectedCalendarDate = date; await RenderCalendarAsync(monthTitle); await LoadSelectedDayAsync(); };
            }
            else button.IsEnabled = false;
            Grid.SetColumn(button, cell % 7); Grid.SetRow(button, cell / 7); grid.Children.Add(button);
        }
        calendarPanel.Children.Add(grid); await LoadSelectedDayAsync();
    }

    private DateTime SelectedCalendarDate { get; set; } = DateTime.Today;

    private async Task LoadSelectedDayAsync()
    {
        await using var db = new PharmacyDbContext(PharmacyDb.Options());
        var records = await db.Prescriptions.Include(p => p.Customer).Where(p => p.Status == PrescriptionStatus.Active && p.ExpiryDate >= SelectedCalendarDate.Date && p.ExpiryDate < SelectedCalendarDate.Date.AddDays(1))
            .OrderBy(p => p.Customer!.FullName).ToListAsync();
        dayRecords.ItemsSource = records.Select(p => $"{p.Customer?.CustomerNumber} | {p.Customer?.FullName} | {p.MedicineName} | Rx {p.PrescriptionNumber ?? "—"} | expires {p.ExpiryDate:dd/MM/yyyy}").ToList();
        dailySummary.Text = string.Format(Tr.Loc["Calendar.DaySummary"], SelectedCalendarDate.ToString("dddd, dd MMM yyyy"), records.Count);
        dailySummary.FontWeight = FontWeight.SemiBold;
    }

    private void ShowCustomers()
    {
        customerFields.Clear(); selectedCustomerId = null;
        customerSearch = new TextBox { Watermark = Tr.Loc["Customers.Search"], Margin = new Thickness(0, 0, 0, 10) };
        customerList = new ListBox { MinWidth = 300 };
        customerStatus = new TextBlock { Foreground = Brushes.LightGreen, Margin = new Thickness(0, 8, 0, 0) };
        customerSearch.TextChanged += async (_, _) => await SearchCustomersAsync();
        customerList.SelectionChanged += async (_, _) => { if (customerList.SelectedItem is CustomerListItem item) await LoadCustomerAsync(item.Id); };
        var left = new StackPanel { Spacing = 8 };
        left.Children.Add(new TextBlock { Text = Tr.Loc["Navigation.Customers"], FontSize = 18, FontWeight = FontWeight.Bold });
        left.Children.Add(customerSearch); left.Children.Add(customerList);
        var newButton = new Button { Content = Tr.Loc["Customers.New"], Classes = { "accent" }, HorizontalAlignment = HorizontalAlignment.Stretch };
        newButton.Click += (_, _) => ClearCustomerForm(); left.Children.Add(newButton);

        var fieldsGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), RowDefinitions = new RowDefinitions("Auto,Auto,Auto,Auto,Auto,Auto,Auto") };
        AddCustomerField(fieldsGrid, "Customer number", "CustomerNumber", 0, 0);
        AddCustomerField(fieldsGrid, "Name", "FirstName", 0, 1);
        AddCustomerField(fieldsGrid, "Surname", "Surname", 1, 0);
        AddCustomerField(fieldsGrid, "Address", "Address", 1, 1);
        AddCustomerField(fieldsGrid, "City", "City", 2, 0);
        AddCustomerField(fieldsGrid, "Postcode", "Postcode", 2, 1);
        AddCustomerField(fieldsGrid, "Telephone", "Telephone", 3, 0);
        AddCustomerField(fieldsGrid, "Mobile", "Mobile", 3, 1);
        AddCustomerField(fieldsGrid, "Email", "Email", 4, 0);
        AddCustomerField(fieldsGrid, "AMKA", "Amka", 4, 1);
        AddCustomerField(fieldsGrid, "Insurance", "Insurance", 5, 0);
        AddCustomerField(fieldsGrid, "Notes", "Notes", 6, 0, 2, true);
        var save = new Button { Content = Tr.Loc["Customers.Save"], Classes = { "accent" }, MinWidth = 150 };
        save.Click += async (_, _) => await SaveCustomerAsync();
        var clear = new Button { Content = Tr.Loc["Customers.Clear"], MinWidth = 100 }; clear.Click += (_, _) => ClearCustomerForm();
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Margin = new Thickness(0, 14, 0, 0) };
        actions.Children.Add(save); actions.Children.Add(clear);
        var right = new StackPanel { Margin = new Thickness(20, 0, 12, 0), Spacing = 10 };
        right.Children.Add(new TextBlock { Text = Tr.Loc["Customers.Details"], FontSize = 18, FontWeight = FontWeight.Bold });
        right.Children.Add(fieldsGrid); right.Children.Add(actions); right.Children.Add(customerStatus);
        var layout = new Grid { ColumnDefinitions = new ColumnDefinitions("320,*"), Margin = new Thickness(22) };
        layout.Children.Add(left); Grid.SetColumn(right, 1); layout.Children.Add(right);
        pageHost.Content = layout; _ = SearchCustomersAsync(); ClearCustomerForm();
    }

    private void AddCustomerField(Grid grid, string label, string key, int row, int column, int columnSpan = 1, bool multiline = false)
    {
        var panel = new StackPanel { Spacing = 4, Margin = new Thickness(5) };
        panel.Children.Add(new TextBlock { Text = label, Foreground = Brushes.LightGray, FontSize = 12 });
        var box = new TextBox { Watermark = label, MinHeight = multiline ? 75 : 34, AcceptsReturn = multiline, TextWrapping = multiline ? TextWrapping.Wrap : TextWrapping.NoWrap };
        customerFields[key] = box; panel.Children.Add(box); Grid.SetRow(panel, row); Grid.SetColumn(panel, column); Grid.SetColumnSpan(panel, columnSpan); grid.Children.Add(panel);
    }

    private async Task SearchCustomersAsync()
    {
        if (customerList is null) return;
        var term = customerSearch.Text?.Trim() ?? "";
        await using var db = new PharmacyDbContext(PharmacyDb.Options());
        var query = db.Customers.AsNoTracking().Where(c => c.IsActive);
        if (!string.IsNullOrWhiteSpace(term))
            query = query.Where(c => c.CustomerNumber.Contains(term) || c.FullName.Contains(term)
                || (c.FirstName != null && c.FirstName.Contains(term)) || (c.Surname != null && c.Surname.Contains(term))
                || (c.Amka != null && c.Amka.Contains(term)) || (c.Mobile != null && c.Mobile.Contains(term))
                || c.Phone.Contains(term) || (c.Telephone != null && c.Telephone.Contains(term)) || (c.Email != null && c.Email.Contains(term)));
        var rows = await query.OrderBy(c => c.Surname ?? c.FullName).ThenBy(c => c.FirstName).Take(250).ToListAsync();
        customerList.ItemsSource = rows.Select(c => new CustomerListItem(c.Id, $"{c.CustomerNumber} | {(string.IsNullOrWhiteSpace(c.FullName) ? $"{c.FirstName} {c.Surname}" : c.FullName)}")).ToList();
    }

    private async Task LoadCustomerAsync(int id)
    {
        await using var db = new PharmacyDbContext(PharmacyDb.Options());
        var c = await db.Customers.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id); if (c is null) return;
        selectedCustomerId = c.Id; var parts = SplitLegacyName(c);
        SetField("CustomerNumber", c.CustomerNumber); SetField("FirstName", c.FirstName ?? parts.First); SetField("Surname", c.Surname ?? parts.Last);
        SetField("Address", c.Address); SetField("City", c.City); SetField("Postcode", c.Postcode);
        SetField("Telephone", c.Telephone ?? c.Phone); SetField("Mobile", c.Mobile); SetField("Email", c.Email);
        SetField("Amka", c.Amka); SetField("Insurance", c.Insurance); SetField("Notes", c.Notes);
        customerStatus.Text = string.Format(Tr.Loc["Customers.Editing"], c.CustomerNumber);
    }

    private void ClearCustomerForm()
    {
        selectedCustomerId = null; foreach (var field in customerFields.Values) field.Text = "";
        customerStatus.Text = Tr.Loc["Customers.NewStatus"]; customerStatus.Foreground = Brushes.LightGreen;
        if (customerList is not null) customerList.SelectedItem = null;
    }

    private void SetField(string key, string? value) { if (customerFields.TryGetValue(key, out var field)) field.Text = value ?? ""; }

    private async Task SaveCustomerAsync()
    {
        var number = GetField("CustomerNumber"); var first = GetField("FirstName"); var surname = GetField("Surname");
        if (string.IsNullOrWhiteSpace(number) || string.IsNullOrWhiteSpace(first) || string.IsNullOrWhiteSpace(surname))
        { customerStatus.Foreground = Brushes.Orange; customerStatus.Text = Tr.Loc["Customers.Required"]; return; }
        await using var db = new PharmacyDbContext(PharmacyDb.Options());
        if (await db.Customers.AnyAsync(c => c.CustomerNumber == number && c.Id != (selectedCustomerId ?? 0)))
        { customerStatus.Foreground = Brushes.Orange; customerStatus.Text = Tr.Loc["Customers.Duplicate"]; return; }
        var customer = selectedCustomerId.HasValue ? await db.Customers.FirstOrDefaultAsync(c => c.Id == selectedCustomerId.Value) : new Customer();
        if (customer is null) return;
        customer.CustomerNumber = number; customer.FirstName = first; customer.Surname = surname; customer.FullName = $"{first} {surname}".Trim();
        customer.Address = GetField("Address"); customer.City = GetField("City"); customer.Postcode = GetField("Postcode");
        customer.Telephone = GetField("Telephone"); customer.Phone = customer.Telephone ?? ""; customer.Mobile = GetField("Mobile");
        customer.Email = GetField("Email"); customer.Amka = GetField("Amka"); customer.Insurance = GetField("Insurance"); customer.Notes = GetField("Notes");
        customer.IsActive = true; if (!selectedCustomerId.HasValue) db.Customers.Add(customer);
        await db.SaveChangesAsync(); selectedCustomerId = customer.Id;
        customerStatus.Foreground = Brushes.LightGreen; customerStatus.Text = string.Format(Tr.Loc["Customers.Saved"], customer.CustomerNumber);
        await SearchCustomersAsync();
    }

    private static (string First, string Last) SplitLegacyName(Customer c)
    {
        if (!string.IsNullOrWhiteSpace(c.FirstName) || !string.IsNullOrWhiteSpace(c.Surname)) return (c.FirstName ?? "", c.Surname ?? "");
        var parts = (c.FullName ?? "").Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        return (parts.FirstOrDefault() ?? "", parts.Length > 1 ? parts[1] : "");
    }

    private string GetField(string key) => customerFields.TryGetValue(key, out var field) ? field.Text?.Trim() ?? "" : "";
    private sealed record CustomerListItem(int Id, string Display) { public override string ToString() => Display; }

    private void ShowPlaceholder(string title)
    {
        pageHost.Content = new StackPanel { Margin = new Thickness(20), Children = { new TextBlock { Text = title, FontSize = 24, FontWeight = FontWeight.Bold }, new TextBlock { Text = Tr.Loc["Common.Queued"] } } };
    }
}