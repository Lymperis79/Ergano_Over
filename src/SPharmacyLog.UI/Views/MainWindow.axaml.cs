using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using System.Runtime.InteropServices;
using ZXing;
using ZXing.Datamatrix;
using Avalonia.VisualTree;
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
    private bool isApplyingTranslations;
    private readonly ContentControl pageHost;
    private TextBlock dailySummary = new();
    private ListBox dayRecords = new();
    private StackPanel calendarPanel = new();
    private DateTime displayedMonth = new(DateTime.Today.Year, DateTime.Today.Month, 1);
    private TextBlock? calendarMonthTitle;
    private bool showingExpiryCalendar;
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
        Tr.Loc.LanguageChanged += (_, _) =>
        {
            ApplyTranslations();
            if (showingExpiryCalendar) _ = LoadSelectedDayAsync();
        };
        Opened += (_, _) => ApplyTranslations();
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
        isApplyingTranslations = true;
        Title = Tr.Loc["App.Title"];
        if (languageBox is not null) languageBox.SelectedIndex = Tr.Loc.CurrentLanguage == AppLanguage.Greek ? 1 : 0;

        // Translate the live visual tree in place. This keeps the current page,
        // selected customer, typed values and open form intact while switching languages.
        foreach (var control in this.GetVisualDescendants().OfType<Control>())
        {
            switch (control)
            {
                case TextBlock textBlock when !string.IsNullOrEmpty(textBlock.Text):
                    textBlock.Text = Tr.Loc.TranslateText(textBlock.Text);
                    break;
                case Button button when button.Content is string content:
                    button.Content = Tr.Loc.TranslateText(content);
                    break;
                case TextBox textBox when !string.IsNullOrEmpty(textBox.PlaceholderText):
                    textBox.PlaceholderText = Tr.Loc.TranslateText(textBox.PlaceholderText);
                    break;
                case TabItem tab when tab.Header is string header:
                    tab.Header = Tr.Loc.TranslateText(header);
                    break;
                case ComboBoxItem item when item.Content is string itemText:
                    item.Content = Tr.Loc.TranslateText(itemText);
                    break;
            }
        }

        TranslateListItems(dayRecords);
        TranslateListItems(customerList);
        TranslateListItems(borrowingList);
        TranslateListItems(customerHistoryList);
        TranslateListItems(subscriptionList);
        TranslateListItems(paymentList);
        TranslateListItems(ledgerList);
        foreach (var key in new[] { "SubscriptionFrequency", "PaymentMethod", "LedgerType" })
        {
            if (activityFields.TryGetValue(key, out var activityField) && !string.IsNullOrWhiteSpace(activityField.Text))
                activityField.Text = Tr.Loc.TranslateText(activityField.Text);
        }
        if (gs1ParsedFields.TryGetValue("HmvoStatus", out var statusField))
            statusField.Text = Tr.Loc.TranslateText(SerializedPackStatuses.Active);
        if (gs1ParsedFields.TryGetValue("MedicineMatch", out var matchField) &&
            (matchField.Text == "Not matched" || matchField.Text == "Δεν υπάρχει αντιστοίχιση" ||
             matchField.Text == "Not matched — enter medicine manually" ||
             matchField.Text == "Χωρίς αντιστοίχιση — εισαγάγετε χειροκίνητα το φάρμακο"))
            matchField.Text = Tr.Loc.TranslateText(matchField.Text);
        if (calendarMonthTitle is not null)
            calendarMonthTitle.Text = displayedMonth.ToString("MMMM yyyy");
        isApplyingTranslations = false;
    }

    private static void TranslateListItems(ListBox? list)
    {
        if (list?.ItemsSource is IEnumerable<string> values)
            list.ItemsSource = values.Select(Tr.Loc.TranslateText).ToList();
    }

    private void OnLanguageChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (isApplyingTranslations) return;
        if (languageBox?.SelectedIndex == 1) Tr.Loc.SetLanguage(AppLanguage.Greek);
        else if (languageBox?.SelectedIndex == 0) Tr.Loc.SetLanguage(AppLanguage.English);
    }



    private static void AttachDialogLocalization(Window dialog)
    {
        void TranslateDialog()
        {
            dialog.Title = Tr.Loc.TranslateText(dialog.Title ?? string.Empty);
            foreach (var control in dialog.GetVisualDescendants().OfType<Control>())
            {
                switch (control)
                {
                    case TextBlock textBlock when !string.IsNullOrEmpty(textBlock.Text):
                        textBlock.Text = Tr.Loc.TranslateText(textBlock.Text);
                        break;
                    case Button button when button.Content is string content:
                        button.Content = Tr.Loc.TranslateText(content);
                        break;
                    case TextBox textBox:
                        if (!string.IsNullOrEmpty(textBox.PlaceholderText))
                            textBox.PlaceholderText = Tr.Loc.TranslateText(textBox.PlaceholderText);
                        if (textBox.Text is "Monthly" or "Μηνιαία" or "Cash" or "Μετρητά" or "Card" or "Κάρτα" or "Transfer" or "Μεταφορά")
                            textBox.Text = Tr.Loc.TranslateText(textBox.Text);
                        break;
                    case ComboBox comboBox:
                        if (comboBox.ItemsSource is IEnumerable<string> options)
                        {
                            var selected = comboBox.SelectedItem?.ToString();
                            var translated = options.Select(Tr.Loc.TranslateText).ToArray();
                            comboBox.ItemsSource = translated;
                            if (selected is not null)
                                comboBox.SelectedItem = Tr.Loc.TranslateText(selected);
                        }
                        break;
                    case ComboBoxItem item when item.Content is string itemText:
                        item.Content = Tr.Loc.TranslateText(itemText);
                        break;
                }
            }
        }

        EventHandler languageChanged = (_, _) => TranslateDialog();
        Tr.Loc.LanguageChanged += languageChanged;
        dialog.Opened += (_, _) => TranslateDialog();
        dialog.Closed += (_, _) => Tr.Loc.LanguageChanged -= languageChanged;
    }

    private static ColumnDefinitions CreateColumns(params GridLength[] widths)
    {
        var definitions = new ColumnDefinitions();
        foreach (var width in widths)
            definitions.Add(new ColumnDefinition { Width = width });
        return definitions;
    }

    private static RowDefinitions CreateRows(params GridLength[] heights)
    {
        var definitions = new RowDefinitions();
        foreach (var height in heights)
            definitions.Add(new RowDefinition { Height = height });
        return definitions;
    }

    private void ShowExpiryCalendar()
    {
        showingExpiryCalendar = true;
        calendarPanel = new StackPanel();
        dailySummary = new TextBlock();
        dayRecords = new ListBox();
        var content = new StackPanel { Margin = new Thickness(22), Spacing = 12 };
        content.Children.Add(new TextBlock { Text = Tr.Loc["Calendar.Title"], FontSize = 24, FontWeight = FontWeight.Bold });
        content.Children.Add(new TextBlock { Text = Tr.Loc["Calendar.Description"] });
        var monthBar = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Spacing = 10 };
        var previous = new Button { Content = "‹" };
        var monthTitle = new TextBlock { FontSize = 18, MinWidth = 160, VerticalAlignment = VerticalAlignment.Center, TextAlignment = TextAlignment.Center };
        calendarMonthTitle = monthTitle;
        var next = new Button { Content = "›" };
        previous.Click += async (_, _) => { displayedMonth = displayedMonth.AddMonths(-1); await RenderCalendarAsync(monthTitle); };
        next.Click += async (_, _) => { displayedMonth = displayedMonth.AddMonths(1); await RenderCalendarAsync(monthTitle); };
        monthBar.Children.Add(previous); monthBar.Children.Add(monthTitle); monthBar.Children.Add(next);
        content.Children.Add(monthBar);
        var weekdayGrid = new Grid { ColumnDefinitions = CreateColumns(GridLength.Star, GridLength.Star, GridLength.Star, GridLength.Star, GridLength.Star, GridLength.Star, GridLength.Star) };
        string[] weekdays = { "Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun" };
        for (var i = 0; i < weekdays.Length; i++) { var label = new TextBlock { Text = weekdays[i], HorizontalAlignment = HorizontalAlignment.Center }; Grid.SetColumn(label, i); weekdayGrid.Children.Add(label); }
        content.Children.Add(weekdayGrid);
        content.Children.Add(calendarPanel);
        dayRecords.MaxHeight = 260;
        content.Children.Add(dailySummary); content.Children.Add(dayRecords);
        pageHost.Content = content;
        ApplyTranslations();
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
        var grid = new Grid { ColumnDefinitions = CreateColumns(GridLength.Star, GridLength.Star, GridLength.Star, GridLength.Star, GridLength.Star, GridLength.Star, GridLength.Star), RowDefinitions = CreateRows(GridLength.Auto, GridLength.Auto, GridLength.Auto, GridLength.Auto, GridLength.Auto, GridLength.Auto) };
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
        ApplyTranslations();
    }

    private TabControl customerTabs = null!;
    private TextBlock customerOverview = null!;
    private readonly Dictionary<string, TextBox> activityFields = new();
    private ListBox borrowingList = null!;
    private ListBox subscriptionList = null!;
    private ListBox paymentList = null!;
    private ListBox ledgerList = null!;
    private List<BorrowingRecord> currentBorrowings = new();
    private List<Subscription> currentSubscriptions = new();
    private List<Payment> currentPayments = new();
    private List<LedgerEntry> currentLedgerEntries = new();
    private ListBox customerHistoryList = null!;
    private TextBlock borrowingSelectionDetails = null!;
    private Image borrowingBarcodeImage = null!;
    private BarcodeDisplayWindow? barcodePopupWindow;
    private TextBox gs1ScanInput = null!;
    private TextBox gs1ScanEcho = null!;
    private TextBox gs1EofEan13Input = null!;
    private TextBlock gs1ScanStatus = null!;
    private readonly Dictionary<string, TextBox> gs1ParsedFields = new();
    private Gs1DataMatrixParser.ParsedPack? currentParsedPack;

    private void ShowCustomers()
    {
        showingExpiryCalendar = false;
        customerFields.Clear(); activityFields.Clear(); gs1ParsedFields.Clear(); selectedCustomerId = null; currentParsedPack = null;
        customerSearch = new TextBox { PlaceholderText = Tr.Loc["Customers.Search"], Margin = new Thickness(0, 0, 0, 10) };
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
        var newButton = new Button { Content = Tr.Loc["Customers.New"], Classes = { "accent" }, HorizontalAlignment = HorizontalAlignment.Stretch, MinHeight = 40 };
        newButton.Click += (_, _) =>
        {
            ClearCustomerForm();
            customerTabs.SelectedIndex = 1;
            customerFields["CustomerNumber"].Focus();
        };
        left.Children.Add(newButton);
        left.Children.Add(customerSearch);
        left.Children.Add(customerList);

        var details = BuildCustomerDetailsTab();
        customerOverview = new TextBlock { Text = "Select a customer to see their account summary.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(12) };
        var overviewTab = new TabItem { Header = "Overview", Content = new StackPanel { Spacing = 12, Children = { customerOverview } } };
        var borrowingTab = BuildBorrowingsTab();
        var historyTab = BuildCustomerHistoryTab();
        var subscriptionsTab = BuildSubscriptionsTab();
        var paymentsTab = BuildPaymentsTab();
        var ledgerTab = BuildLedgerTab();
        customerTabs = new TabControl { Items = { overviewTab, details, borrowingTab, historyTab, subscriptionsTab, paymentsTab, ledgerTab }, Margin = new Thickness(12) };
        // Use a Grid rather than a StackPanel so the TabControl receives a finite
        // height. Otherwise its tab content (including the Borrowings ScrollViewer)
        // is measured with infinite height and cannot scroll vertically.
        var right = new Grid
        {
            Margin = new Thickness(12, 0, 12, 0),
            RowDefinitions = CreateRows(GridLength.Star, GridLength.Auto)
        };
        customerTabs.HorizontalAlignment = HorizontalAlignment.Stretch;
        customerTabs.VerticalAlignment = VerticalAlignment.Stretch;
        right.Children.Add(customerTabs);
        Grid.SetRow(customerStatus, 1);
        right.Children.Add(customerStatus);
        var layout = new Grid
        {
            ColumnDefinitions = CreateColumns(new GridLength(290), GridLength.Star),
            RowDefinitions = CreateRows(GridLength.Star),
            Margin = new Thickness(18)
        };
        layout.Children.Add(left);
        Grid.SetColumn(right, 1);
        layout.Children.Add(right);
        pageHost.Content = layout;
        _ = SearchCustomersAsync();
        ClearCustomerForm();
        ApplyTranslations();
    }

    private TabItem BuildCustomerDetailsTab()
    {
        var fieldsGrid = new Grid { ColumnDefinitions = CreateColumns(GridLength.Star, GridLength.Star), RowDefinitions = CreateRows(GridLength.Auto, GridLength.Auto, GridLength.Auto, GridLength.Auto, GridLength.Auto, GridLength.Auto, GridLength.Auto) };
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
        borrowingList = new ListBox { MinHeight = 220 };
        borrowingSelectionDetails = new TextBlock
        {
            Text = "Select a borrowing to view its details.",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(4, 8, 4, 8)
        };
        borrowingBarcodeImage = new Image
        {
            Width = 200, Height = 200, Stretch = Stretch.Uniform, IsVisible = false,
            HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(4)
        };
        borrowingList.SelectionChanged += (_, _) => ShowSelectedBorrowingScanDetails();
        borrowingBarcodeImage.PointerPressed += (_, e) =>
        {
            if (borrowingBarcodeImage.Source is not WriteableBitmap barcodeBitmap ||
                borrowingList.SelectedIndex < 0 ||
                borrowingList.SelectedIndex >= currentBorrowings.Count)
                return;

            e.Handled = true;
            ShowBarcodePopup(currentBorrowings[borrowingList.SelectedIndex].MedicineName, barcodeBitmap);
        };

        var add = new Button { Content = "＋ Add medicine", Classes = { "accent" }, MinWidth = 145, MinHeight = 38 };
        add.Click += async (_, _) => await OpenBorrowingEditorAsync(null);
        var edit = new Button { Content = "Edit selected", MinWidth = 125, MinHeight = 38 };
        edit.Click += async (_, _) =>
        {
            if (borrowingList.SelectedIndex < 0 || borrowingList.SelectedIndex >= currentBorrowings.Count)
            {
                SetActivityStatus("Select a borrowing to edit.");
                return;
            }
            await OpenBorrowingEditorAsync(currentBorrowings[borrowingList.SelectedIndex]);
        };
        var returned = new Button { Content = "Mark selected as returned", MinHeight = 38 };
        returned.Click += async (_, _) => await ReturnSelectedBorrowingAsync();
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { add, edit, returned } };

        var selectedBorrowingLayout = new Grid
        {
            ColumnDefinitions = CreateColumns(new GridLength(240), GridLength.Star),
            RowDefinitions = CreateRows(GridLength.Auto),
            ColumnSpacing = 16,
            Margin = new Thickness(4, 8, 4, 4)
        };
        var barcodePanel = new StackPanel
        {
            Spacing = 6,
            HorizontalAlignment = HorizontalAlignment.Center,
            Children =
            {
                new TextBlock
                {
                    Text = "GS1 DataMatrix barcode",
                    FontWeight = FontWeight.SemiBold,
                    HorizontalAlignment = HorizontalAlignment.Center
                },
                borrowingBarcodeImage
            }
        };
        var detailsPanel = new StackPanel
        {
            Spacing = 6,
            Children =
            {
                new TextBlock { Text = "SELECTED BORROWING DETAILS", FontWeight = FontWeight.SemiBold },
                borrowingSelectionDetails
            }
        };
        Grid.SetColumn(detailsPanel, 1);
        selectedBorrowingLayout.Children.Add(barcodePanel);
        selectedBorrowingLayout.Children.Add(detailsPanel);

        var panel = new StackPanel
        {
            Spacing = 10,
            Margin = new Thickness(8),
            Children =
            {
                new TextBlock { Text = "CUSTOMER MEDICINE BORROWINGS", FontSize = 18, FontWeight = FontWeight.Bold },
                new TextBlock { Text = "Add a medicine or edit an existing borrowing. Scan a GS1 DataMatrix inside the editor when registering a serialized pack.", TextWrapping = TextWrapping.Wrap, Foreground = Brushes.LightGray },
                actions,
                borrowingList,
                selectedBorrowingLayout
            }
        };
        return new TabItem
        {
            Header = "Borrowings",
            Content = new ScrollViewer
            {
                Content = panel,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto
            }
        };
    }

    private async Task OpenBorrowingEditorAsync(BorrowingRecord? existing)
    {
        if (!selectedCustomerId.HasValue)
        {
            SetActivityStatus("Save or select a customer first.");
            return;
        }

        var isEdit = existing is not null;
        var fields = new Dictionary<string, TextBox>();
        TextBox AddField(StackPanel host, string label, string key, string? value = null, string? placeholder = null)
        {
            var group = new StackPanel { Spacing = 4, Margin = new Thickness(0, 3) };
            group.Children.Add(new TextBlock { Text = label, Foreground = Brushes.LightGray, FontSize = 12 });
            var box = new TextBox { Text = value ?? string.Empty, PlaceholderText = placeholder ?? label, MinHeight = 34 };
            fields[key] = box;
            group.Children.Add(box);
            host.Children.Add(group);
            return box;
        }

        var scanInput = new TextBox
        {
            MinHeight = 38, FontSize = 15,
            PlaceholderText = "Scan GS1 DataMatrix here (scanner suffix: Enter)",
            IsEnabled = !isEdit
        };
        var scanEcho = new TextBox
        {
            IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap,
            MinHeight = 48, MaxHeight = 75, FontFamily = new FontFamily("Consolas")
        };
        var scanStatus = new TextBlock { Text = isEdit ? "The original serialized pack scan is retained unchanged." : "Ready for a GS1 DataMatrix scan.", TextWrapping = TextWrapping.Wrap, Foreground = Brushes.Gray };
        DateTime? scanCapturedAtUtc = null;
        var scanDateText = new TextBox
        {
            Text = existing is null ? "Will be recorded when the borrowing is saved." :
                (existing.SerializedPackScan is null ? existing.BorrowedAt : existing.SerializedPackScan.ScannedAt.ToLocalTime()).ToString("dd/MM/yyyy HH:mm:ss"),
            IsReadOnly = true,
            MinHeight = 34
        };
        Gs1DataMatrixParser.ParsedPack? parsedPack = null;
        var gtinInput = new TextBox { MaxLength = 14, PlaceholderText = "14-digit GTIN", IsEnabled = !isEdit };
        var serialInput = new TextBox { PlaceholderText = "Serial number (AI 21)", IsEnabled = !isEdit };
        var expiryInput = new TextBox { PlaceholderText = "Expiry date (yyyy-MM-dd)", IsEnabled = !isEdit };
        var lotInput = new TextBox { PlaceholderText = "Batch / Lot (AI 10)", IsEnabled = !isEdit };
        var eofInput = new TextBox { MaxLength = 13, PlaceholderText = "Optional 13-digit Greek EOF / NTIN barcode", IsEnabled = !isEdit };
        var medicineNameInput = new TextBox { Text = existing?.MedicineName ?? string.Empty, MinHeight = 36, FontSize = 15, PlaceholderText = "Type or confirm the medicine name" };
        fields["MedicineName"] = medicineNameInput;
        var scanPanel = new StackPanel { Spacing = 7 };
        scanPanel.Children.Add(new TextBlock { Text = "GS1 DATAMATRIX SCAN", FontWeight = FontWeight.SemiBold });
        scanPanel.Children.Add(scanInput);
        scanPanel.Children.Add(new TextBlock { Text = "Raw barcode (GS separator shown as <GS>):", FontSize = 12, Foreground = Brushes.LightGray });
        scanPanel.Children.Add(scanEcho);
        if (!isEdit)
        {
            scanPanel.Children.Add(new TextBlock { Text = "Medicine name", FontSize = 12, Foreground = Brushes.LightGray });
            scanPanel.Children.Add(medicineNameInput);
        }
        scanPanel.Children.Add(new TextBlock { Text = "Parsed fields — verify or correct before saving:", FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 5, 0, 0) });
        void AddScanField(string label, TextBox box)
        {
            scanPanel.Children.Add(new TextBlock { Text = label, FontSize = 12, Foreground = Brushes.LightGray });
            scanPanel.Children.Add(box);
        }
        AddScanField("GTIN (AI 01)", gtinInput);
        AddScanField("Serial number (AI 21)", serialInput);
        AddScanField("Expiry date (AI 17)", expiryInput);
        AddScanField("Batch / Lot (AI 10)", lotInput);
        AddScanField("EOF / EAN-13", eofInput);
        scanPanel.Children.Add(scanStatus);
        scanInput.TextChanged += (_, _) => scanEcho.Text = (scanInput.Text ?? string.Empty)
            .Replace(Gs1DataMatrixParser.GroupSeparator.ToString(), "<GS>", StringComparison.Ordinal);
        scanInput.KeyDown += async (_, e) =>
        {
            if (e.Key != Key.Enter) return;
            e.Handled = true;
            var raw = scanInput.Text ?? string.Empty;
            if (!Gs1DataMatrixParser.TryParse(raw, out parsedPack, out var error) || parsedPack is null)
            {
                scanStatus.Foreground = Brushes.OrangeRed;
                scanStatus.Text = Tr.Loc.TranslateText(error);
                return;
            }
            scanCapturedAtUtc = DateTime.UtcNow;
            scanDateText.Text = scanCapturedAtUtc.Value.ToLocalTime().ToString("dd/MM/yyyy HH:mm:ss");
            await using var lookupDb = new PharmacyDbContext(PharmacyDb.Options());
            gtinInput.Text = parsedPack.Gtin14;
            serialInput.Text = parsedPack.SerialNumber;
            expiryInput.Text = parsedPack.ExpiryDate.ToString("yyyy-MM-dd");
            lotInput.Text = parsedPack.BatchLot;
            var match = await lookupDb.MedicineBarcodes.Include(x => x.Medicine)
                .FirstOrDefaultAsync(x => x.BarcodeValue == parsedPack.Gtin14 ||
                    (!string.IsNullOrWhiteSpace(parsedPack.ResolvedEofBarcode) && x.BarcodeValue == parsedPack.ResolvedEofBarcode));
            var medicine = match?.Medicine ?? await lookupDb.Medicines.FirstOrDefaultAsync(x =>
                x.Barcode == parsedPack.Gtin14 ||
                (!string.IsNullOrWhiteSpace(parsedPack.ResolvedEofBarcode) && x.Barcode == parsedPack.ResolvedEofBarcode));
            if (medicine is not null && string.IsNullOrWhiteSpace(medicineNameInput.Text)) medicineNameInput.Text = medicine.Name;
            if (string.IsNullOrWhiteSpace(eofInput.Text)) eofInput.Text = parsedPack.ResolvedEofBarcode ?? string.Empty;
            scanStatus.Foreground = Brushes.LightGreen;
            scanStatus.Text = medicine is null
                ? "GS1 scan parsed. No catalogue match was found; enter the medicine name."
                : $"GS1 scan parsed. Medicine matched: {medicine.Name}.";
        };

        var form = new StackPanel { Spacing = 7, Margin = new Thickness(16) };
        form.Children.Add(new TextBlock
        {
            Text = isEdit ? "EDIT BORROWING" : "ADD MEDICINE / BORROWING",
            FontSize = 19, FontWeight = FontWeight.Bold
        });
        if (!isEdit)
        {
            form.Children.Add(scanPanel);
        }
        else
        {
            var scanInfo = new TextBlock
            {
                Text = existing!.SerializedPackScan is null
                    ? "This is a legacy borrowing. Its original barcode information is preserved."
                    : $"GS1 pack: GTIN {existing.SerializedPackScan.Gtin14} | Serial {existing.SerializedPackScan.SerialNumber}. The original scan is read-only.",
                TextWrapping = TextWrapping.Wrap, Foreground = Brushes.LightGray
            };
            form.Children.Add(scanInfo);

            var rawBarcodeGroup = new StackPanel { Spacing = 4, Margin = new Thickness(0, 3) };
            rawBarcodeGroup.Children.Add(new TextBlock
            {
                Text = "Raw barcode",
                Foreground = Brushes.LightGray,
                FontSize = 12
            });
            rawBarcodeGroup.Children.Add(new TextBox
            {
                Text = (existing.SerializedPackScan?.RawGs1String ?? string.Empty)
                    .Replace(Gs1DataMatrixParser.GroupSeparator.ToString(), "<GS>", StringComparison.Ordinal),
                IsReadOnly = true,
                AcceptsReturn = true,
                TextWrapping = TextWrapping.Wrap,
                MinHeight = 48,
                MaxHeight = 75,
                FontFamily = new FontFamily("Consolas")
            });
            form.Children.Add(rawBarcodeGroup);

            var medicineNameGroup = new StackPanel { Spacing = 4, Margin = new Thickness(0, 3) };
            medicineNameGroup.Children.Add(new TextBlock { Text = "Medicine name", Foreground = Brushes.LightGray, FontSize = 12 });
            medicineNameGroup.Children.Add(medicineNameInput);
            form.Children.Add(medicineNameGroup);
        }
        AddField(form, "Quantity", "Quantity", (existing?.Quantity ?? 1).ToString());

        var scanDateGroup = new StackPanel { Spacing = 4, Margin = new Thickness(0, 3) };
        scanDateGroup.Children.Add(new TextBlock { Text = "Scan / borrowing date", Foreground = Brushes.LightGray, FontSize = 12 });
        scanDateGroup.Children.Add(scanDateText);
        form.Children.Add(scanDateGroup);
        AddField(form, "Notes", "Notes", existing?.Notes);

        var status = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = Brushes.Orange };
        var save = new Button { Content = "Save", Classes = { "accent" }, MinWidth = 110 };
        var cancel = new Button { Content = "Cancel", MinWidth = 100 };
        var dialog = new Window
        {
            Title = isEdit ? "Edit borrowing" : "Add medicine",
            Width = 620, Height = 780, MinWidth = 520, MinHeight = 560,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = true,
            Content = new Grid
            {
                RowDefinitions = CreateRows(GridLength.Star, GridLength.Auto),
                Children =
                {
                    new ScrollViewer { Content = form, VerticalScrollBarVisibility = ScrollBarVisibility.Auto },
                    new StackPanel { Orientation = Orientation.Vertical, Spacing = 8, Margin = new Thickness(16), Children = { status, new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8, Children = { save, cancel } } } }
                }
            }
        };
        Grid.SetRow(((Grid)dialog.Content).Children[1], 1);
        cancel.Click += (_, _) => dialog.Close();
        save.Click += async (_, _) =>
        {
            var name = medicineNameInput.Text?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(name))
            {
                status.Text = "Medicine name is required.";
                return;
            }
            if (!int.TryParse(fields["Quantity"].Text, out var quantity) || quantity < 1)
            {
                status.Text = "Enter a valid quantity.";
                return;
            }
            var correctedGtin = gtinInput.Text?.Trim() ?? string.Empty;
            var correctedSerial = serialInput.Text?.Trim() ?? string.Empty;
            var correctedLot = lotInput.Text?.Trim() ?? string.Empty;
            DateTime? correctedExpiry = null;
            if (parsedPack is not null)
            {
                if (correctedGtin.Length != 14 || correctedGtin.Any(ch => !char.IsDigit(ch)))
                { status.Text = "GTIN must contain exactly 14 digits."; return; }
                if (string.IsNullOrWhiteSpace(correctedSerial) || correctedSerial.Length > 20)
                { status.Text = "Enter a serial number of up to 20 characters."; return; }
                if (!DateTime.TryParse(expiryInput.Text, out var parsedExpiry))
                { status.Text = "Enter a valid expiry date (yyyy-MM-dd)."; return; }
                correctedExpiry = parsedExpiry.Date;
                if (correctedLot.Length > 20)
                { status.Text = "Batch / Lot must not exceed 20 characters."; return; }
                if (quantity != 1)
                { status.Text = "A serialized DataMatrix identifies one physical pack. Quantity must be 1."; return; }
            }
            await using var db = new PharmacyDbContext(PharmacyDb.Options());
            if (isEdit)
            {
                var record = await db.Borrowings.FirstOrDefaultAsync(x => x.Id == existing!.Id && x.CustomerId == selectedCustomerId.Value);
                if (record is null) { status.Text = "The borrowing record could not be found."; return; }
                record.MedicineName = name;
                record.Quantity = quantity;
                                                record.Notes = fields["Notes"].Text?.Trim();
            }
            else
            {
                SerializedPackScan? serializedPack = null;
                if (parsedPack is not null)
                {
                    if (await db.SerializedPackScans.AnyAsync(x => x.Gtin14 == correctedGtin && x.SerialNumber == correctedSerial))
                    { status.Text = "This physical pack (GTIN + serial number) has already been scanned."; return; }
                    var eof = string.IsNullOrWhiteSpace(eofInput.Text) ? parsedPack.ResolvedEofBarcode : eofInput.Text.Trim();
                    if (!string.IsNullOrWhiteSpace(eof) && (eof.Length != 13 || eof.Any(ch => !char.IsDigit(ch))))
                    { status.Text = "The EOF / NTIN barcode must contain exactly 13 digits."; return; }
                    var matched = await db.MedicineBarcodes.Include(x => x.Medicine)
                        .Where(x => x.BarcodeValue == correctedGtin || (!string.IsNullOrWhiteSpace(eof) && x.BarcodeValue == eof))
                        .Select(x => x.Medicine).FirstOrDefaultAsync()
                        ?? await db.Medicines.FirstOrDefaultAsync(x => x.Barcode == correctedGtin || (!string.IsNullOrWhiteSpace(eof) && x.Barcode == eof));
                    if (matched is null)
                    {
                        matched = new MedicineCatalogItem { Name = name, Barcode = correctedGtin, IsActive = true };
                        db.Medicines.Add(matched);
                        await db.SaveChangesAsync();
                    }
                    foreach (var mapping in new[] { (Value: correctedGtin, Type: "GTIN14"), (Value: eof, Type: "EOF_EAN13") })
                    {
                        if (string.IsNullOrWhiteSpace(mapping.Value)) continue;
                        var current = await db.MedicineBarcodes.FirstOrDefaultAsync(x => x.BarcodeValue == mapping.Value);
                        if (current is not null && current.MedicineId != matched.Id)
                        { status.Text = "This barcode is already assigned to another medicine."; return; }
                        if (current is null)
                            db.MedicineBarcodes.Add(new MedicineBarcode { MedicineId = matched.Id, BarcodeValue = mapping.Value, BarcodeType = mapping.Type });
                    }
                    serializedPack = new SerializedPackScan
                    {
                        RawGs1String = scanInput.Text ?? string.Empty, Gtin14 = correctedGtin,
                        EofEan13 = string.IsNullOrWhiteSpace(eof) ? null : eof,
                        SerialNumber = correctedSerial, ExpiryDate = correctedExpiry!.Value,
                        BatchLot = correctedLot, HmvoStatus = SerializedPackStatuses.Active,
                        ScannedAt = scanCapturedAtUtc ?? DateTime.UtcNow, MedicineId = matched.Id
                    };
                }
                db.Borrowings.Add(new BorrowingRecord
                {
                    CustomerId = selectedCustomerId.Value, MedicineName = name, OriginalBarcode = parsedPack is null ? null : correctedGtin,
                    SerializedPackScan = serializedPack, Quantity = quantity, InternalBarcode = "BOR-" + Guid.NewGuid().ToString("N"),
                    BorrowedAt = scanCapturedAtUtc?.ToLocalTime() ?? DateTime.Now, Notes = fields["Notes"].Text?.Trim()
                });
            }
            await db.SaveChangesAsync();
            dialog.Close();
            SetActivityStatus(isEdit ? "Borrowing updated." : "Medicine borrowing added.");
            await RefreshCustomerWorkspaceAsync();
        };
        AttachDialogLocalization(dialog);
        await dialog.ShowDialog(this);
    }

    private void ShowSelectedBorrowingScanDetails()
    {
        // Render only from the already-loaded record; do not query or reload data here.
        var index = borrowingList.SelectedIndex;
        if (index < 0 || index >= currentBorrowings.Count)
        {
            borrowingSelectionDetails.Text = "Select an existing borrowing to view its saved GS1 scan details.";
            borrowingBarcodeImage.Source = null;
            borrowingBarcodeImage.IsVisible = false;
            return;
        }

        var borrowing = currentBorrowings[index];
        var scan = borrowing.SerializedPackScan;
        if (scan is null)
        {
            borrowingSelectionDetails.Text = $"Selected borrowing: {borrowing.MedicineName}\nThis is a legacy borrowing record and has no saved GS1 DataMatrix scan.";
            borrowingBarcodeImage.Source = null;
            borrowingBarcodeImage.IsVisible = false;
            return;
        }

        borrowingSelectionDetails.Text =
            $"GS1 PACK DETAILS\n" +
            $"Medicine: {borrowing.MedicineName}\n" +
            $"GTIN-14: {scan.Gtin14}\n" +
            $"Serial number: {scan.SerialNumber}\n" +
            $"Expiry date: {scan.ExpiryDate:dd/MM/yyyy}\n" +
            $"Batch / lot: {scan.BatchLot}\n" +
            $"EOF / NTIN barcode: {scan.EofEan13 ?? "—"}\n" +
            $"Pack status: {scan.HmvoStatus}\n" +
            $"Scanned at: {scan.ScannedAt:dd/MM/yyyy HH:mm:ss} UTC\n" +
            $"Original scanner payload: {scan.RawGs1String}";

        try
        {
            borrowingBarcodeImage.Source = CreateGs1DataMatrixBitmap(scan.RawGs1String);
            borrowingBarcodeImage.IsVisible = true;
        }
        catch (Exception ex)
        {
            borrowingBarcodeImage.Source = null;
            borrowingBarcodeImage.IsVisible = false;
            borrowingSelectionDetails.Text += $"\n\nBarcode preview could not be generated: {ex.Message}";
        }
    }

    private void ShowBarcodePopup(string medicineName, WriteableBitmap barcodeBitmap)
    {
        var popup = barcodePopupWindow;
        if (popup is null)
        {
            popup = new BarcodeDisplayWindow(medicineName, barcodeBitmap);
            barcodePopupWindow = popup;
            popup.Closed += (_, _) => barcodePopupWindow = null;
            popup.Show(this);
        }
        else
        {
            popup.UpdateBarcode(medicineName, barcodeBitmap);
            if (!popup.IsVisible)
                popup.Show(this);
            popup.Activate();
        }

        popup.Topmost = true;
    }

    private static WriteableBitmap CreateGs1DataMatrixBitmap(string rawPayload)
    {
        var hints = new Dictionary<EncodeHintType, object>
        {
            [EncodeHintType.DATA_MATRIX_COMPACT] = true,
            [EncodeHintType.GS1_FORMAT] = true,
            [EncodeHintType.MARGIN] = 4
        };
        var matrix = new DataMatrixWriter().encode(rawPayload, BarcodeFormat.DATA_MATRIX, 0, 0, hints);
        const int scale = 5;
        var width = matrix.Width * scale;
        var height = matrix.Height * scale;
        var bitmap = new WriteableBitmap(new PixelSize(width, height), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Opaque);
        using (var framebuffer = bitmap.Lock())
        {
            var pixels = new byte[framebuffer.RowBytes * height];
            for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
            {
                var value = matrix[x / scale, y / scale] ? (byte)0 : (byte)255;
                var offset = y * framebuffer.RowBytes + x * 4;
                pixels[offset] = value; pixels[offset + 1] = value; pixels[offset + 2] = value; pixels[offset + 3] = 255;
            }
            Marshal.Copy(pixels, 0, framebuffer.Address, pixels.Length);
        }
        return bitmap;
    }
    private void UpdateGs1ScanEcho()
    {
        if (gs1ScanEcho is null || gs1ScanInput is null) return;

        var raw = gs1ScanInput.Text ?? string.Empty;
        gs1ScanEcho.Text = string.IsNullOrEmpty(raw)
            ? string.Empty
            : raw.Replace(Gs1DataMatrixParser.GroupSeparator.ToString(), "<GS>", StringComparison.Ordinal);
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
        UpdateGs1ScanEcho();
        if (!Gs1DataMatrixParser.TryParse(raw, out var parsed, out var error) || parsed is null)
        {
            currentParsedPack = null;
            gs1ScanStatus.Foreground = Brushes.OrangeRed;
            gs1ScanStatus.Text = Tr.Loc.TranslateText(error);
            ClearGs1ParsedFields();
            return;
        }

        currentParsedPack = parsed;
        gs1EofEan13Input.Text = string.Empty;
        if (activityFields.TryGetValue("BorrowingName", out var scannedNameBox))
            scannedNameBox.Text = string.Empty;
        gs1ParsedFields["Gtin14"].Text = parsed.Gtin14;
        gs1ParsedFields["SerialNumber"].Text = parsed.SerialNumber;
        gs1ParsedFields["ExpiryDate"].Text = parsed.ExpiryDate.ToString("dd/MM/yyyy");
        gs1ParsedFields["BatchLot"].Text = parsed.BatchLot;
        gs1ParsedFields["Nhrn715"].Text = parsed.Nhrn715 ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(parsed.ResolvedEofBarcode))
            gs1EofEan13Input.Text = parsed.ResolvedEofBarcode;
        gs1ParsedFields["RawPayload"].Text = raw.Replace(Gs1DataMatrixParser.GroupSeparator.ToString(), "<GS>", StringComparison.Ordinal);
        gs1ScanStatus.Foreground = Brushes.LightGreen;
        gs1ScanStatus.Text = Tr.Loc.TranslateText("GS1 scan parsed successfully. Review the extracted details before saving the borrowing.");

        await using var db = new PharmacyDbContext(PharmacyDb.Options());
        var alreadyScanned = await db.SerializedPackScans.AnyAsync(x => x.Gtin14 == parsed.Gtin14 && x.SerialNumber == parsed.SerialNumber);
        if (alreadyScanned)
        {
            gs1ScanStatus.Foreground = Brushes.OrangeRed;
            gs1ScanStatus.Text = Tr.Loc.TranslateText("This GTIN + serial number has already been registered. This physical pack cannot be added again.");
            return;
        }

        var mappedBarcode = await db.MedicineBarcodes
            .Include(x => x.Medicine)
            .FirstOrDefaultAsync(x => x.BarcodeValue == parsed.Gtin14 ||
                (!string.IsNullOrWhiteSpace(parsed.ResolvedEofBarcode) && x.BarcodeValue == parsed.ResolvedEofBarcode));
        var medicine = mappedBarcode?.Medicine
            ?? await db.Medicines.FirstOrDefaultAsync(x => x.Barcode == parsed.Gtin14 ||
                (!string.IsNullOrWhiteSpace(parsed.ResolvedEofBarcode) && x.Barcode == parsed.ResolvedEofBarcode));

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
            gs1ScanStatus.Text = Tr.Loc.TranslateText(gs1ScanStatus.Text + $" Medicine matched: {medicine.Name}.");
        }
        else
        {
            gs1ParsedFields["MedicineMatch"].Text = Tr.Loc.TranslateText("Not matched — enter medicine manually");
            gs1ScanStatus.Text = Tr.Loc.TranslateText(gs1ScanStatus.Text + " No medicine catalogue match yet; you can enter the medicine name and EOF barcode manually.");
        }
    }

    private void ClearGs1ParsedFields()
    {
        foreach (var field in gs1ParsedFields.Values) field.Text = string.Empty;
        if (gs1ParsedFields.TryGetValue("HmvoStatus", out var status)) status.Text = Tr.Loc.TranslateText(SerializedPackStatuses.Active);
        if (gs1ParsedFields.TryGetValue("MedicineMatch", out var match)) match.Text = Tr.Loc.TranslateText("Not matched");
    }

    private TabItem BuildCustomerHistoryTab()
    {
        customerHistoryList = new ListBox { MinHeight = 240 };
        return new TabItem
        {
            Header = "History",
            Content = new StackPanel
            {
                Spacing = 10,
                Margin = new Thickness(8),
                Children =
                {
                    new TextBlock { Text = "CUSTOMER MEDICINE & PRESCRIPTION HISTORY", FontSize = 18, FontWeight = FontWeight.Bold },
                    new TextBlock { Text = "All borrowing records (including returned items) and prescriptions, ordered by date.", TextWrapping = TextWrapping.Wrap, Foreground = Brushes.LightGray },
                    customerHistoryList
                }
            }
        };
    }

    private TabItem BuildSubscriptionsTab()
    {
        subscriptionList = new ListBox { MinHeight = 220 };
        var add = new Button { Content = "＋ Add subscription", Classes = { "accent" }, MinWidth = 145, MinHeight = 38 };
        add.Click += async (_, _) => await OpenSubscriptionEditorAsync(null);
        var edit = new Button { Content = "Edit selected", MinWidth = 125, MinHeight = 38 };
        edit.Click += async (_, _) =>
        {
            if (subscriptionList.SelectedIndex < 0 || subscriptionList.SelectedIndex >= currentSubscriptions.Count)
            { SetActivityStatus("Select a subscription to edit."); return; }
            await OpenSubscriptionEditorAsync(currentSubscriptions[subscriptionList.SelectedIndex]);
        };
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { add, edit } };
        return new TabItem { Header = "Subscriptions", Content = new StackPanel { Spacing = 10, Margin = new Thickness(8), Children =
        {
            new TextBlock { Text = "CUSTOMER SUBSCRIPTIONS", FontSize = 18, FontWeight = FontWeight.Bold },
            actions, subscriptionList
        } } };
    }

    private TabItem BuildPaymentsTab()
    {
        paymentList = new ListBox { MinHeight = 220 };
        var add = new Button { Content = "＋ Record payment", Classes = { "accent" }, MinWidth = 145, MinHeight = 38 };
        add.Click += async (_, _) => await OpenPaymentEditorAsync(null);
        var edit = new Button { Content = "Edit selected", MinWidth = 125, MinHeight = 38 };
        edit.Click += async (_, _) =>
        {
            if (paymentList.SelectedIndex < 0 || paymentList.SelectedIndex >= currentPayments.Count)
            { SetActivityStatus("Select a payment to edit."); return; }
            await OpenPaymentEditorAsync(currentPayments[paymentList.SelectedIndex]);
        };
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { add, edit } };
        return new TabItem { Header = "Payments", Content = new StackPanel { Spacing = 10, Margin = new Thickness(8), Children =
        {
            new TextBlock { Text = "CUSTOMER PAYMENTS", FontSize = 18, FontWeight = FontWeight.Bold },
            actions, paymentList
        } } };
    }

    private TabItem BuildLedgerTab()
    {
        ledgerList = new ListBox { MinHeight = 220 };
        var add = new Button { Content = "＋ Add ledger entry", Classes = { "accent" }, MinWidth = 145, MinHeight = 38 };
        add.Click += async (_, _) => await OpenLedgerEditorAsync(null);
        var edit = new Button { Content = "Edit selected", MinWidth = 125, MinHeight = 38 };
        edit.Click += async (_, _) =>
        {
            if (ledgerList.SelectedIndex < 0 || ledgerList.SelectedIndex >= currentLedgerEntries.Count)
            { SetActivityStatus("Select a ledger entry to edit."); return; }
            await OpenLedgerEditorAsync(currentLedgerEntries[ledgerList.SelectedIndex]);
        };
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { add, edit } };
        return new TabItem { Header = "Ledger", Content = new StackPanel { Spacing = 10, Margin = new Thickness(8), Children =
        {
            new TextBlock { Text = "CUSTOMER ACCOUNT LEDGER", FontSize = 18, FontWeight = FontWeight.Bold },
            actions, ledgerList
        } } };
    }

    private void AddActivityField(StackPanel parent, string label, string key, string? defaultValue = null)
    {
        var input = new TextBox { PlaceholderText = label, Text = defaultValue ?? string.Empty, MinHeight = 32 };
        activityFields[key] = input;
        parent.Children.Add(input);
    }

    private string ActivityValue(string key)
    {
        var value = activityFields.TryGetValue(key, out var box) ? box.Text?.Trim() ?? string.Empty : string.Empty;
        if (Tr.Loc.CurrentLanguage != AppLanguage.Greek) return value;

        return key switch
        {
            "SubscriptionFrequency" when value == "Μηνιαία" => "Monthly",
            "PaymentMethod" when value == "Μετρητά" => "Cash",
            "PaymentMethod" when value == "Κάρτα" => "Card",
            "PaymentMethod" when value == "Μεταφορά" => "Transfer",
            "LedgerType" when value == "Χρέωση" => "Charge",
            "LedgerType" when value == "Προσαρμογή" => "Adjustment",
            "LedgerType" when value == "Αντιστροφή" => "Reversal",
            _ => value
        };
    }

    private async Task RefreshCustomerWorkspaceAsync()
    {
        if (!selectedCustomerId.HasValue) return;
        var id = selectedCustomerId.Value;
        await using var db = new PharmacyDbContext(PharmacyDb.Options());
        var customer = await db.Customers.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id);
        if (customer is null) return;
        var allBorrowings = await db.Borrowings.AsNoTracking().Include(x => x.SerializedPackScan).Where(x => x.CustomerId == id).OrderByDescending(x => x.BorrowedAt).ToListAsync();
        var borrowings = allBorrowings.Where(x => x.Status == BorrowingStatus.Borrowed).ToList();
        var prescriptions = await db.Prescriptions.AsNoTracking().Where(x => x.CustomerId == id).OrderByDescending(x => x.CreatedAt).ToListAsync();
        var subscriptions = await db.Subscriptions.AsNoTracking().Where(x => x.CustomerId == id).OrderByDescending(x => x.StartDate).ToListAsync();
        var payments = await db.Payments.AsNoTracking().Where(x => x.CustomerId == id).OrderByDescending(x => x.PaidAt).ToListAsync();
        var ledger = await db.LedgerEntries.AsNoTracking().Where(x => x.CustomerId == id).OrderByDescending(x => x.EntryDate).ToListAsync();
        var charges = ledger.Where(x => x.Type == LedgerEntryType.Charge || x.Type == LedgerEntryType.Adjustment).Sum(x => x.Amount);
        var credits = ledger.Where(x => x.Type == LedgerEntryType.Payment || x.Type == LedgerEntryType.Reversal).Sum(x => x.Amount);
        var balance = charges - credits;
        customerOverview.Text = $"{customer.CustomerNumber}  |  {customer.FullName}\n{customer.Address}, {customer.City} {customer.Postcode}\nTelephone: {customer.Telephone ?? customer.Phone}  |  Mobile: {customer.Mobile}\nEmail: {customer.Email}\n\nBORROWINGS: {allBorrowings.Count} total, {borrowings.Count} currently borrowed\nSUBSCRIPTIONS: {subscriptions.Count(x => x.Status == SubscriptionStatus.Active)} active of {subscriptions.Count}\nPAYMENTS: {payments.Count} recorded  |  Total paid: {payments.Sum(x => x.Amount):C}\nACCOUNT BALANCE: {balance:C}\n\nRecent activity:\n{string.Join("\n", ledger.Take(5).Select(x => $"{x.EntryDate:dd/MM/yyyy}  {x.Type}  {x.Amount:C}  {x.Description}"))}";
        currentBorrowings = borrowings;
        borrowingList.ItemsSource = borrowings.Select(x =>
            x.SerializedPackScan is null
                ? $"{x.BorrowedAt:dd/MM/yyyy HH:mm} | {x.MedicineName} | Qty {x.Quantity}"
                : $"{x.SerializedPackScan.ScannedAt:dd/MM/yyyy HH:mm} | {x.MedicineName} | SN {x.SerializedPackScan.SerialNumber} | GTIN {x.SerializedPackScan.Gtin14} | Exp {x.SerializedPackScan.ExpiryDate:dd/MM/yyyy} | Lot {x.SerializedPackScan.BatchLot}")
            .ToList();
        customerHistoryList.ItemsSource = allBorrowings.Select(x => new
            {
                Date = x.SerializedPackScan?.ScannedAt ?? x.BorrowedAt,
                Text = $"{x.SerializedPackScan?.ScannedAt ?? x.BorrowedAt:dd/MM/yyyy HH:mm} | BORROWING | {x.MedicineName} | Qty {x.Quantity} | {x.Status}"
            }).Concat(prescriptions.Select(x => new
            {
                Date = x.CreatedAt,
                Text = $"{x.CreatedAt:dd/MM/yyyy HH:mm} | PRESCRIPTION | {x.MedicineName} | No. {x.PrescriptionNumber ?? "-"} | {x.Status} | Valid until {x.ExpiryDate:dd/MM/yyyy}"
            })).OrderByDescending(x => x.Date).Select(x => x.Text).ToList();
        currentSubscriptions = subscriptions;
        currentPayments = payments;
        currentLedgerEntries = ledger;
        subscriptionList.ItemsSource = subscriptions.Select(x => $"{x.Title} | {x.Status} | Next: {x.NextDueDate:dd/MM/yyyy} | Expires: {x.ExpiryDate:dd/MM/yyyy} | {x.Frequency}").ToList();
        paymentList.ItemsSource = payments.Select(x => $"{x.PaidAt:dd/MM/yyyy HH:mm} | {x.Amount:C} | {x.Method} | {x.Reference}").ToList();
        ledgerList.ItemsSource = ledger.Select(x => $"{x.EntryDate:dd/MM/yyyy HH:mm} | {x.Type} | {x.Amount:C} | {x.Description}").ToList();
        ApplyTranslations();
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
            if (string.IsNullOrWhiteSpace(eofEan13))
                eofEan13 = parsedPack.ResolvedEofBarcode;
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

            if (matchedMedicine is not null)
            {
                // Persist both identifiers against the same medicine so future
                // scans of either the manufacturer GTIN or Greek EOF barcode resolve.
                var gtinMapping = await db.MedicineBarcodes.FirstOrDefaultAsync(x => x.BarcodeValue == parsedPack.Gtin14);
                if (gtinMapping is not null && gtinMapping.MedicineId != matchedMedicine.Id)
                {
                    SetActivityStatus("The scanned GTIN is already mapped to a different medicine. Resolve the catalogue barcode conflict first.");
                    return;
                }
                if (gtinMapping is null)
                    db.MedicineBarcodes.Add(new MedicineBarcode { MedicineId = matchedMedicine.Id, BarcodeValue = parsedPack.Gtin14, BarcodeType = "GTIN14" });

                if (!string.IsNullOrWhiteSpace(eofEan13))
                {
                    var eofMapping = await db.MedicineBarcodes.FirstOrDefaultAsync(x => x.BarcodeValue == eofEan13);
                    if (eofMapping is not null && eofMapping.MedicineId != matchedMedicine.Id)
                    {
                        SetActivityStatus("The EOF / NTIN barcode is already mapped to a different medicine. Resolve the catalogue barcode conflict first.");
                        return;
                    }
                    if (eofMapping is null)
                        db.MedicineBarcodes.Add(new MedicineBarcode { MedicineId = matchedMedicine.Id, BarcodeValue = eofEan13, BarcodeType = "EOF_EAN13" });
                }
            }

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
        UpdateGs1ScanEcho();
        if (gs1EofEan13Input is not null) gs1EofEan13Input.Text = string.Empty;
        currentParsedPack = null;
        ClearGs1ParsedFields();
        gs1ScanStatus.Text = Tr.Loc.TranslateText("Ready for the next GS1 DataMatrix scan.");
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

    private async Task OpenSubscriptionEditorAsync(Subscription? existing)
    {
        if (!selectedCustomerId.HasValue) { SetActivityStatus("Save or select a customer first."); return; }
        var isEdit = existing is not null;
        var title = new TextBox { Text = existing?.Title ?? "", PlaceholderText = "Subscription title", MinHeight = 34 };
        var start = new TextBox { Text = (existing?.StartDate ?? DateTime.Today).ToString("yyyy-MM-dd"), PlaceholderText = "Start date (yyyy-MM-dd)", MinHeight = 34 };
        var due = new TextBox { Text = existing?.NextDueDate.ToString("yyyy-MM-dd") ?? "", PlaceholderText = "Next due date (yyyy-MM-dd)", MinHeight = 34 };
        var expiry = new TextBox { Text = existing?.ExpiryDate?.ToString("yyyy-MM-dd") ?? "", PlaceholderText = "Expiry date (optional)", MinHeight = 34 };
        var frequency = new TextBox { Text = existing?.Frequency ?? "Monthly", PlaceholderText = "Frequency", MinHeight = 34 };
        var notes = new TextBox { Text = existing?.Notes ?? "", PlaceholderText = "Notes", MinHeight = 70, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap };
        var form = new StackPanel { Spacing = 7, Margin = new Thickness(16), Children =
        {
            new TextBlock { Text = isEdit ? "EDIT SUBSCRIPTION" : "ADD SUBSCRIPTION", FontSize = 19, FontWeight = FontWeight.Bold },
            new TextBlock { Text = "Subscription title" }, title,
            new TextBlock { Text = "Start date (yyyy-MM-dd)" }, start,
            new TextBlock { Text = "Next due date (yyyy-MM-dd)" }, due,
            new TextBlock { Text = "Expiry date (yyyy-MM-dd, optional)" }, expiry,
            new TextBlock { Text = "Frequency" }, frequency,
            new TextBlock { Text = "Notes" }, notes
        } };
        var status = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = Brushes.Orange };
        var save = new Button { Content = "Save", Classes = { "accent" }, MinWidth = 110 };
        var cancel = new Button { Content = "Cancel", MinWidth = 100 };
        var dialog = CreateActivityEditorWindow(isEdit ? "Edit subscription" : "Add subscription", form, status, save, cancel, 540, 650);
        cancel.Click += (_, _) => dialog.Close();
        save.Click += async (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(title.Text) || !DateTime.TryParse(start.Text, out var startDate) || !DateTime.TryParse(due.Text, out var dueDate))
            { status.Text = "Enter a title and valid start and next due dates."; return; }
            DateTime? expiryDate = null;
            if (!string.IsNullOrWhiteSpace(expiry.Text))
            {
                if (!DateTime.TryParse(expiry.Text, out var parsedExpiry)) { status.Text = "Enter a valid expiry date."; return; }
                expiryDate = parsedExpiry.Date;
            }
            await using var db = new PharmacyDbContext(PharmacyDb.Options());
            Subscription record;
            if (isEdit)
            {
                record = await db.Subscriptions.FirstOrDefaultAsync(x => x.Id == existing!.Id && x.CustomerId == selectedCustomerId.Value);
                if (record is null) { status.Text = "The subscription could not be found."; return; }
            }
            else
            {
                record = new Subscription { CustomerId = selectedCustomerId.Value, Status = SubscriptionStatus.Active };
                db.Subscriptions.Add(record);
            }
            record.Title = title.Text.Trim();
            record.StartDate = startDate.Date;
            record.NextDueDate = dueDate.Date;
            record.ExpiryDate = expiryDate;
            record.Frequency = string.IsNullOrWhiteSpace(frequency.Text) ? "Monthly" : frequency.Text.Trim();
            record.Notes = notes.Text?.Trim();
            await db.SaveChangesAsync();
            dialog.Close();
            SetActivityStatus(isEdit ? "Subscription updated." : "Subscription added.");
            await RefreshCustomerWorkspaceAsync();
        };
        await dialog.ShowDialog(this);
    }

    private async Task OpenPaymentEditorAsync(Payment? existing)
    {
        if (!selectedCustomerId.HasValue) { SetActivityStatus("Save or select a customer first."); return; }
        var isEdit = existing is not null;
        var amount = new TextBox { Text = existing?.Amount.ToString("0.00") ?? "", PlaceholderText = "Amount", MinHeight = 34 };
        var method = new TextBox { Text = existing?.Method ?? "Cash", PlaceholderText = "Method (Cash / Card / Transfer)", MinHeight = 34 };
        var reference = new TextBox { Text = existing?.Reference ?? "", PlaceholderText = "Reference", MinHeight = 34 };
        var paidAt = new TextBox { Text = (existing?.PaidAt ?? DateTime.Now).ToString("yyyy-MM-dd HH:mm"), PlaceholderText = "Payment date (yyyy-MM-dd HH:mm)", MinHeight = 34 };
        var notes = new TextBox { Text = existing?.Notes ?? "", PlaceholderText = "Notes", MinHeight = 70, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap };
        var form = new StackPanel { Spacing = 7, Margin = new Thickness(16), Children =
        {
            new TextBlock { Text = isEdit ? "EDIT PAYMENT" : "RECORD PAYMENT", FontSize = 19, FontWeight = FontWeight.Bold },
            new TextBlock { Text = "Amount" }, amount,
            new TextBlock { Text = "Method (Cash / Card / Transfer)" }, method,
            new TextBlock { Text = "Reference" }, reference,
            new TextBlock { Text = "Payment date (yyyy-MM-dd HH:mm)" }, paidAt,
            new TextBlock { Text = "Notes" }, notes
        } };
        var status = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = Brushes.Orange };
        var save = new Button { Content = "Save", Classes = { "accent" }, MinWidth = 110 };
        var cancel = new Button { Content = "Cancel", MinWidth = 100 };
        var dialog = CreateActivityEditorWindow(isEdit ? "Edit payment" : "Record payment", form, status, save, cancel, 540, 650);
        cancel.Click += (_, _) => dialog.Close();
        save.Click += async (_, _) =>
        {
            if (!decimal.TryParse(amount.Text, out var parsedAmount) || parsedAmount <= 0 || !DateTime.TryParse(paidAt.Text, out var parsedPaidAt))
            { status.Text = "Enter a positive amount and valid payment date."; return; }
            await using var db = new PharmacyDbContext(PharmacyDb.Options());
            Payment record;
            decimal oldAmount = 0;
            string? oldReference = null;
            if (isEdit)
            {
                record = await db.Payments.FirstOrDefaultAsync(x => x.Id == existing!.Id && x.CustomerId == selectedCustomerId.Value);
                if (record is null) { status.Text = "The payment could not be found."; return; }
                oldAmount = record.Amount;
                oldReference = record.Reference;
            }
            else
            {
                record = new Payment { CustomerId = selectedCustomerId.Value };
                db.Payments.Add(record);
            }
            record.Amount = parsedAmount;
            record.Method = string.IsNullOrWhiteSpace(method.Text) ? "Cash" : method.Text.Trim();
            record.Reference = reference.Text?.Trim();
            record.PaidAt = parsedPaidAt;
            record.Notes = notes.Text?.Trim();
            if (isEdit)
            {
                var linkedLedger = await db.LedgerEntries.FirstOrDefaultAsync(x => x.CustomerId == selectedCustomerId.Value &&
                    x.Type == LedgerEntryType.Payment && x.SourceReference == oldReference &&
                    x.Amount == oldAmount);
                if (linkedLedger is not null)
                {
                    linkedLedger.Amount = parsedAmount;
                    linkedLedger.EntryDate = parsedPaidAt;
                    linkedLedger.SourceReference = record.Reference;
                    linkedLedger.Description = $"Payment - {record.Method}";
                }
            }
            else
            {
                db.LedgerEntries.Add(new LedgerEntry
                {
                    CustomerId = selectedCustomerId.Value, Type = LedgerEntryType.Payment,
                    Amount = parsedAmount, EntryDate = parsedPaidAt,
                    Description = $"Payment - {record.Method}", SourceReference = record.Reference,
                    RecordedBy = viewModel.User.Username
                });
            }
            await db.SaveChangesAsync();
            dialog.Close();
            SetActivityStatus(isEdit ? "Payment updated." : "Payment recorded and ledger updated.");
            await RefreshCustomerWorkspaceAsync();
        };
        await dialog.ShowDialog(this);
    }

    private async Task OpenLedgerEditorAsync(LedgerEntry? existing)
    {
        if (!selectedCustomerId.HasValue) { SetActivityStatus("Save or select a customer first."); return; }
        var isEdit = existing is not null;
        var type = new ComboBox
        {
            MinHeight = 34,
            ItemsSource = new[] { "Charge", "Adjustment", "Reversal" },
            SelectedItem = existing?.Type.ToString() ?? "Charge"
        };
        var amount = new TextBox { Text = existing?.Amount.ToString("0.00") ?? "", PlaceholderText = "Amount", MinHeight = 34 };
        var description = new TextBox { Text = existing?.Description ?? "", PlaceholderText = "Description", MinHeight = 34 };
        var entryDate = new TextBox { Text = (existing?.EntryDate ?? DateTime.Now).ToString("yyyy-MM-dd HH:mm"), PlaceholderText = "Entry date (yyyy-MM-dd HH:mm)", MinHeight = 34 };
        var form = new StackPanel { Spacing = 7, Margin = new Thickness(16), Children =
        {
            new TextBlock { Text = isEdit ? "EDIT LEDGER ENTRY" : "ADD LEDGER ENTRY", FontSize = 19, FontWeight = FontWeight.Bold },
            new TextBlock { Text = "Entry type" }, type,
            new TextBlock { Text = "Amount (positive number)" }, amount,
            new TextBlock { Text = "Description" }, description,
            new TextBlock { Text = "Entry date (yyyy-MM-dd HH:mm)" }, entryDate
        } };
        var status = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = Brushes.Orange };
        var save = new Button { Content = "Save", Classes = { "accent" }, MinWidth = 110 };
        var cancel = new Button { Content = "Cancel", MinWidth = 100 };
        var dialog = CreateActivityEditorWindow(isEdit ? "Edit ledger entry" : "Add ledger entry", form, status, save, cancel, 540, 560);
        cancel.Click += (_, _) => dialog.Close();
        save.Click += async (_, _) =>
        {
            if (!Enum.TryParse<LedgerEntryType>(type.SelectedItem?.ToString(), true, out var parsedType) ||
                parsedType == LedgerEntryType.Payment ||
                !decimal.TryParse(amount.Text, out var parsedAmount) || parsedAmount <= 0 ||
                string.IsNullOrWhiteSpace(description.Text) ||
                !DateTime.TryParse(entryDate.Text, out var parsedDate))
            { status.Text = "Choose a valid entry type, positive amount, description and date."; return; }
            await using var db = new PharmacyDbContext(PharmacyDb.Options());
            LedgerEntry record;
            if (isEdit)
            {
                record = await db.LedgerEntries.FirstOrDefaultAsync(x => x.Id == existing!.Id && x.CustomerId == selectedCustomerId.Value);
                if (record is null) { status.Text = "The ledger entry could not be found."; return; }
                if (record.Type == LedgerEntryType.Payment)
                { status.Text = "Payment ledger entries must be edited from the Payments tab."; return; }
            }
            else
            {
                record = new LedgerEntry { CustomerId = selectedCustomerId.Value, RecordedBy = viewModel.User.Username };
                db.LedgerEntries.Add(record);
            }
            record.Type = parsedType;
            record.Amount = parsedAmount;
            record.Description = description.Text.Trim();
            record.EntryDate = parsedDate;
            await db.SaveChangesAsync();
            dialog.Close();
            SetActivityStatus(isEdit ? "Ledger entry updated." : "Ledger entry added.");
            await RefreshCustomerWorkspaceAsync();
        };
        await dialog.ShowDialog(this);
    }

    private static Window CreateActivityEditorWindow(string title, Control form, TextBlock status, Button save, Button cancel, double width, double height)
    {
        var content = new Grid
        {
            RowDefinitions = CreateRows(GridLength.Star, GridLength.Auto),
            Children =
            {
                new ScrollViewer { Content = form, VerticalScrollBarVisibility = ScrollBarVisibility.Auto },
                new StackPanel
                {
                    Spacing = 8, Margin = new Thickness(16),
                    Children =
                    {
                        status,
                        new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8, Children = { save, cancel } }
                    }
                }
            }
        };
        Grid.SetRow(content.Children[1], 1);
        var dialog = new Window
        {
            Title = title, Width = width, Height = height, MinWidth = 480, MinHeight = 420,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, CanResize = true, Content = content
        };
        AttachDialogLocalization(dialog);
        return dialog;
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
        customerStatus.Text = Tr.Loc.TranslateText(message);
        customerStatus.Foreground = Brushes.LightGreen;
    }

    private void AddCustomerField(Grid grid, string label, string key, int row, int column, int columnSpan = 1, bool multiline = false)
    {
        var panel = new StackPanel { Spacing = 4, Margin = new Thickness(5) };
        panel.Children.Add(new TextBlock { Text = label, Foreground = Brushes.LightGray, FontSize = 12 });
        var box = new TextBox { PlaceholderText = label, MinHeight = multiline ? 75 : 34, AcceptsReturn = multiline, TextWrapping = multiline ? TextWrapping.Wrap : TextWrapping.NoWrap };
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
        await db.SaveChangesAsync();
        selectedCustomerId = customer.Id;
        customerStatus.Foreground = Brushes.LightGreen;
        customerStatus.Text = string.Format(Tr.Loc["Customers.Saved"], customer.CustomerNumber);
        await SearchCustomersAsync();
        var savedItem = customerList.Items.OfType<CustomerListItem>().FirstOrDefault(item => item.Id == customer.Id);
        if (savedItem is not null)
            customerList.SelectedItem = savedItem;
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
        showingExpiryCalendar = false;
        pageHost.Content = new StackPanel { Margin = new Thickness(20), Children = { new TextBlock { Text = title, FontSize = 24, FontWeight = FontWeight.Bold }, new TextBlock { Text = Tr.Loc["Common.Queued"] } } };
    }
}