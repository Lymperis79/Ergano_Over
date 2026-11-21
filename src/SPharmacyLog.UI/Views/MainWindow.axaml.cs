using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Input;
using Avalonia.Media;
using Microsoft.EntityFrameworkCore;
using SPharmacyLog.Data;
using SPharmacyLog.Data.Entities;
using SPharmacyLog.UI.Localization;
using SPharmacyLog.UI.Services;
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

    private TabControl customerTabs = null!;
    private TextBlock customerOverview = null!;
    private readonly Dictionary<string, TextBox> activityFields = new();
    private ListBox borrowingList = null!;
    private ListBox subscriptionList = null!;
    private ListBox paymentList = null!;
    private ListBox ledgerList = null!;
    private List<BorrowingRecord> currentBorrowings = new();
    private TextBox gs1ScanInput = null!;
    private TextBox gs1EofEan13Input = null!;
    private TextBlock gs1ScanStatus = null!;
    private readonly Dictionary<string, TextBox> gs1ParsedFields = new();
    private Gs1DataMatrixParser.ParsedPack? currentParsedPack;

    private void ShowCustomers()
    {
        customerFields.Clear(); activityFields.Clear(); gs1ParsedFields.Clear(); selectedCustomerId = null; currentParsedPack = null;
        customerSearch = new TextBox { Watermark = Tr.Loc["Customers.Search"], Margin = new Thickness(0, 0, 0, 10) };
        customerList = new ListBox { MinWidth = 300 };
        customerStatus = new TextBlock { Foreground = Brushes.LightGreen, Margin = new Thickness(0, 8, 0, 0) };
        customerSearch.TextChanged += async (_, _) => await SearchCustomersAsync();
        customerList.SelectionChanged += async (_, _) =>
        {
            if (customerList.SelectedItem is CustomerListItem item)
            {
                await LoadCustomerAsync(item.Id);
                await RefreshCustomerWorkspaceAsync();
            }
        };
        var left = new StackPanel { Spacing = 8 };
        left.Children.Add(new TextBlock { Text = Tr.Loc["Navigation.Customers"], FontSize = 18, FontWeight = FontWeight.Bold });
        left.Children.Add(customerSearch); left.Children.Add(customerList);
        var newButton = new Button { Content = Tr.Loc["Customers.New"], Classes = { "accent" }, HorizontalAlignment = HorizontalAlignment.Stretch };
        newButton.Click += (_, _) => { ClearCustomerForm(); customerTabs.SelectedIndex = 1; };
        left.Children.Add(newButton);

        var details = BuildCustomerDetailsTab();
        customerOverview = new TextBlock { Text = "Select a customer to see their account summary.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(12) };
        var overviewTab = new TabItem { Header = "Overview", Content = new StackPanel { Spacing = 12, Children = { customerOverview } } };
        var borrowingTab = BuildBorrowingsTab();
        var subscriptionsTab = BuildSubscriptionsTab();
        var paymentsTab = BuildPaymentsTab();
        var ledgerTab = BuildLedgerTab();
        customerTabs = new TabControl { Items = { overviewTab, details, borrowingTab, subscriptionsTab, paymentsTab, ledgerTab }, Margin = new Thickness(12) };
        customerTabs.SelectionChanged += async (_, _) =>
        {
            if (selectedCustomerId.HasValue) await RefreshCustomerWorkspaceAsync();
        };
        var right = new StackPanel { Margin = new Thickness(12, 0, 12, 0), Spacing = 8 };
        right.Children.Add(customerTabs);
        right.Children.Add(customerStatus);
        var layout = new Grid { ColumnDefinitions = new ColumnDefinitions("290,*"), Margin = new Thickness(18) };
        layout.Children.Add(left); Grid.SetColumn(right, 1); layout.Children.Add(right);
        pageHost.Content = layout;
        _ = SearchCustomersAsync();
        ClearCustomerForm();
    }

    private TabItem BuildCustomerDetailsTab()
    {
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
        var clear = new Button { Content = Tr.Loc["Customers.Clear"], MinWidth = 100 };
        clear.Click += (_, _) => ClearCustomerForm();
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Margin = new Thickness(5, 14, 0, 0) };
        actions.Children.Add(save); actions.Children.Add(clear);
        var panel = new StackPanel { Spacing = 8, Children = { new TextBlock { Text = Tr.Loc["Customers.Details"], FontSize = 18, FontWeight = FontWeight.Bold }, fieldsGrid, actions } };
        return new TabItem { Header = "Details", Content = new ScrollViewer { Content = panel } };
    }

    private TabItem BuildBorrowingsTab()
    {
        borrowingList = new ListBox { MinHeight = 180 };

        // A scanner configured as USB keyboard/HID writes directly into this field.
        // Most scanners send Enter after the DataMatrix payload.
        gs1ScanInput = new TextBox
        {
            Watermark = "Scan GS1 DataMatrix here (scanner suffix: Enter)",
            MinHeight = 38,
            FontSize = 15
        };
        gs1ScanInput.KeyDown += async (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                e.Handled = true;
                await PreviewGs1ScanAsync();
            }
        };

        gs1ScanStatus = new TextBlock
        {
            Text = "Ready for a GS1 DataMatrix scan.",
            Foreground = Brushes.Gray,
            TextWrapping = TextWrapping.Wrap
        };

        var scanPanel = new StackPanel
        {
            Spacing = 8,
            Margin = new Thickness(0, 0, 0, 8),
            Children =
            {
                new TextBlock { Text = "PHARMACEUTICAL PACK SCAN", FontSize = 16, FontWeight = FontWeight.Bold },
                new TextBlock { Text = "Scan the 2D DataMatrix. The original scanner text is retained exactly; the fields below are extracted from GS1 Application Identifiers.", TextWrapping = TextWrapping.Wrap },
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 8,
                    Children =
                    {
                        gs1ScanInput,
                        CreateGs1ParseButton()
                    }
                },
                gs1ScanStatus
            }
        };

        var detailsGrid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,*"),
            RowDefinitions = new RowDefinitions("Auto,Auto,Auto,Auto,Auto")
        };
        AddGs1Detail(detailsGrid, "GTIN-14 (AI 01)", "Gtin14", 0, 0);
        AddGs1Detail(detailsGrid, "Serial number (AI 21)", "SerialNumber", 0, 1);
        AddGs1Detail(detailsGrid, "Expiry date (AI 17)", "ExpiryDate", 1, 0);
        AddGs1Detail(detailsGrid, "Batch / lot (AI 10)", "BatchLot", 1, 1);
        AddGs1Detail(detailsGrid, "Raw scanner payload", "RawPayload", 2, 0, 2);
        AddGs1Detail(detailsGrid, "Pack status", "HmvoStatus", 3, 0);
        AddGs1Detail(detailsGrid, "Medicine catalogue match", "MedicineMatch", 3, 1);
        gs1ParsedFields["HmvoStatus"].Text = SerializedPackStatuses.Active;
        gs1ParsedFields["MedicineMatch"].Text = "Not matched";
        var eofPanel = new StackPanel { Spacing = 3, Margin = new Thickness(5) };
        eofPanel.Children.Add(new TextBlock { Text = "Greek EOF / NTIN barcode (optional mapping)" });
        gs1EofEan13Input = new TextBox { Watermark = "13-digit 0280... EOF barcode", MaxLength = 13 };
        eofPanel.Children.Add(gs1EofEan13Input);
        Grid.SetRow(eofPanel, 4);
        Grid.SetColumnSpan(eofPanel, 2);
        detailsGrid.Children.Add(eofPanel);

        var fields = new StackPanel { Spacing = 6 };
        AddActivityField(fields, "Medicine name (auto-filled when barcode is mapped)", "BorrowingName");
        AddActivityField(fields, "Quantity (one serialized pack per record)", "BorrowingQuantity", "1");
        AddActivityField(fields, "Internal barcode / borrowing reference", "BorrowingBarcode");
        AddActivityField(fields, "Due date (yyyy-MM-dd)", "BorrowingDueDate");
        AddActivityField(fields, "Value", "BorrowingValue");
        AddActivityField(fields, "Notes", "BorrowingNotes");

        var add = new Button { Content = "＋ Add borrowing", Classes = { "accent" } };
        add.Click += async (_, _) => await AddBorrowingAsync();
        var returned = new Button { Content = "Mark selected as returned" };
        returned.Click += async (_, _) => await ReturnSelectedBorrowingAsync();

        var panel = new StackPanel
        {
            Spacing = 10,
            Children =
            {
                borrowingList,
                scanPanel,
                detailsGrid,
                fields,
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { add, returned } }
            }
        };
        return new TabItem { Header = "Borrowings", Content = new ScrollViewer { Content = panel } };
    }

    private Button CreateGs1ParseButton()
    {
        var button = new Button { Content = "Parse scan", MinWidth = 110 };
        button.Click += async (_, _) => await PreviewGs1ScanAsync();
        return button;
    }

    private void AddGs1Detail(Grid grid, string label, string key, int row, int column, int columnSpan = 1)
    {
        var panel = new StackPanel { Spacing = 3, Margin = new Thickness(5) };
        panel.Children.Add(new TextBlock { Text = label });
        var value = new TextBox { IsReadOnly = true, MinHeight = 32 };
        gs1ParsedFields[key] = value;
        panel.Children.Add(value);
        Grid.SetRow(panel, row);
        Grid.SetColumn(panel, column);
        Grid.SetColumnSpan(panel, columnSpan);
        grid.Children.Add(panel);
    }

    private async Task PreviewGs1ScanAsync()
    {
        var raw = gs1ScanInput.Text ?? string.Empty;
        if (!Gs1DataMatrixParser.TryParse(raw, out var parsed, out var error) || parsed is null)
        {
            currentParsedPack = null;
            gs1ScanStatus.Foreground = Brushes.OrangeRed;
            gs1ScanStatus.Text = error;
            ClearGs1ParsedFields();
            return;
        }

        currentParsedPack = parsed;
        gs1ParsedFields["Gtin14"].Text = parsed.Gtin14;
        gs1ParsedFields["SerialNumber"].Text = parsed.SerialNumber;
        gs1ParsedFields["ExpiryDate"].Text = parsed.ExpiryDate.ToString("dd/MM/yyyy");
        gs1ParsedFields["BatchLot"].Text = parsed.BatchLot;
        gs1ParsedFields["RawPayload"].Text = raw.Replace(Gs1DataMatrixParser.GroupSeparator.ToString(), "<GS>", StringComparison.Ordinal);
        gs1ScanStatus.Foreground = Brushes.LightGreen;
        gs1ScanStatus.Text = "GS1 scan parsed successfully. Review the extracted details before saving the borrowing.";

        await using var db = new PharmacyDbContext(PharmacyDb.Options());
        var mappedBarcode = await db.MedicineBarcodes
            .Include(x => x.Medicine)
            .FirstOrDefaultAsync(x => x.BarcodeValue == parsed.Gtin14);
        var medicine = mappedBarcode?.Medicine
            ?? await db.Medicines.FirstOrDefaultAsync(x => x.Barcode == parsed.Gtin14);

        if (medicine is not null)
        {
            if (activityFields.TryGetValue("BorrowingName", out var nameBox))
                nameBox.Text = medicine.Name;
            var eofBarcode = await db.MedicineBarcodes
                .Where(x => x.MedicineId == medicine.Id && x.BarcodeType == "EOF_EAN13")
                .Select(x => x.BarcodeValue)
                .FirstOrDefaultAsync();
            if (!string.IsNullOrWhiteSpace(eofBarcode))
                gs1EofEan13Input.Text = eofBarcode;
            gs1ParsedFields["MedicineMatch"].Text = medicine.Name;
            gs1ScanStatus.Text += $" Medicine matched: {medicine.Name}.";
        }
        else
        {
            gs1ParsedFields["MedicineMatch"].Text = "Not matched — enter medicine manually";
            gs1ScanStatus.Text += " No medicine catalogue match yet; you can enter the medicine name and EOF barcode manually.";
        }
    }

    private void ClearGs1ParsedFields()
    {
        foreach (var field in gs1ParsedFields.Values) field.Text = string.Empty;
        if (gs1ParsedFields.TryGetValue("HmvoStatus", out var status)) status.Text = SerializedPackStatuses.Active;
        if (gs1ParsedFields.TryGetValue("MedicineMatch", out var match)) match.Text = "Not matched";
    }

    private TabItem BuildSubscriptionsTab()
    {
        subscriptionList = new ListBox { MinHeight = 180 };
        var fields = new StackPanel { Spacing = 6 };
        AddActivityField(fields, "Subscription title", "SubscriptionTitle");
        AddActivityField(fields, "Start date (yyyy-MM-dd)", "SubscriptionStart", DateTime.Today.ToString("yyyy-MM-dd"));
        AddActivityField(fields, "Next due date (yyyy-MM-dd)", "SubscriptionDue");
        AddActivityField(fields, "Expiry date (yyyy-MM-dd, optional)", "SubscriptionExpiry");
        AddActivityField(fields, "Frequency", "SubscriptionFrequency", "Monthly");
        AddActivityField(fields, "Notes", "SubscriptionNotes");
        var add = new Button { Content = "＋ Add subscription", Classes = { "accent" } };
        add.Click += async (_, _) => await AddSubscriptionAsync();
        return new TabItem { Header = "Subscriptions", Content = new StackPanel { Spacing = 10, Children = { subscriptionList, fields, add } } };
    }

    private TabItem BuildPaymentsTab()
    {
        paymentList = new ListBox { MinHeight = 180 };
        var fields = new StackPanel { Spacing = 6 };
        AddActivityField(fields, "Payment amount", "PaymentAmount");
        AddActivityField(fields, "Method (Cash / Card / Transfer)", "PaymentMethod", "Cash");
        AddActivityField(fields, "Reference", "PaymentReference");
        AddActivityField(fields, "Notes", "PaymentNotes");
        var add = new Button { Content = "＋ Record payment", Classes = { "accent" } };
        add.Click += async (_, _) => await AddPaymentAsync();
        return new TabItem { Header = "Payments", Content = new StackPanel { Spacing = 10, Children = { paymentList, fields, add } } };
    }

    private TabItem BuildLedgerTab()
    {
        ledgerList = new ListBox { MinHeight = 180 };
        var fields = new StackPanel { Spacing = 6 };
        AddActivityField(fields, "Entry type (Charge / Adjustment / Reversal)", "LedgerType", "Charge");
        AddActivityField(fields, "Amount (positive number)", "LedgerAmount");
        AddActivityField(fields, "Description", "LedgerDescription");
        var add = new Button { Content = "＋ Add ledger entry", Classes = { "accent" } };
        add.Click += async (_, _) => await AddLedgerEntryAsync();
        return new TabItem { Header = "Ledger", Content = new StackPanel { Spacing = 10, Children = { ledgerList, fields, add } } };
    }

    private void AddActivityField(StackPanel parent, string label, string key, string? defaultValue = null)
    {
        var input = new TextBox { Watermark = label, Text = defaultValue ?? string.Empty, MinHeight = 32 };
        activityFields[key] = input;
        parent.Children.Add(input);
    }

    private string ActivityValue(string key) => activityFields.TryGetValue(key, out var box) ? box.Text?.Trim() ?? string.Empty : string.Empty;

    private async Task RefreshCustomerWorkspaceAsync()
    {
        if (!selectedCustomerId.HasValue) return;
        var id = selectedCustomerId.Value;
        await using var db = new PharmacyDbContext(PharmacyDb.Options());
        var customer = await db.Customers.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id);
        if (customer is null) return;
        var borrowings = await db.Borrowings.AsNoTracking().Include(x => x.SerializedPackScan).Where(x => x.CustomerId == id).OrderByDescending(x => x.BorrowedAt).ToListAsync();
        var subscriptions = await db.Subscriptions.AsNoTracking().Where(x => x.CustomerId == id).OrderByDescending(x => x.StartDate).ToListAsync();
        var payments = await db.Payments.AsNoTracking().Where(x => x.CustomerId == id).OrderByDescending(x => x.PaidAt).ToListAsync();
        var ledger = await db.LedgerEntries.AsNoTracking().Where(x => x.CustomerId == id).OrderByDescending(x => x.EntryDate).ToListAsync();
        var charges = ledger.Where(x => x.Type == LedgerEntryType.Charge || x.Type == LedgerEntryType.Adjustment).Sum(x => x.Amount);
        var credits = ledger.Where(x => x.Type == LedgerEntryType.Payment || x.Type == LedgerEntryType.Reversal).Sum(x => x.Amount);
        var balance = charges - credits;
        customerOverview.Text = $"{customer.CustomerNumber}  |  {customer.FullName}\n{customer.Address}, {customer.City} {customer.Postcode}\nTelephone: {customer.Telephone ?? customer.Phone}  |  Mobile: {customer.Mobile}\nEmail: {customer.Email}\n\nBORROWINGS: {borrowings.Count} total, {borrowings.Count(x => x.Status == BorrowingStatus.Borrowed)} currently borrowed\nSUBSCRIPTIONS: {subscriptions.Count(x => x.Status == SubscriptionStatus.Active)} active of {subscriptions.Count}\nPAYMENTS: {payments.Count} recorded  |  Total paid: {payments.Sum(x => x.Amount):C}\nACCOUNT BALANCE: {balance:C}\n\nRecent activity:\n{string.Join("\n", ledger.Take(5).Select(x => $"{x.EntryDate:dd/MM/yyyy}  {x.Type}  {x.Amount:C}  {x.Description}"))}";
        currentBorrowings = borrowings;
        borrowingList.ItemsSource = borrowings.Select(x =>
            x.SerializedPackScan is null
                ? $"{x.BorrowedAt:dd/MM/yyyy} | {x.MedicineName} | Qty {x.Quantity} | {x.Status} | Due {x.DueDate:dd/MM/yyyy} | {x.InternalBarcode}"
                : $"{x.BorrowedAt:dd/MM/yyyy} | {x.MedicineName} | SN {x.SerializedPackScan.SerialNumber} | GTIN {x.SerializedPackScan.Gtin14} | Exp {x.SerializedPackScan.ExpiryDate:dd/MM/yyyy} | Lot {x.SerializedPackScan.BatchLot} | {x.Status}")
            .ToList();
        subscriptionList.ItemsSource = subscriptions.Select(x => $"{x.Title} | {x.Status} | Next: {x.NextDueDate:dd/MM/yyyy} | Expires: {x.ExpiryDate:dd/MM/yyyy} | {x.Frequency}").ToList();
        paymentList.ItemsSource = payments.Select(x => $"{x.PaidAt:dd/MM/yyyy} | {x.Amount:C} | {x.Method} | {x.Reference}").ToList();
        ledgerList.ItemsSource = ledger.Select(x => $"{x.EntryDate:dd/MM/yyyy} | {x.Type} | {x.Amount:C} | {x.Description}").ToList();
    }

    private async Task AddBorrowingAsync()
    {
        if (!selectedCustomerId.HasValue) { SetActivityStatus("Save or select a customer first."); return; }

        // Parse on Save as well, so a scanner does not have to send an Enter suffix.
        var rawGs1 = gs1ScanInput?.Text ?? string.Empty;
        Gs1DataMatrixParser.ParsedPack? parsedPack = null;
        if (!string.IsNullOrWhiteSpace(rawGs1))
        {
            if (!Gs1DataMatrixParser.TryParse(rawGs1, out parsedPack, out var parseError) || parsedPack is null)
            {
                SetActivityStatus("GS1 scan is incomplete or invalid: " + parseError);
                return;
            }
            currentParsedPack = parsedPack;
        }

        if (string.IsNullOrWhiteSpace(ActivityValue("BorrowingName")) || string.IsNullOrWhiteSpace(ActivityValue("BorrowingBarcode")))
        {
            SetActivityStatus("Medicine name and internal borrowing reference are required.");
            return;
        }
        if (!int.TryParse(ActivityValue("BorrowingQuantity"), out var quantity) || quantity < 1)
        {
            SetActivityStatus("Enter a valid quantity.");
            return;
        }
        if (parsedPack is not null && quantity != 1)
        {
            SetActivityStatus("A serialized DataMatrix identifies one physical pack. Create one borrowing record per scanned pack (quantity 1).");
            return;
        }

        DateTime? due = DateTime.TryParse(ActivityValue("BorrowingDueDate"), out var dueDate) ? dueDate : null;
        decimal? value = decimal.TryParse(ActivityValue("BorrowingValue"), out var amount) ? amount : null;
        await using var db = new PharmacyDbContext(PharmacyDb.Options());
        if (await db.Borrowings.AnyAsync(x => x.InternalBarcode == ActivityValue("BorrowingBarcode")))
        {
            SetActivityStatus("This internal barcode is already in use.");
            return;
        }

        SerializedPackScan? serializedPack = null;
        if (parsedPack is not null)
        {
            if (await db.SerializedPackScans.AnyAsync(x => x.Gtin14 == parsedPack.Gtin14 && x.SerialNumber == parsedPack.SerialNumber))
            {
                SetActivityStatus("This exact physical pack (GTIN + serial number) has already been scanned. Duplicate registration is blocked.");
                return;
            }

            var eofEan13 = gs1EofEan13Input.Text?.Trim();
            if (!string.IsNullOrWhiteSpace(eofEan13) &&
                (eofEan13.Length != 13 || eofEan13.Any(ch => !char.IsDigit(ch))))
            {
                SetActivityStatus("The EOF / NTIN barcode must contain exactly 13 digits.");
                return;
            }

            var matchedMedicine = await db.MedicineBarcodes
                .Include(x => x.Medicine)
                .Where(x => x.BarcodeValue == parsedPack.Gtin14 || (!string.IsNullOrWhiteSpace(eofEan13) && x.BarcodeValue == eofEan13))
                .Select(x => x.Medicine)
                .FirstOrDefaultAsync()
                ?? await db.Medicines.FirstOrDefaultAsync(x => x.Barcode == parsedPack.Gtin14 ||
                    (!string.IsNullOrWhiteSpace(eofEan13) && x.Barcode == eofEan13));

            serializedPack = new SerializedPackScan
            {
                RawGs1String = rawGs1,
                Gtin14 = parsedPack.Gtin14,
                EofEan13 = string.IsNullOrWhiteSpace(eofEan13) ? null : eofEan13,
                SerialNumber = parsedPack.SerialNumber,
                ExpiryDate = parsedPack.ExpiryDate,
                BatchLot = parsedPack.BatchLot,
                HmvoStatus = SerializedPackStatuses.Active,
                ScannedAt = DateTime.UtcNow,
                MedicineId = matchedMedicine?.Id
            };
        }

        var borrowing = new BorrowingRecord
        {
            CustomerId = selectedCustomerId.Value,
            MedicineName = ActivityValue("BorrowingName"),
            OriginalBarcode = parsedPack?.Gtin14,
            SerializedPackScan = serializedPack,
            Quantity = quantity,
            InternalBarcode = ActivityValue("BorrowingBarcode"),
            DueDate = due,
            Value = value,
            Notes = ActivityValue("BorrowingNotes")
        };
        db.Borrowings.Add(borrowing);
        await db.SaveChangesAsync();

        SetActivityStatus(parsedPack is null
            ? "Borrowing record added."
            : "GS1 pack scan and borrowing saved successfully. The raw payload and all parsed fields are stored.");
        if (gs1ScanInput is not null) gs1ScanInput.Text = string.Empty;
        if (gs1EofEan13Input is not null) gs1EofEan13Input.Text = string.Empty;
        currentParsedPack = null;
        ClearGs1ParsedFields();
        gs1ScanStatus.Text = "Ready for the next GS1 DataMatrix scan.";
        gs1ScanStatus.Foreground = Brushes.Gray;
        await RefreshCustomerWorkspaceAsync();
    }

    private async Task ReturnSelectedBorrowingAsync()
    {
        if (borrowingList.SelectedIndex < 0 || borrowingList.SelectedIndex >= currentBorrowings.Count || !selectedCustomerId.HasValue) { SetActivityStatus("Select a borrowing record first."); return; }
        var selectedId = currentBorrowings[borrowingList.SelectedIndex].Id;
        await using var db = new PharmacyDbContext(PharmacyDb.Options());
        var record = await db.Borrowings.Include(x => x.SerializedPackScan).FirstOrDefaultAsync(x => x.Id == selectedId && x.CustomerId == selectedCustomerId.Value && x.Status == BorrowingStatus.Borrowed);
        if (record is null) { SetActivityStatus("No active borrowing matches the entered barcode."); return; }
        record.Status = BorrowingStatus.Returned;
        record.ReturnedAt = DateTime.Now;
        if (record.SerializedPackScan is not null)
            record.SerializedPackScan.HmvoStatus = SerializedPackStatuses.Returned;
        await db.SaveChangesAsync(); SetActivityStatus("Item marked as returned."); await RefreshCustomerWorkspaceAsync();
    }

    private async Task AddSubscriptionAsync()
    {
        if (!selectedCustomerId.HasValue) { SetActivityStatus("Save or select a customer first."); return; }
        if (string.IsNullOrWhiteSpace(ActivityValue("SubscriptionTitle")) || !DateTime.TryParse(ActivityValue("SubscriptionDue"), out var due)) { SetActivityStatus("Enter a title and valid next due date."); return; }
        DateTime.TryParse(ActivityValue("SubscriptionStart"), out var start);
        DateTime? expiry = DateTime.TryParse(ActivityValue("SubscriptionExpiry"), out var exp) ? exp : null;
        await using var db = new PharmacyDbContext(PharmacyDb.Options());
        db.Subscriptions.Add(new Subscription { CustomerId = selectedCustomerId.Value, Title = ActivityValue("SubscriptionTitle"), StartDate = start == default ? DateTime.Today : start, NextDueDate = due, ExpiryDate = expiry, Frequency = string.IsNullOrWhiteSpace(ActivityValue("SubscriptionFrequency")) ? "Monthly" : ActivityValue("SubscriptionFrequency"), Notes = ActivityValue("SubscriptionNotes") });
        await db.SaveChangesAsync(); SetActivityStatus("Subscription added."); await RefreshCustomerWorkspaceAsync();
    }

    private async Task AddPaymentAsync()
    {
        if (!selectedCustomerId.HasValue) { SetActivityStatus("Save or select a customer first."); return; }
        if (!decimal.TryParse(ActivityValue("PaymentAmount"), out var amount) || amount <= 0) { SetActivityStatus("Enter a payment amount greater than zero."); return; }
        await using var db = new PharmacyDbContext(PharmacyDb.Options());
        var method = string.IsNullOrWhiteSpace(ActivityValue("PaymentMethod")) ? "Cash" : ActivityValue("PaymentMethod");
        var reference = ActivityValue("PaymentReference");
        db.Payments.Add(new Payment { CustomerId = selectedCustomerId.Value, Amount = amount, Method = method, Reference = reference, Notes = ActivityValue("PaymentNotes"), RecordedBy = viewModel.User.Username });
        db.LedgerEntries.Add(new LedgerEntry { CustomerId = selectedCustomerId.Value, Type = LedgerEntryType.Payment, Amount = amount, Description = $"Payment - {method}", SourceReference = reference, RecordedBy = viewModel.User.Username });
        await db.SaveChangesAsync(); SetActivityStatus("Payment recorded and ledger updated."); await RefreshCustomerWorkspaceAsync();
    }

    private async Task AddLedgerEntryAsync()
    {
        if (!selectedCustomerId.HasValue) { SetActivityStatus("Save or select a customer first."); return; }
        if (!decimal.TryParse(ActivityValue("LedgerAmount"), out var amount) || amount <= 0 || string.IsNullOrWhiteSpace(ActivityValue("LedgerDescription"))) { SetActivityStatus("Enter a positive amount and description."); return; }
        if (!Enum.TryParse<LedgerEntryType>(ActivityValue("LedgerType"), true, out var type) || type == LedgerEntryType.Payment) { SetActivityStatus("Choose Charge, Adjustment or Reversal. Payments should be recorded in the Payments tab."); return; }
        await using var db = new PharmacyDbContext(PharmacyDb.Options());
        db.LedgerEntries.Add(new LedgerEntry { CustomerId = selectedCustomerId.Value, Type = type, Amount = amount, Description = ActivityValue("LedgerDescription"), RecordedBy = viewModel.User.Username });
        await db.SaveChangesAsync(); SetActivityStatus("Ledger entry added."); await RefreshCustomerWorkspaceAsync();
    }

    private void SetActivityStatus(string message)
    {
        customerStatus.Text = message;
        customerStatus.Foreground = Brushes.LightGreen;
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
        await RefreshCustomerWorkspaceAsync();
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