using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using System.Runtime.InteropServices;
using System.Globalization;
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
    private const uint KlfActivate = 0x00000001;
    private static IntPtr? previousBarcodeKeyboardLayout;

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr LoadKeyboardLayout(string pwszKLID, uint flags);

    [DllImport("user32.dll")]
    private static extern IntPtr GetKeyboardLayout(uint idThread);

    [DllImport("user32.dll")]
    private static extern IntPtr ActivateKeyboardLayout(IntPtr hkl, uint flags);

    private static void ActivateEnglishBarcodeKeyboard()
    {
        if (!OperatingSystem.IsWindows()) return;

        // Preserve the layout that was active before the first barcode focus.
        previousBarcodeKeyboardLayout ??= GetKeyboardLayout(0);
        var englishLayout = LoadKeyboardLayout("00000409", KlfActivate);
        if (englishLayout != IntPtr.Zero)
            ActivateKeyboardLayout(englishLayout, 0);
    }

    private static void RestoreBarcodeKeyboard()
    {
        if (!OperatingSystem.IsWindows() || previousBarcodeKeyboardLayout is not IntPtr previous)
            return;

        ActivateKeyboardLayout(previous, 0);
        previousBarcodeKeyboardLayout = null;
    }

    private readonly MainWindowViewModel viewModel;
    private ComboBox languageBox = null!;
    private bool isApplyingTranslations;
    private string? activeNavigationKey;
    private readonly ContentControl pageHost;
    private TextBlock dailySummary = new();
    private ListBox dayRecords = new();
    private StackPanel calendarPanel = new();
    private DateTime displayedMonth = new(DateTime.Today.Year, DateTime.Today.Month, 1);
    private TextBlock? calendarMonthTitle;
    private StackPanel calendarNotesPanel = new();
    private readonly Dictionary<DateTime, Border> calendarDateCells = new();
    private bool showingExpiryCalendar;
    private bool suppressCustomerSelectionChanged;
    private const string MenuDashboard = "Dashboard";
    private const string MenuCalendar = "ExpiryCalendar";
    private const string MenuCustomers = "Customers";
    private const string MenuBorrowing = "Borrowing";
    private const string MenuSubscriptions = "Subscriptions";
    private const string MenuPayments = "Payments";
    private const string MenuReports = "Reports";
    private static readonly (string Key, string LabelKey)[] ConfigurableMenus =
    {
        (MenuDashboard, "Dashboard"),
        (MenuCalendar, "Navigation.ExpiryCalendar"),
        (MenuCustomers, "Navigation.Customers"),
        (MenuBorrowing, "Navigation.Borrowing"),
        (MenuSubscriptions, "Navigation.Subscriptions"),
        (MenuPayments, "Navigation.Payments"),
        (MenuReports, "Navigation.Reports")
    };
    private bool skipNextCustomerSearch;
    private readonly Dictionary<string, TextBox> customerFields = new();
    private ListBox customerList = null!;
    private TextBox customerSearch = null!;
    private TextBlock customerStatus = null!;
    private CheckBox customerActiveCheckBox = null!;
    private CheckBox showInactiveCustomersCheckBox = null!;
    private int? selectedCustomerId;

    public MainWindow(PharmacyUser user)
    {
        InitializeComponent();
        languageBox = this.FindControl<ComboBox>("LanguageBox") ?? throw new InvalidOperationException("LanguageBox was not found in MainWindow.axaml.");
        viewModel = new MainWindowViewModel(user);
        DataContext = viewModel;
        UserLabel.Text = viewModel.UserDisplay;
        ApplyCompanyBranding();
        Tr.Loc.LanguageChanged += (_, _) =>
        {
            BuildNavigation();
            ApplyTranslations();
            if (showingExpiryCalendar) _ = LoadSelectedDayAsync();
        };
        Opened += (_, _) => ApplyTranslations();
        ApplyTranslations();
        pageHost = PageHost;

        BuildNavigation();
        if (CanAccessMenu(MenuDashboard)) ShowDashboard();
        else if (CanAccessMenu(MenuCalendar)) ShowExpiryCalendar();
        else if (CanAccessMenu(MenuCustomers)) ShowCustomers();
        else if (CanAccessMenu(MenuReports)) ShowReports();
        else ShowNoMenuAccess();
    }

    private bool CanAccessMenu(string key)
    {
        if (viewModel.User.Role == PharmacyRole.Admin) return true;
        if (string.IsNullOrWhiteSpace(viewModel.User.AllowedMenus)) return true;
        return viewModel.User.AllowedMenus.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Contains(key, StringComparer.Ordinal);
    }

    private void BuildNavigation()
    {
        NavigationPanel.Children.Clear();
        NavigationPanel.Children.Add(new TextBlock
        {
            Text = Tr.Loc.TranslateText("WORKSPACE"),
            FontSize = 10,
            FontWeight = FontWeight.Bold,
            Foreground = new SolidColorBrush(Color.Parse("#8293A8")),
            Margin = new Thickness(4, 12, 0, 0)
        });

        foreach (var item in ConfigurableMenus.Where(item =>
                     item.Key != MenuReports &&
                     item.Key != MenuBorrowing &&
                     item.Key != MenuSubscriptions &&
                     item.Key != MenuPayments))
        {
            if (!CanAccessMenu(item.Key)) continue;
            Action action = item.Key switch
            {
                MenuDashboard => ShowDashboard,
                MenuCalendar => ShowExpiryCalendar,
                MenuCustomers => ShowCustomers,
                MenuBorrowing => () => ShowPlaceholder(Tr.Loc[item.LabelKey]),
                MenuSubscriptions => () => ShowPlaceholder(Tr.Loc[item.LabelKey]),
                MenuPayments => () => ShowPlaceholder(Tr.Loc[item.LabelKey]),
                _ => () => ShowPlaceholder(Tr.Loc[item.LabelKey])
            };
            var icon = item.Key switch
            {
                MenuDashboard => "⌂",
                MenuCalendar => "▦",
                MenuCustomers => "♙",
                MenuBorrowing => "▣",
                MenuSubscriptions => "◷",
                MenuPayments => "€",
                _ => "•"
            };
            AddNavigationButton(item.LabelKey, action, icon);
        }

        if (CanAccessMenu(MenuReports))
        {
            NavigationPanel.Children.Add(new TextBlock
            {
                Text = Tr.Loc.TranslateText("REPORTS"),
                FontSize = 10,
                FontWeight = FontWeight.Bold,
                Foreground = new SolidColorBrush(Color.Parse("#8293A8")),
                Margin = new Thickness(4, 12, 0, 0)
            });
            AddNavigationButton("Navigation.Reports", ShowReports, "▤");
        }

        if (viewModel.User.Role == PharmacyRole.Admin)
        {
            NavigationPanel.Children.Add(new TextBlock
            {
                Text = Tr.Loc.TranslateText("ADMINISTRATIVE"),
                FontSize = 10,
                FontWeight = FontWeight.Bold,
                Foreground = new SolidColorBrush(Color.Parse("#8293A8")),
                Margin = new Thickness(4, 12, 0, 0)
            });
            AddNavigationButton("Navigation.Users", () => _ = ShowUsersAsync(), "♟");
            AddNavigationButton("Company settings", OpenCompanySettings, "▣");
            AddNavigationButton("Weather settings", OpenWeatherSettings, "☼");
            AddNavigationButton("Navigation.ApplicationSettings", OpenApplicationSettings, "⚙");
            AddNavigationButton("Database synchronization", OpenCustomerDatabaseSync, "⇄");
        }

        // Keep the selected item highlighted when the menu is rebuilt (for example,
        // after changing the application language).
        var firstNavigationButton = NavigationPanel.Children.OfType<Button>().FirstOrDefault();
        activeNavigationKey ??= firstNavigationButton?.Tag as string;
        UpdateActiveNavigationVisual();
    }

    private void UpdateActiveNavigationVisual()
    {
        foreach (var button in NavigationPanel.Children.OfType<Button>())
        {
            var isSelected = string.Equals(button.Tag as string, activeNavigationKey, StringComparison.Ordinal);
            if (isSelected)
            {
                if (!button.Classes.Contains("selected")) button.Classes.Add("selected");
            }
            else
            {
                button.Classes.Remove("selected");
            }
        }
    }

    private void AddNavigationButton(string labelKey, Action action, string iconGlyph)
    {
        // Some sidebar labels are literal UI phrases stored in UiGreek rather
        // than resource keys. Resolve normal resource keys first, then translate
        // the literal phrase when the resource lookup returns its missing-key marker.
        var label = Tr.Loc[labelKey];
        if (label == $"[{labelKey}]")
            label = Tr.Loc.TranslateText(labelKey);

        var icon = new TextBlock
        {
            Text = iconGlyph,
            Width = 25,
            FontSize = 17,
            FontWeight = FontWeight.SemiBold,
            Foreground = new SolidColorBrush(Color.Parse("#91A9C4")),
            VerticalAlignment = VerticalAlignment.Center,
            TextAlignment = TextAlignment.Center
        };
        var caption = new TextBlock
        {
            Text = label,
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.Wrap
        };
        var content = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10,
            VerticalAlignment = VerticalAlignment.Center,
            Children = { icon, caption }
        };
        var button = new Button
        {
            Content = content,
            Classes = { "nav" },
            MinWidth = 190,
            HorizontalContentAlignment = HorizontalAlignment.Left,
            Tag = labelKey
        };
        button.Click += (_, _) =>
        {
            activeNavigationKey = labelKey;
            UpdateActiveNavigationVisual();
            action();
        };
        NavigationPanel.Children.Add(button);
    }

    private async void ShowDashboard()
    {
        if (!CanAccessMenu(MenuDashboard)) { ShowNoMenuAccess(); return; }
        showingExpiryCalendar = false;
        var root = new StackPanel { Margin = new Thickness(22), Spacing = 14 };
        root.Children.Add(new TextBlock { Text = Tr.Loc.TranslateText("Dashboard"), FontSize = 25, FontWeight = FontWeight.Bold });
        root.Children.Add(new TextBlock { Text = DateTime.Today.ToString("dddd, dd MMMM yyyy"), Foreground = Brushes.LightGray });

        var weatherRow = new Grid { ColumnDefinitions = CreateColumns(GridLength.Star, GridLength.Auto), Background = new SolidColorBrush(Color.Parse("#202733")), Margin = new Thickness(0, 2, 0, 4) };
        var weatherText = new TextBlock { Text = "Weather: loading…", VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(14, 12) };
        var weatherRefresh = new Button { Content = "↻", Margin = new Thickness(6), VerticalAlignment = VerticalAlignment.Center };
        weatherRow.Children.Add(weatherText);
        Grid.SetColumn(weatherRefresh, 1); weatherRow.Children.Add(weatherRefresh);
        root.Children.Add(new Border { Child = weatherRow, CornerRadius = new CornerRadius(9), BorderThickness = new Thickness(1), BorderBrush = new SolidColorBrush(Color.Parse("#354152")) });

        var cards = new Grid { ColumnDefinitions = CreateColumns(GridLength.Star, GridLength.Star), RowDefinitions = CreateRows(GridLength.Auto, GridLength.Auto) };
        var customersValue = new TextBlock { Text = "—", FontSize = 30, FontWeight = FontWeight.Bold };
        var activeValue = new TextBlock { Text = "—", FontSize = 30, FontWeight = FontWeight.Bold };
        var dueValue = new TextBlock { Text = "—", FontSize = 30, FontWeight = FontWeight.Bold };
        var overdueValue = new TextBlock { Text = "—", FontSize = 30, FontWeight = FontWeight.Bold };
        void AddCard(string label, TextBlock value, int column, int row, string accent)
        {
            var body = new StackPanel { Spacing = 5, Children =
            {
                new TextBlock { Text = Tr.Loc.TranslateText(label), Foreground = Brushes.LightGray, FontSize = 13 },
                value
            }};
            var card = new Border { Background = new SolidColorBrush(Color.Parse("#202733")), BorderBrush = new SolidColorBrush(Color.Parse(accent)), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10), Padding = new Thickness(18), Margin = new Thickness(4), Child = body };
            Grid.SetColumn(card, column); Grid.SetRow(card, row); cards.Children.Add(card);
        }
        AddCard("Total customers", customersValue, 0, 0, "#397DA5");
        AddCard("Active subscriptions", activeValue, 1, 0, "#397DA5");
        AddCard("Subscriptions due today", dueValue, 0, 1, "#D39A35");
        AddCard("Overdue open subscriptions", overdueValue, 1, 1, "#C65D5D");
        root.Children.Add(cards);

        var todayPanel = new Border { Background = new SolidColorBrush(Color.Parse("#202733")), BorderBrush = new SolidColorBrush(Color.Parse("#354152")), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10), Padding = new Thickness(16), Child = new StackPanel { Spacing = 7 } };
        var todayStack = (StackPanel)todayPanel.Child!;
        todayStack.Children.Add(new TextBlock { Text = Tr.Loc.TranslateText("Today's status"), FontSize = 17, FontWeight = FontWeight.SemiBold });
        var todaySummary = new TextBlock { Text = "Loading today's activity…", TextWrapping = TextWrapping.Wrap, Foreground = Brushes.LightGray };
        todayStack.Children.Add(todaySummary);
        root.Children.Add(todayPanel);
        pageHost.Content = new ScrollViewer { Content = root, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };

        async Task RefreshWeatherAsync()
        {
            weatherRefresh.IsEnabled = false;
            try
            {
                var settings = WeatherSettingsService.Load();
                var snapshot = await WeatherSettingsService.GetCurrentAsync(settings.Location);
                var parts = new List<string> { "Weather" };
                if (settings.ShowLocation && !string.IsNullOrWhiteSpace(settings.Location)) parts.Add(settings.Location);
                if (settings.ShowTemperature) parts.Add($"{snapshot.Temperature:0.#} °C");
                if (settings.ShowCondition) parts.Add(WeatherSettingsService.Describe(snapshot.WeatherCode));
                if (settings.ShowFeelsLike) parts.Add($"Feels like {snapshot.FeelsLike:0.#} °C");
                if (settings.ShowHumidity) parts.Add($"Humidity {snapshot.Humidity}%");
                if (settings.ShowWind) parts.Add($"Wind {snapshot.WindSpeed:0.#} km/h");
                weatherText.Text = string.Join("   ·   ", parts);
            }
            catch (Exception ex) { weatherText.Text = ex.Message; }
            finally { weatherRefresh.IsEnabled = true; }
        }
        weatherRefresh.Click += async (_, _) => await RefreshWeatherAsync();
        await RefreshWeatherAsync();

        try
        {
            await using var db = new PharmacyDbContext(PharmacyDb.Options());
            var today = DateTime.Today;
            var tomorrow = today.AddDays(1);
            var customerCount = await db.Customers.CountAsync();
            var activeCount = await db.Subscriptions.CountAsync(item => item.Status == SubscriptionStatus.Active);
            var dueToday = await db.Subscriptions.CountAsync(item => item.Status == SubscriptionStatus.Active && item.NextDueDate >= today && item.NextDueDate < tomorrow);
            var overdue = await db.Subscriptions.CountAsync(item => item.Status == SubscriptionStatus.Active && item.NextDueDate < today);
            customersValue.Text = customerCount.ToString();
            activeValue.Text = activeCount.ToString();
            dueValue.Text = dueToday.ToString();
            overdueValue.Text = overdue.ToString();
            todaySummary.Text = $"{dueToday} subscription(s) due today. {overdue} overdue active subscription(s) remain open. {activeCount} active subscription(s) in total.";
        }
        catch (Exception ex)
        {
            todaySummary.Text = "Unable to load today's status: " + ex.Message;
        }
        ApplyTranslations();
    }

    private void ApplyCompanyBranding()
    {
        var settings = CompanySettingsService.Load();
        CompanyNameLabel.Text = string.IsNullOrWhiteSpace(settings.Name) ? "S Pharmacy Log" : settings.Name;
        CompanyLogo.Source = null;
        CompanyLogo.IsVisible = false;
        CompanyDefaultIcon.IsVisible = true;
        if (!string.IsNullOrWhiteSpace(settings.LogoFile) && File.Exists(settings.LogoFile))
        {
            try
            {
                CompanyLogo.Source = new Bitmap(settings.LogoFile);
                CompanyLogo.IsVisible = true;
                CompanyDefaultIcon.IsVisible = false;
            }
            catch { }
        }
    }

    private async void OpenCompanySettings()
    {
        if (viewModel.User.Role != PharmacyRole.Admin) return;
        var dialog = new CompanySettingsWindow();
        AttachDialogLocalization(dialog);
        var saved = await dialog.ShowDialog<bool>(this);
        if (saved) ApplyCompanyBranding();
    }

    private async void OpenWeatherSettings()
    {
        if (viewModel.User.Role != PharmacyRole.Admin) return;
        var dialog = new WeatherSettingsWindow();
        AttachDialogLocalization(dialog);
        var saved = await dialog.ShowDialog<bool>(this);
        if (saved)
            ShowDashboard();
    }

    private async void OpenApplicationSettings()
    {
        if (viewModel.User.Role != PharmacyRole.Admin) return;
        var dialog = new ApplicationSettingsWindow();
        AttachDialogLocalization(dialog);
        var saved = await dialog.ShowDialog<bool>(this);
        if (saved && showingExpiryCalendar && calendarMonthTitle is not null)
            await RenderCalendarAsync(calendarMonthTitle);
    }

    private async void OpenCustomerDatabaseSync()
    {
        if (viewModel.User.Role != PharmacyRole.Admin) return;
        var dialog = new CustomerDatabaseSyncWindow();
        AttachDialogLocalization(dialog);
        await dialog.ShowDialog(this);
    }

    private async void ShowReports()
    {
        if (!CanAccessMenu(MenuReports)) { ShowNoMenuAccess(); return; }
        showingExpiryCalendar = false;
        var dialog = new ReportsWindow();
        AttachDialogLocalization(dialog);
        await dialog.ShowDialog(this);
    }

    private void ShowNoMenuAccess()
    {
        showingExpiryCalendar = false;
        pageHost.Content = new StackPanel
        {
            Margin = new Thickness(22),
            Spacing = 8,
            Children =
            {
                new TextBlock { Text = Tr.Loc.TranslateText("No menu access"), FontSize = 22, FontWeight = FontWeight.Bold },
                new TextBlock { Text = Tr.Loc.TranslateText("Your account has no assigned workspace menus. Contact an administrator.") }
            }
        };
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
                case CheckBox checkBox when checkBox.Content is string checkText:
                    checkBox.Content = Tr.Loc.TranslateText(checkText);
                    break;
                case TextBox textBox when !string.IsNullOrEmpty(textBox.PlaceholderText):
                    textBox.PlaceholderText = Tr.Loc.TranslateText(textBox.PlaceholderText);
                    break;
                case TabItem tab when tab.Header is string header:
                    tab.Header = Tr.Loc.TranslateText(header);
                    break;
                case ComboBox comboBox when comboBox.ItemsSource is IEnumerable<string> options:
                {
                    var selected = comboBox.SelectedItem?.ToString();
                    var translatedOptions = options.Select(Tr.Loc.TranslateText).ToArray();
                    comboBox.ItemsSource = translatedOptions;
                    if (selected is not null)
                        comboBox.SelectedItem = Tr.Loc.TranslateText(selected);
                    break;
                }
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
        TranslateListItems(subscriptionHistoryList);
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
        UpdateCustomerTabsVisibility();
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
                    case CheckBox checkBox when checkBox.Content is string checkText:
                        checkBox.Content = Tr.Loc.TranslateText(checkText);
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
        if (!CanAccessMenu(MenuCalendar)) { ShowNoMenuAccess(); return; }
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
        content.Children.Add(new ScrollViewer { Content = calendarPanel, MaxHeight = 520, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        dayRecords.MaxHeight = 260;
        content.Children.Add(dailySummary);
        var addCalendarNoteButton = new Button
        {
            Content = Tr.Loc.TranslateText("Add note to selected date"),
            HorizontalAlignment = HorizontalAlignment.Left,
            MinWidth = 190
        };
        addCalendarNoteButton.Click += async (_, _) => await OpenCalendarNoteEditorAsync(SelectedCalendarDate);
        content.Children.Add(addCalendarNoteButton);
        calendarNotesPanel = new StackPanel { Spacing = 6 };
        content.Children.Add(calendarNotesPanel);
        content.Children.Add(dayRecords);
        pageHost.Content = content;
        ApplyTranslations();
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
        var dueSubscriptions = await db.Subscriptions.AsNoTracking()
            .Include(subscription => subscription.Customer)
            .Where(subscription => subscription.Status == SubscriptionStatus.Active &&
                subscription.NextDueDate >= start && subscription.NextDueDate < end)
            .OrderBy(subscription => subscription.Customer!.Surname)
            .ThenBy(subscription => subscription.Customer!.FirstName)
            .ToListAsync();
        var subscriptionsByDate = dueSubscriptions
            .GroupBy(subscription => subscription.NextDueDate.Date)
            .ToDictionary(group => group.Key, group => group.ToList());
        var currentUsername = viewModel.User.Username?.Trim() ?? string.Empty;
        var notesForMonth = await db.CalendarNotes.AsNoTracking()
            .Where(note => note.CalendarDate >= start && note.CalendarDate < end)
            .OrderBy(note => note.CalendarDate)
            .ThenBy(note => note.CreatedAt)
            .ToListAsync();
        var visibleNotes = notesForMonth
            .Where(note => note.IsShared || string.Equals(note.CreatedBy?.Trim(), currentUsername, StringComparison.OrdinalIgnoreCase))
            .ToList();
        var notesByDate = visibleNotes
            .GroupBy(note => note.CalendarDate.Date)
            .ToDictionary(group => group.Key, group => group.ToList());
        var showOfficialHolidays = ApplicationSettingsService.Load().ShowOfficialHolidays;
        var holidaysByDate = showOfficialHolidays
            ? GetGreekPublicHolidays(displayedMonth.Year)
            : new Dictionary<DateTime, string>();

        calendarPanel.Children.Clear();
        var grid = new Grid
        {
            ColumnDefinitions = CreateColumns(GridLength.Star, GridLength.Star, GridLength.Star, GridLength.Star, GridLength.Star, GridLength.Star, GridLength.Star),
            RowDefinitions = CreateRows(GridLength.Auto, GridLength.Auto, GridLength.Auto, GridLength.Auto, GridLength.Auto, GridLength.Auto)
        };
        calendarDateCells.Clear();
        var offset = ((int)displayedMonth.DayOfWeek + 6) % 7;
        var daysInMonth = DateTime.DaysInMonth(displayedMonth.Year, displayedMonth.Month);
        for (var cell = 0; cell < 42; cell++)
        {
            var dayNumber = cell - offset + 1;
            Control dayCell;
            if (dayNumber >= 1 && dayNumber <= daysInMonth)
            {
                var date = new DateTime(displayedMonth.Year, displayedMonth.Month, dayNumber);
                var expiryCount = expiryCounts.GetValueOrDefault(date);
                var dayContent = new StackPanel { Spacing = 3 };
                var dayHeader = new Button
                {
                    Padding = new Thickness(3),
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    HorizontalContentAlignment = HorizontalAlignment.Center,
                    Content = new StackPanel
                    {
                        Spacing = 1,
                        Children =
                        {
                            new TextBlock { Text = dayNumber.ToString(), FontWeight = FontWeight.Bold, HorizontalAlignment = HorizontalAlignment.Center },
                            new TextBlock
                            {
                                Text = expiryCount > 0 ? $"{expiryCount} {Tr.Loc["Calendar.Expiring"]}" : Tr.Loc["Calendar.None"],
                                FontSize = 10,
                                Foreground = expiryCount > 0 ? Brushes.Orange : Brushes.Gray,
                                HorizontalAlignment = HorizontalAlignment.Center
                            }
                        }
                    }
                };
                dayHeader.Click += async (_, _) =>
                {
                    SelectedCalendarDate = date;
                    foreach (var pair in calendarDateCells)
                    {
                        pair.Value.BorderThickness = pair.Key == date ? new Thickness(2) : new Thickness(1);
                        pair.Value.BorderBrush = pair.Key == date
                            ? new SolidColorBrush(Color.Parse("#21A1D8"))
                            : new SolidColorBrush(Color.Parse("#34495E"));
                        pair.Value.Background = pair.Key == date
                            ? new SolidColorBrush(Color.Parse("#243C52"))
                            : new SolidColorBrush(Color.Parse("#172536"));
                    }
                    await LoadSelectedDayAsync();
                };
                dayContent.Children.Add(dayHeader);

                if (notesByDate.TryGetValue(date, out var dayNotes))
                {
                    foreach (var note in dayNotes)
                    {
                        var noteButton = new Button
                        {
                            Content = "📝 " + note.Header,
                            FontSize = 10,
                            Padding = new Thickness(4, 2),
                            MinHeight = 23,
                            HorizontalAlignment = HorizontalAlignment.Stretch,
                            HorizontalContentAlignment = HorizontalAlignment.Left,
                            Background = new SolidColorBrush(Color.Parse("#263B4D")),
                            BorderBrush = new SolidColorBrush(Color.Parse(note.IsShared ? "#477B63" : "#477DA5")),
                            BorderThickness = new Thickness(1),
                            Foreground = note.IsShared ? Brushes.LightGreen : Brushes.LightSkyBlue
                        };
                        noteButton.Click += async (_, _) =>
                        {
                            SelectedCalendarDate = date;
                            foreach (var pair in calendarDateCells)
                            {
                                pair.Value.BorderThickness = pair.Key == date ? new Thickness(2) : new Thickness(1);
                                pair.Value.BorderBrush = pair.Key == date
                                    ? new SolidColorBrush(Color.Parse("#21A1D8"))
                                    : new SolidColorBrush(Color.Parse("#34495E"));
                                pair.Value.Background = pair.Key == date
                                    ? new SolidColorBrush(Color.Parse("#243C52"))
                                    : new SolidColorBrush(Color.Parse("#172536"));
                            }
                            await LoadSelectedDayAsync();
                            await OpenCalendarNoteEditorAsync(date, note);
                        };
                        dayContent.Children.Add(noteButton);
                    }
                }

                if (holidaysByDate.TryGetValue(date, out var holidayName))
                {
                    dayContent.Children.Add(new TextBlock
                    {
                        Text = "★ " + Tr.Loc.TranslateText(holidayName),
                        FontSize = 10,
                        FontWeight = FontWeight.SemiBold,
                        Foreground = new SolidColorBrush(Color.Parse("#F2C66D")),
                        TextWrapping = TextWrapping.Wrap
                    });
                }

                if (subscriptionsByDate.TryGetValue(date, out var daySubscriptions))
                {
                    foreach (var subscription in daySubscriptions)
                    {
                        var customer = subscription.Customer;
                        var subscriptionNumber = GetSubscriptionNumber(subscription);
                        var subscriptionBox = new Button
                        {
                            MinHeight = 34,
                            Padding = new Thickness(4, 3),
                            HorizontalContentAlignment = HorizontalAlignment.Stretch,
                            HorizontalAlignment = HorizontalAlignment.Stretch,
                            Background = new SolidColorBrush(Color.Parse("#203D55")),
                            BorderBrush = new SolidColorBrush(Color.Parse("#397DA5")),
                            BorderThickness = new Thickness(1),
                            Content = new StackPanel
                            {
                                Spacing = 1,
                                Children =
                                {
                                    new TextBlock
                                    {
                                        Text = $"{subscriptionNumber} | {customer?.CustomerNumber}",
                                        FontSize = 10,
                                        FontWeight = FontWeight.Bold,
                                        TextTrimming = TextTrimming.CharacterEllipsis
                                    },
                                    new TextBlock
                                    {
                                        Text = $"{customer?.FullName} · {subscription.Title}",
                                        FontSize = 10,
                                        TextTrimming = TextTrimming.CharacterEllipsis
                                    }
                                }
                            }
                        };
                        subscriptionBox.Click += async (_, _) => await OpenSubscriptionDetailsAsync(subscription.Id);
                        dayContent.Children.Add(subscriptionBox);
                    }
                }

                var dateCellBorder = new Border
                {
                    MinHeight = 86,
                    Margin = new Thickness(3),
                    Padding = new Thickness(3),
                    CornerRadius = new CornerRadius(6),
                    BorderThickness = date == SelectedCalendarDate ? new Thickness(2) : new Thickness(1),
                    BorderBrush = date == SelectedCalendarDate
                        ? new SolidColorBrush(Color.Parse("#21A1D8"))
                        : new SolidColorBrush(Color.Parse("#34495E")),
                    Background = date == SelectedCalendarDate
                        ? new SolidColorBrush(Color.Parse("#243C52"))
                        : new SolidColorBrush(Color.Parse("#172536")),
                    Child = dayContent
                };
                dateCellBorder.AddHandler(InputElement.PointerPressedEvent, async (_, e) =>
                {
                    var pointer = e.GetCurrentPoint(dateCellBorder);
                    if (pointer.Properties.IsRightButtonPressed || e.ClickCount >= 2)
                    {
                        e.Handled = true;
                        SelectedCalendarDate = date;
                        foreach (var pair in calendarDateCells)
                        {
                            pair.Value.BorderThickness = pair.Key == date ? new Thickness(2) : new Thickness(1);
                            pair.Value.BorderBrush = pair.Key == date
                                ? new SolidColorBrush(Color.Parse("#21A1D8"))
                                : new SolidColorBrush(Color.Parse("#34495E"));
                            pair.Value.Background = pair.Key == date
                                ? new SolidColorBrush(Color.Parse("#243C52"))
                                : new SolidColorBrush(Color.Parse("#172536"));
                        }
                        await LoadSelectedDayAsync();
                        await OpenCalendarNoteEditorAsync(date);
                    }
                }, RoutingStrategies.Bubble, handledEventsToo: true);
                dayCell = dateCellBorder;
                calendarDateCells[date] = dateCellBorder;
            }
            else
            {
                dayCell = new Border { MinHeight = 86, Margin = new Thickness(3) };
            }

            Grid.SetColumn(dayCell, cell % 7);
            Grid.SetRow(dayCell, cell / 7);
            grid.Children.Add(dayCell);
        }
        calendarPanel.Children.Add(grid);
        await LoadSelectedDayAsync();
    }

    private static Dictionary<DateTime, string> GetGreekPublicHolidays(int year)
    {
        var holidays = new Dictionary<DateTime, string>
        {
            [new DateTime(year, 1, 1)] = "New Year's Day",
            [new DateTime(year, 1, 6)] = "Epiphany",
            [new DateTime(year, 3, 25)] = "Greek Independence Day",
            [new DateTime(year, 5, 1)] = "Labour Day",
            [new DateTime(year, 8, 15)] = "Assumption of the Virgin Mary",
            [new DateTime(year, 10, 28)] = "Ohi Day",
            [new DateTime(year, 12, 25)] = "Christmas Day",
            [new DateTime(year, 12, 26)] = "Synaxis of the Mother of God"
        };

        // Orthodox Easter is calculated on the Julian calendar and converted
        // to the Gregorian calendar (13 days in 1900–2099, 14 days in 2100–2199).
        var a = year % 4;
        var b = year % 7;
        var c = year % 19;
        var d = (19 * c + 15) % 30;
        var e = (2 * a + 4 * b - d + 34) % 7;
        var month = (d + e + 114) / 31;
        var day = ((d + e + 114) % 31) + 1;
        var offset = year >= 2100 ? 14 : 13;
        var orthodoxEaster = new DateTime(year, month, day).AddDays(offset);

        holidays[orthodoxEaster.AddDays(-48)] = "Clean Monday";
        holidays[orthodoxEaster.AddDays(-2)] = "Good Friday";
        holidays[orthodoxEaster.AddDays(-1)] = "Holy Saturday";
        holidays[orthodoxEaster] = "Orthodox Easter Sunday";
        holidays[orthodoxEaster.AddDays(1)] = "Easter Monday";
        holidays[orthodoxEaster.AddDays(49)] = "Pentecost Sunday";
        holidays[orthodoxEaster.AddDays(50)] = "Holy Spirit Monday";
        return holidays;
    }

    private DateTime SelectedCalendarDate { get; set; } = DateTime.Today;

    private async Task LoadSelectedDayAsync()
    {
        await using var db = new PharmacyDbContext(PharmacyDb.Options());
        var dayStart = SelectedCalendarDate.Date;
        var dayEnd = dayStart.AddDays(1);
        var records = await db.Prescriptions.Include(p => p.Customer)
            .Where(p => p.Status == PrescriptionStatus.Active && p.ExpiryDate >= dayStart && p.ExpiryDate < dayEnd)
            .OrderBy(p => p.Customer!.FullName).ToListAsync();
        var subscriptions = await db.Subscriptions.AsNoTracking().Include(subscription => subscription.Customer)
            .Where(subscription => subscription.Status == SubscriptionStatus.Active &&
                subscription.NextDueDate >= dayStart && subscription.NextDueDate < dayEnd)
            .OrderBy(subscription => subscription.Customer!.FullName).ToListAsync();
        var dayItems = records.Select(p =>
                $"{p.Customer?.CustomerNumber} | {p.Customer?.FullName} | {p.MedicineName} | Rx {p.PrescriptionNumber ?? "—"} | expires {p.ExpiryDate:dd/MM/yyyy}")
            .Concat(subscriptions.Select(subscription =>
                $"{GetSubscriptionNumber(subscription)} | {subscription.Customer?.CustomerNumber} | {subscription.Customer?.FullName} | {subscription.Title} | due {subscription.NextDueDate:dd/MM/yyyy}"))
            .ToList();
        if (ApplicationSettingsService.Load().ShowOfficialHolidays)
        {
            var holidays = GetGreekPublicHolidays(SelectedCalendarDate.Year);
            if (holidays.TryGetValue(SelectedCalendarDate.Date, out var holidayName))
                dayItems.Insert(0, "★ " + Tr.Loc.TranslateText(holidayName));
        }
        dayRecords.ItemsSource = dayItems;

        var username = viewModel.User.Username?.Trim() ?? string.Empty;
        // Load notes for the selected day first, then apply ownership filtering in memory.
        // This avoids provider-specific case-sensitivity differences in username comparisons.
        var notesForDay = await db.CalendarNotes.AsNoTracking()
            .Where(note => note.CalendarDate >= dayStart && note.CalendarDate < dayEnd)
            .OrderBy(note => note.CreatedAt)
            .ToListAsync();
        var notes = notesForDay
            .Where(note => note.IsShared || string.Equals(note.CreatedBy?.Trim(), username, StringComparison.OrdinalIgnoreCase))
            .ToList();
        calendarNotesPanel.Children.Clear();
        if (notes.Count > 0)
        {
            calendarNotesPanel.Children.Add(new TextBlock
            {
                Text = Tr.Loc.TranslateText("Calendar notes"),
                FontWeight = FontWeight.SemiBold,
                FontSize = 15
            });
            foreach (var note in notes)
            {
                var isOwner = string.Equals(note.CreatedBy?.Trim(), viewModel.User.Username?.Trim(), StringComparison.OrdinalIgnoreCase);
                var details = new StackPanel
                {
                    Spacing = 3,
                    Children =
                    {
                        new TextBlock { Text = note.Header, FontWeight = FontWeight.Bold },
                        new TextBlock { Text = note.Note, TextWrapping = TextWrapping.Wrap },
                        new TextBlock
                        {
                            Text = $"{(note.IsShared ? Tr.Loc.TranslateText("Visible to everyone") : Tr.Loc.TranslateText("Private note"))} · {note.CreatedBy} · {DateTime.SpecifyKind(note.CreatedAt, DateTimeKind.Utc).ToLocalTime():dd/MM/yyyy HH:mm}",
                            FontSize = 10,
                            Foreground = Brushes.LightGray
                        }
                    }
                };
                var noteRow = new Grid
                {
                    ColumnDefinitions = new ColumnDefinitions
                    {
                        new ColumnDefinition(GridLength.Star),
                        new ColumnDefinition(GridLength.Auto),
                        new ColumnDefinition(GridLength.Auto)
                    },
                    Background = new SolidColorBrush(Color.Parse("#202F40")),
                    Margin = new Thickness(0, 2)
                };
                var detailsBorder = new Border
                {
                    Padding = new Thickness(10),
                    Child = details
                };
                noteRow.Children.Add(detailsBorder);
                if (isOwner)
                {
                    var editButton = new Button
                    {
                        Content = Tr.Loc.TranslateText("Edit"),
                        MinWidth = 76,
                        Margin = new Thickness(4),
                        VerticalAlignment = VerticalAlignment.Center
                    };
                    editButton.Click += async (_, _) => await OpenCalendarNoteEditorAsync(note.CalendarDate, note);
                    Grid.SetColumn(editButton, 1);
                    noteRow.Children.Add(editButton);

                    var deleteButton = new Button
                    {
                        Content = Tr.Loc.TranslateText("Delete"),
                        MinWidth = 76,
                        Margin = new Thickness(4),
                        VerticalAlignment = VerticalAlignment.Center
                    };
                    deleteButton.Click += async (_, _) =>
                    {
                        var confirm = new Window
                        {
                            Title = Tr.Loc.TranslateText("Delete calendar note"),
                            Width = 390,
                            Height = 170,
                            WindowStartupLocation = WindowStartupLocation.CenterOwner,
                            CanResize = false
                        };
                        var yes = new Button { Content = Tr.Loc.TranslateText("Delete"), MinWidth = 90 };
                        var no = new Button { Content = Tr.Loc.TranslateText("Cancel"), MinWidth = 90 };
                        yes.Click += (_, _) => confirm.Close(true);
                        no.Click += (_, _) => confirm.Close(false);
                        confirm.Content = new StackPanel
                        {
                            Margin = new Thickness(18),
                            Spacing = 14,
                            Children =
                            {
                                new TextBlock { Text = Tr.Loc.TranslateText("Delete this calendar note?"), TextWrapping = TextWrapping.Wrap },
                                new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8, Children = { no, yes } }
                            }
                        };
                        AttachDialogLocalization(confirm);
                        if (await confirm.ShowDialog<bool>(this))
                        {
                            await using var deleteDb = new PharmacyDbContext(PharmacyDb.Options());
                            var current = await deleteDb.CalendarNotes.FirstOrDefaultAsync(item => item.Id == note.Id);
                            if (current is not null && string.Equals(current.CreatedBy?.Trim(), viewModel.User.Username?.Trim(), StringComparison.OrdinalIgnoreCase))
                            {
                                deleteDb.CalendarNotes.Remove(current);
                                await deleteDb.SaveChangesAsync();
                                if (calendarMonthTitle is not null)
                                    await RenderCalendarAsync(calendarMonthTitle);
                            }
                        }
                    };
                    Grid.SetColumn(deleteButton, 2);
                    noteRow.Children.Add(deleteButton);
                }
                calendarNotesPanel.Children.Add(new Border
                {
                    BorderBrush = new SolidColorBrush(Color.Parse("#3A536B")),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(6),
                    Child = noteRow
                });
            }
        }
        dailySummary.Text = string.Format(Tr.Loc["Calendar.DaySummary"], SelectedCalendarDate.ToString("dddd, dd MMM yyyy"), records.Count)
            + $" | {subscriptions.Count} {Tr.Loc["Calendar.SubscriptionsDue"]}";
        dailySummary.FontWeight = FontWeight.SemiBold;
        ApplyTranslations();
    }

    private async Task OpenCalendarNoteEditorAsync(DateTime calendarDate, CalendarNote? existing = null)
    {
        if (!showingExpiryCalendar) return;
        var isOwner = existing is null ||
            string.Equals(existing.CreatedBy?.Trim(), viewModel.User.Username?.Trim(), StringComparison.OrdinalIgnoreCase);

        var header = new TextBox
        {
            Text = existing?.Header ?? string.Empty,
            IsReadOnly = !isOwner,
            PlaceholderText = Tr.Loc.TranslateText("Note header"),
            MinHeight = 36
        };
        var body = new TextBox
        {
            Text = existing?.Note ?? string.Empty,
            PlaceholderText = Tr.Loc.TranslateText("Write your note here"),
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            MinHeight = 150,
            VerticalContentAlignment = VerticalAlignment.Top,
            IsReadOnly = !isOwner
        };
        var shared = new CheckBox
        {
            Content = Tr.Loc.TranslateText("Visible to all users"),
            IsChecked = existing?.IsShared ?? false,
            IsEnabled = isOwner
        };
        var createdInfo = new TextBlock
        {
            Text = existing is null
                ? $"{Tr.Loc.TranslateText("Date")}: {calendarDate:dd/MM/yyyy} · {Tr.Loc.TranslateText("Created by")}: {viewModel.User.Username}"
                : $"{Tr.Loc.TranslateText("Created by")}: {existing.CreatedBy} · {DateTime.SpecifyKind(existing.CreatedAt, DateTimeKind.Utc).ToLocalTime():dd/MM/yyyy HH:mm}",
            Foreground = Brushes.LightGray,
            FontSize = 11
        };
        var status = new TextBlock { Foreground = Brushes.Orange, TextWrapping = TextWrapping.Wrap };
        var save = new Button { Content = Tr.Loc.TranslateText("Save"), Classes = { "accent" }, MinWidth = 100, IsVisible = isOwner };
        var cancel = new Button { Content = Tr.Loc.TranslateText(isOwner ? "Cancel" : "Close"), MinWidth = 100 };
        var delete = new Button
        {
            Content = Tr.Loc.TranslateText("Delete"),
            MinWidth = 90,
            IsVisible = existing is not null && isOwner
        };
        var dialog = new Window
        {
            Title = existing is null
                ? Tr.Loc.TranslateText("Add calendar note")
                : isOwner ? Tr.Loc.TranslateText("Edit calendar note") : Tr.Loc.TranslateText("View calendar note"),
            Width = 560,
            Height = 500,
            MinWidth = 460,
            MinHeight = 400,
            CanResize = true,
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };
        cancel.Click += (_, _) => dialog.Close();
        save.Click += async (_, _) =>
        {
            if (!isOwner) return;
            var title = header.Text?.Trim() ?? string.Empty;
            var text = body.Text?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(text))
            {
                status.Text = Tr.Loc.TranslateText("Both the header and note are required.");
                return;
            }

            await using var db = new PharmacyDbContext(PharmacyDb.Options());
            CalendarNote? record;
            if (existing is null)
            {
                record = new CalendarNote
                {
                    CalendarDate = calendarDate.Date,
                    CreatedBy = viewModel.User.Username ?? string.Empty,
                    CreatedAt = DateTime.UtcNow
                };
                db.CalendarNotes.Add(record);
            }
            else
            {
                record = await db.CalendarNotes.FirstOrDefaultAsync(note => note.Id == existing.Id);
                if (record is null || !string.Equals(record.CreatedBy?.Trim(), viewModel.User.Username?.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    status.Text = Tr.Loc.TranslateText("This note can no longer be edited by this user.");
                    return;
                }
            }
            record.Header = title;
            record.Note = text;
            record.IsShared = shared.IsChecked == true;
            await db.SaveChangesAsync();
            dialog.Close();
            if (calendarMonthTitle is not null)
                await RenderCalendarAsync(calendarMonthTitle);
        };
        delete.Click += async (_, _) =>
        {
            if (existing is null) return;
            await using var db = new PharmacyDbContext(PharmacyDb.Options());
            var record = await db.CalendarNotes.FirstOrDefaultAsync(note => note.Id == existing.Id);
            if (record is null || !string.Equals(record.CreatedBy?.Trim(), viewModel.User.Username?.Trim(), StringComparison.OrdinalIgnoreCase)) return;
            db.CalendarNotes.Remove(record);
            await db.SaveChangesAsync();
            dialog.Close();
            if (calendarMonthTitle is not null)
                await RenderCalendarAsync(calendarMonthTitle);
        };

        dialog.Content = new StackPanel
        {
            Margin = new Thickness(18),
            Spacing = 10,
            Children =
            {
                new TextBlock { Text = $"{Tr.Loc.TranslateText("Calendar date")}: {calendarDate:dddd, dd MMMM yyyy}", FontWeight = FontWeight.SemiBold },
                new TextBlock { Text = Tr.Loc.TranslateText("Header") }, header,
                new TextBlock { Text = Tr.Loc.TranslateText("Note") }, body,
                shared,
                createdInfo,
                status,
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Spacing = 8,
                    Children = { delete, cancel, save }
                }
            }
        };
        AttachDialogLocalization(dialog);
        await dialog.ShowDialog(this);
    }

    private TabControl customerTabs = null!;
    private TabItem customerDetailsTab = null!;
    private TextBlock customerOverview = null!;
    private readonly Dictionary<string, TextBox> activityFields = new();
    private ListBox borrowingList = null!;
    private ListBox subscriptionList = null!;
    private ListBox paymentList = null!;
    private ListBox ledgerList = null!;
    private List<BorrowingRecord> currentBorrowings = new();
    private List<Subscription> currentSubscriptions = new();
    private List<Subscription> currentExecutedSubscriptions = new();
    private ListBox subscriptionHistoryList = null!;
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
    private bool isCreatingNewCustomer;

    private void ShowCustomers()
    {
        if (!CanAccessMenu(MenuCustomers)) { ShowNoMenuAccess(); return; }
        showingExpiryCalendar = false;
        customerFields.Clear(); activityFields.Clear(); gs1ParsedFields.Clear(); selectedCustomerId = null; currentParsedPack = null;
        customerSearch = new TextBox { PlaceholderText = Tr.Loc["Customers.Search"], Margin = new Thickness(0, 0, 0, 4) };
        showInactiveCustomersCheckBox = new CheckBox
        {
            Content = Tr.Loc.TranslateText("Show inactive customers"),
            Margin = new Thickness(0, 0, 0, 8)
        };
        showInactiveCustomersCheckBox.IsCheckedChanged += async (_, _) => await SearchCustomersAsync();
        customerList = new ListBox { MinWidth = 300 };
        customerStatus = new TextBlock { Foreground = Brushes.LightGreen, Margin = new Thickness(0, 8, 0, 0) };
        customerSearch.TextChanged += async (_, _) => await SearchCustomersAsync();
        customerList.SelectionChanged += async (_, _) =>
        {
            if (suppressCustomerSelectionChanged) return;
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
            ClearCustomerForm(showNewCustomerForm: true);
            customerTabs.SelectedItem = customerDetailsTab;
            customerFields["CustomerNumber"].Focus();
        };
        left.Children.Add(newButton);
        left.Children.Add(customerSearch);
        left.Children.Add(showInactiveCustomersCheckBox);
        left.Children.Add(customerList);

        var details = BuildCustomerDetailsTab();
        customerDetailsTab = details;
        customerOverview = new TextBlock { Text = "Select a customer to see their account summary.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(12) };
        var overviewTab = new TabItem { Header = "Overview", Content = new StackPanel { Spacing = 12, Children = { customerOverview } } };
        var borrowingTab = BuildBorrowingsTab();
        var historyTab = BuildCustomerHistoryTab();
        var subscriptionsTab = BuildSubscriptionsTab();
        var ledgerTab = BuildLedgerTab();
        borrowingTab.IsVisible = CanAccessMenu(MenuBorrowing);
        historyTab.IsVisible = CanAccessMenu(MenuBorrowing);
        subscriptionsTab.IsVisible = CanAccessMenu(MenuSubscriptions);
        ledgerTab.IsVisible = CanAccessMenu(MenuPayments);
        customerTabs = new TabControl { Items = { overviewTab, details, borrowingTab, historyTab, subscriptionsTab, ledgerTab }, Margin = new Thickness(12) };
        // Avalonia virtualizes TabControl content: controls in inactive tabs may not
        // exist in the visual tree yet. Translate each tab when it becomes active.
        customerTabs.SelectionChanged += (_, _) =>
        {
            if (!isApplyingTranslations)
                ApplyTranslations();
        };
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
        if (skipNextCustomerSearch) skipNextCustomerSearch = false;
        else _ = SearchCustomersAsync();
        ClearCustomerForm(showNewCustomerForm: false);
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
        customerActiveCheckBox = new CheckBox
        {
            Content = Tr.Loc.TranslateText("Customer is active"),
            IsChecked = true,
            Margin = new Thickness(5, 8, 0, 0)
        };
        var save = new Button { Content = Tr.Loc["Customers.Save"], Classes = { "accent" }, MinWidth = 150 };
        save.Click += async (_, _) => await SaveCustomerAsync();
        var clear = new Button { Content = Tr.Loc["Customers.Clear"], MinWidth = 100 };
        clear.Click += (_, _) => ClearCustomerForm(showNewCustomerForm: true);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Margin = new Thickness(5, 14, 0, 0) };
        actions.Children.Add(save); actions.Children.Add(clear);
        var panel = new StackPanel { Spacing = 8, Children = { new TextBlock { Text = Tr.Loc["Customers.Details"], FontSize = 18, FontWeight = FontWeight.Bold }, fieldsGrid, customerActiveCheckBox, actions } };
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
        scanInput.GotFocus += (_, _) => ActivateEnglishBarcodeKeyboard();
        scanInput.LostFocus += (_, _) => RestoreBarcodeKeyboard();
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
        scanInput.GotFocus += (_, _) => ActivateEnglishBarcodeKeyboard();
        scanInput.LostFocus += (_, _) => RestoreBarcodeKeyboard();
        dialog.Opened += (_, _) =>
        {
            if (!isEdit)
                scanInput.Focus();
        };
        dialog.Closed += (_, _) => RestoreBarcodeKeyboard();
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
        customerHistoryList = new ListBox { MinHeight = 180 };
        subscriptionHistoryList = new ListBox { MinHeight = 150 };
        subscriptionHistoryList.SelectionChanged += async (_, _) =>
        {
            var index = subscriptionHistoryList.SelectedIndex;
            if (index < 0 || index >= currentExecutedSubscriptions.Count) return;
            var subscriptionId = currentExecutedSubscriptions[index].Id;
            subscriptionHistoryList.SelectedIndex = -1;
            await OpenSubscriptionDetailsAsync(subscriptionId);
        };
        return new TabItem
        {
            Header = "History",
            Content = new ScrollViewer
            {
                Content = new StackPanel
                {
                    Spacing = 10,
                    Margin = new Thickness(8),
                    Children =
                    {
                        new TextBlock { Text = "CUSTOMER MEDICINE & PRESCRIPTION HISTORY", FontSize = 18, FontWeight = FontWeight.Bold },
                        new TextBlock { Text = "All borrowing records (including returned items) and prescriptions, ordered by date.", TextWrapping = TextWrapping.Wrap, Foreground = Brushes.LightGray },
                        customerHistoryList,
                        new TextBlock { Text = "EXECUTED SUBSCRIPTIONS HISTORY", FontSize = 16, FontWeight = FontWeight.Bold, Margin = new Thickness(0, 8, 0, 0) },
                        new TextBlock { Text = "Executed subscriptions are kept here and can be opened to review their audit history.", TextWrapping = TextWrapping.Wrap, Foreground = Brushes.LightGray },
                        subscriptionHistoryList
                    }
                }
            }
        };
    }

    private TabItem BuildSubscriptionsTab()
    {
        subscriptionList = new ListBox { MinHeight = 220 };
        var add = new Button { Content = "＋ Add subscription", Classes = { "accent" }, MinWidth = 145, MinHeight = 38 };
        add.Click += async (_, _) => await OpenSubscriptionEditorAsync(null);
        var view = new Button { Content = "View details", MinWidth = 125, MinHeight = 38 };
        view.Click += async (_, _) =>
        {
            if (subscriptionList.SelectedIndex < 0 || subscriptionList.SelectedIndex >= currentSubscriptions.Count)
            { SetActivityStatus("Select a subscription first."); return; }
            await OpenSubscriptionDetailsAsync(currentSubscriptions[subscriptionList.SelectedIndex].Id);
        };
        var edit = new Button { Content = "Edit selected", MinWidth = 125, MinHeight = 38 };
        edit.Click += async (_, _) =>
        {
            if (subscriptionList.SelectedIndex < 0 || subscriptionList.SelectedIndex >= currentSubscriptions.Count)
            { SetActivityStatus("Select a subscription to edit."); return; }
            await OpenSubscriptionEditorAsync(currentSubscriptions[subscriptionList.SelectedIndex]);
        };
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { add, view, edit } };
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
            "LedgerType" when value == "Πληρωμή" => "Payment",
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
        var allSubscriptions = await db.Subscriptions.AsNoTracking().Where(x => x.CustomerId == id).OrderByDescending(x => x.StartDate).ToListAsync();
        var subscriptions = allSubscriptions.Where(x => x.Status == SubscriptionStatus.Active).ToList();
        var executedSubscriptions = allSubscriptions.Where(x => x.Status == SubscriptionStatus.Executed).ToList();
        var payments = await db.Payments.AsNoTracking().Where(x => x.CustomerId == id).OrderByDescending(x => x.PaidAt).ToListAsync();
        var ledger = await db.LedgerEntries.AsNoTracking().Where(x => x.CustomerId == id).OrderByDescending(x => x.EntryDate).ToListAsync();
        var charges = ledger.Where(x => x.Type == LedgerEntryType.Charge || x.Type == LedgerEntryType.Adjustment).Sum(x => x.Amount);
        var credits = ledger.Where(x => x.Type == LedgerEntryType.Payment || x.Type == LedgerEntryType.Reversal).Sum(x => x.Amount);
        var balance = charges - credits;
        var overviewLines = new List<string>
        {
            $"{customer.CustomerNumber}  |  {customer.FullName}",
            $"{customer.Address}, {customer.City} {customer.Postcode}",
            $"Telephone: {customer.Telephone ?? customer.Phone}  |  Mobile: {customer.Mobile}",
            $"Email: {customer.Email}"
        };
        if (CanAccessMenu(MenuBorrowing))
            overviewLines.Add($"BORROWINGS: {allBorrowings.Count} total, {borrowings.Count} currently borrowed");
        if (CanAccessMenu(MenuSubscriptions))
            overviewLines.Add($"SUBSCRIPTIONS: {subscriptions.Count} active | {executedSubscriptions.Count} executed");
        if (CanAccessMenu(MenuPayments))
        {
            overviewLines.Add($"PAYMENTS: {payments.Count} recorded  |  Total paid: {payments.Sum(x => x.Amount):C}");
            overviewLines.Add($"ACCOUNT BALANCE: {balance:C}");
            overviewLines.AddRange(ledger.Take(5).Select(x => $"{x.EntryDate:dd/MM/yyyy}  {x.Type}  {x.Amount:C}  {x.Description}"));
        }
        customerOverview.Text = string.Join("\n", overviewLines);
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
        currentExecutedSubscriptions = executedSubscriptions;
        currentPayments = payments;
        currentLedgerEntries = ledger;
        subscriptionList.ItemsSource = subscriptions.Select(x => $"{GetSubscriptionNumber(x)} | {x.Title} | {Tr.Loc.TranslateText(x.Status.ToString())} | Next: {x.NextDueDate:dd/MM/yyyy} | Expires: {x.ExpiryDate?.ToString("dd/MM/yyyy") ?? "—"} | {x.Frequency}").ToList();
        subscriptionHistoryList.ItemsSource = executedSubscriptions.Select(x => $"{GetSubscriptionNumber(x)} | {x.Title} | Last changed {ToLocalSubscriptionTime(x.LastModifiedAt):dd/MM/yyyy HH:mm}").ToList();
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
        decimal? value = TryParseLocalizedAmount(ActivityValue("BorrowingValue"), out var amount) ? amount : null;
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

    private static string GetSubscriptionNumber(Subscription subscription) =>
        string.IsNullOrWhiteSpace(subscription.Barcode)
            ? $"SUB-{subscription.Id:D8}"
            : subscription.Barcode;

    private static DateTime ToLocalSubscriptionTime(DateTime utcTime) =>
        DateTime.SpecifyKind(utcTime, DateTimeKind.Utc).ToLocalTime();

    private static ComboBox CreateSubscriptionStatusSelector(SubscriptionStatus selectedStatus)
    {
        var selector = new ComboBox { MinHeight = 34 };
        selector.Items.Add(new ComboBoxItem
        {
            Content = Tr.Loc.TranslateText(SubscriptionStatus.Active.ToString())
        });
        selector.Items.Add(new ComboBoxItem
        {
            Content = Tr.Loc.TranslateText(SubscriptionStatus.Executed.ToString())
        });
        selector.SelectedIndex = selectedStatus == SubscriptionStatus.Executed ? 1 : 0;
        return selector;
    }

    private static bool TryReadSubscriptionStatus(ComboBox selector, out SubscriptionStatus status)
    {
        if (selector.SelectedIndex == 0)
        {
            status = SubscriptionStatus.Active;
            return true;
        }

        if (selector.SelectedIndex == 1)
        {
            status = SubscriptionStatus.Executed;
            return true;
        }

        status = SubscriptionStatus.Active;
        return false;
    }

    private async Task OpenSubscriptionDetailsAsync(int subscriptionId)
    {
        await using var db = new PharmacyDbContext(PharmacyDb.Options());
        var subscription = await db.Subscriptions.Include(item => item.Customer)
            .FirstOrDefaultAsync(item => item.Id == subscriptionId);
        if (subscription is null || subscription.Customer is null) return;

        if (string.IsNullOrWhiteSpace(subscription.Barcode))
        {
            subscription.Barcode = GetSubscriptionNumber(subscription);
            await db.SaveChangesAsync();
        }

        var customer = subscription.Customer!;
        var subscriptionNumber = GetSubscriptionNumber(subscription);
        var barcodeBitmap = CreateSubscriptionBarcodeBitmap(subscriptionNumber);
        var statusSelector = CreateSubscriptionStatusSelector(subscription.Status);
        var auditEntries = await db.SubscriptionAuditEntries.AsNoTracking()
            .Where(entry => entry.SubscriptionId == subscription.Id)
            .OrderByDescending(entry => entry.ChangedAt)
            .ToListAsync();
        var auditList = new ListBox { MinHeight = 110 };
        void RefreshAuditList()
        {
            auditList.ItemsSource = auditEntries.Select(entry =>
            {
                var change = entry.PreviousStatus.HasValue
                    ? $"{Tr.Loc.TranslateText(((SubscriptionStatus)entry.PreviousStatus.Value).ToString())} → {Tr.Loc.TranslateText(((SubscriptionStatus)entry.NewStatus).ToString())}"
                    : Tr.Loc.TranslateText(entry.Details ?? string.Empty);
                return $"{ToLocalSubscriptionTime(entry.ChangedAt):dd/MM/yyyy HH:mm:ss} | {Tr.Loc.TranslateText(entry.Action)} | {entry.ChangedBy} | {change}";
            }).ToList();
        }
        RefreshAuditList();
        var details = new StackPanel
        {
            Spacing = 8,
            Margin = new Thickness(18),
            Children =
            {
                new TextBlock { Text = subscription.Title, FontSize = 22, FontWeight = FontWeight.Bold, TextWrapping = TextWrapping.Wrap },
                new TextBlock { Text = $"{Tr.Loc.TranslateText("Subscription number")}: {subscriptionNumber}", FontSize = 15, FontWeight = FontWeight.SemiBold },
                new Image { Source = barcodeBitmap, Width = 230, Height = 190, Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Center },
                new TextBlock { Text = $"{Tr.Loc.TranslateText("Customer")}: {customer.CustomerNumber} | {customer.FullName}", TextWrapping = TextWrapping.Wrap },
                new TextBlock { Text = $"{Tr.Loc.TranslateText("Address")}: {customer.Address}, {customer.City} {customer.Postcode}", TextWrapping = TextWrapping.Wrap },
                new TextBlock { Text = $"{Tr.Loc.TranslateText("Telephone")}: {customer.Telephone ?? customer.Phone} | {Tr.Loc.TranslateText("Mobile")}: {customer.Mobile}", TextWrapping = TextWrapping.Wrap },
                new TextBlock { Text = $"{Tr.Loc.TranslateText("Email")}: {customer.Email}", TextWrapping = TextWrapping.Wrap },
                new TextBlock { Text = $"{Tr.Loc.TranslateText("Start date")}: {subscription.StartDate:dd/MM/yyyy}" },
                new TextBlock { Text = $"{Tr.Loc.TranslateText("Next due date")}: {subscription.NextDueDate:dd/MM/yyyy}" },
                new TextBlock { Text = $"{Tr.Loc.TranslateText("Expiry date")}: {subscription.ExpiryDate?.ToString("dd/MM/yyyy") ?? "—"}" },
                new TextBlock { Text = $"{Tr.Loc.TranslateText("Frequency")}: {subscription.Frequency}" },
                new TextBlock { Text = $"{Tr.Loc.TranslateText("Status")}" },
                statusSelector,
                new TextBlock { Text = $"{Tr.Loc.TranslateText("Created by")}: {subscription.CreatedBy} | {ToLocalSubscriptionTime(subscription.CreatedAt):dd/MM/yyyy HH:mm:ss}", TextWrapping = TextWrapping.Wrap },
                new TextBlock { Text = $"{Tr.Loc.TranslateText("Last changed by")}: {subscription.LastModifiedBy} | {ToLocalSubscriptionTime(subscription.LastModifiedAt):dd/MM/yyyy HH:mm:ss}", TextWrapping = TextWrapping.Wrap },
                new TextBlock { Text = $"{Tr.Loc.TranslateText("Notes")}: {subscription.Notes}", TextWrapping = TextWrapping.Wrap },
                new TextBlock { Text = Tr.Loc.TranslateText("Audit history"), FontSize = 16, FontWeight = FontWeight.Bold },
                auditList
            }
        };
        var openCustomer = new Button
        {
            Content = Tr.Loc.TranslateText("Open customer"),
            MinWidth = 135,
            IsVisible = CanAccessMenu(MenuCustomers)
        };
        var editSubscription = new Button
        {
            Content = Tr.Loc.TranslateText("Edit subscription"),
            Classes = { "accent" },
            MinWidth = 150,
            IsVisible = CanAccessMenu(MenuSubscriptions) && CanAccessMenu(MenuCustomers)
        };
        var saveStatus = new Button { Content = Tr.Loc.TranslateText("Save status"), Classes = { "accent" }, MinWidth = 110 };
        var close = new Button { Content = Tr.Loc.TranslateText("Close"), MinWidth = 90 };
        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
            Children = { openCustomer, editSubscription, saveStatus, close }
        };
        var content = new Grid
        {
            RowDefinitions = CreateRows(GridLength.Star, GridLength.Auto),
            Children =
            {
                new ScrollViewer { Content = details, VerticalScrollBarVisibility = ScrollBarVisibility.Auto },
                actions
            }
        };
        Grid.SetRow(actions, 1);
        var popup = new Window
        {
            Title = Tr.Loc.TranslateText("Subscription details"),
            Width = 560,
            Height = 690,
            MinWidth = 480,
            MinHeight = 520,
            CanResize = true,
            Topmost = true,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = content
        };
        AttachDialogLocalization(popup);
        EventHandler auditLanguageChanged = (_, _) => RefreshAuditList();
        Tr.Loc.LanguageChanged += auditLanguageChanged;
        popup.Closed += (_, _) => Tr.Loc.LanguageChanged -= auditLanguageChanged;
        close.Click += (_, _) => popup.Close();
        saveStatus.Click += async (_, _) =>
        {
            if (!TryReadSubscriptionStatus(statusSelector, out var newStatus))
                return;
            if (newStatus == subscription.Status) return;

            await using var updateDb = new PharmacyDbContext(PharmacyDb.Options());
            var current = await updateDb.Subscriptions.FirstOrDefaultAsync(item => item.Id == subscriptionId);
            if (current is null) { popup.Close(); return; }
            var oldStatus = current.Status;
            var now = DateTime.UtcNow;
            var actor = viewModel.User.Username;
            current.Status = newStatus;
            current.LastModifiedAt = now;
            current.LastModifiedBy = actor;
            updateDb.SubscriptionAuditEntries.Add(new SubscriptionAuditEntry
            {
                SubscriptionId = current.Id,
                Action = "Status changed",
                PreviousStatus = (int)oldStatus,
                NewStatus = (int)newStatus,
                ChangedBy = actor,
                ChangedAt = now,
                Details = "Subscription status changed."
            });
            await updateDb.SaveChangesAsync();
            popup.Close();
            if (!showingExpiryCalendar && selectedCustomerId == current.CustomerId)
                await RefreshCustomerWorkspaceAsync();
            if (showingExpiryCalendar && calendarMonthTitle is not null)
                await RenderCalendarAsync(calendarMonthTitle);
            await OpenSubscriptionDetailsAsync(subscriptionId);
        };
        openCustomer.Click += async (_, _) =>
        {
            popup.Close();
            await OpenCustomerFromSubscriptionAsync(customer.Id, null);
        };
        editSubscription.Click += async (_, _) =>
        {
            popup.Close();
            await OpenCustomerFromSubscriptionAsync(customer.Id, subscription);
        };
        popup.Show(this);
    }

    private async Task OpenCustomerFromSubscriptionAsync(int customerId, Subscription? editSubscription)
    {
        if (!CanAccessMenu(MenuCustomers)) return;
        skipNextCustomerSearch = true;
        ShowCustomers();
        await SearchCustomersAsync();
        var customerItem = customerList.Items.OfType<CustomerListItem>().FirstOrDefault(item => item.Id == customerId);
        if (customerItem is null) return;

        suppressCustomerSelectionChanged = true;
        customerList.SelectedItem = customerItem;
        suppressCustomerSelectionChanged = false;
        await LoadCustomerAsync(customerId);
        await RefreshCustomerWorkspaceAsync();
        if (editSubscription is not null && CanAccessMenu(MenuSubscriptions))
        {
            var subscriptionTabIndex = customerTabs.Items.OfType<TabItem>()
                .Select((tab, index) => new { tab, index })
                .FirstOrDefault(item => item.tab.Header?.ToString() == "Subscriptions")?.index ?? -1;
            if (subscriptionTabIndex >= 0)
                customerTabs.SelectedIndex = subscriptionTabIndex;
            await OpenSubscriptionEditorAsync(editSubscription);
        }
        else
        {
            customerTabs.SelectedIndex = 0;
        }
    }

    private static WriteableBitmap CreateSubscriptionBarcodeBitmap(string barcodeValue)
    {
        var hints = new Dictionary<EncodeHintType, object>
        {
            [EncodeHintType.MARGIN] = 4
        };
        var matrix = new DataMatrixWriter().encode(barcodeValue, BarcodeFormat.DATA_MATRIX, 0, 0, hints);
        const int scale = 6;
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
                pixels[offset] = value;
                pixels[offset + 1] = value;
                pixels[offset + 2] = value;
                pixels[offset + 3] = 255;
            }
            Marshal.Copy(pixels, 0, framebuffer.Address, pixels.Length);
        }
        return bitmap;
    }

    private async Task OpenSubscriptionEditorAsync(Subscription? existing)
    {
        if (!CanAccessMenu(MenuSubscriptions)) return;
        if (!selectedCustomerId.HasValue) { SetActivityStatus("Save or select a customer first."); return; }
        var isEdit = existing is not null;
        var title = new TextBox { Text = existing?.Title ?? "", PlaceholderText = "Subscription title", MinHeight = 34 };
        var start = new TextBox { Text = (existing?.StartDate ?? DateTime.Today).ToString("yyyy-MM-dd"), PlaceholderText = "Start date (yyyy-MM-dd)", MinHeight = 34 };
        var due = new TextBox { Text = existing?.NextDueDate.ToString("yyyy-MM-dd") ?? "", PlaceholderText = "Next due date (yyyy-MM-dd)", MinHeight = 34 };
        var expiry = new TextBox { Text = existing?.ExpiryDate?.ToString("yyyy-MM-dd") ?? "", PlaceholderText = "Expiry date (optional)", MinHeight = 34 };
        var frequency = new TextBox { Text = existing?.Frequency ?? "Monthly", PlaceholderText = "Frequency", MinHeight = 34 };
        var notes = new TextBox { Text = existing?.Notes ?? "", PlaceholderText = "Notes", MinHeight = 70, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap };
        var subscriptionStatus = CreateSubscriptionStatusSelector(existing?.Status ?? SubscriptionStatus.Active);
        var form = new StackPanel { Spacing = 7, Margin = new Thickness(16), Children =
        {
            new TextBlock { Text = isEdit ? "EDIT SUBSCRIPTION" : "ADD SUBSCRIPTION", FontSize = 19, FontWeight = FontWeight.Bold },
            new TextBlock { Text = "Subscription title" }, title,
            new TextBlock { Text = "Start date (yyyy-MM-dd)" }, start,
            new TextBlock { Text = "Next due date (yyyy-MM-dd)" }, due,
            new TextBlock { Text = "Expiry date (yyyy-MM-dd, optional)" }, expiry,
            new TextBlock { Text = "Frequency" }, frequency,
            new TextBlock { Text = "Status" }, subscriptionStatus,
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
            if (!TryReadSubscriptionStatus(subscriptionStatus, out var selectedStatus))
            { status.Text = "Choose Active or Executed status."; return; }

            await using var transaction = await db.Database.BeginTransactionAsync();
            Subscription? record;
            SubscriptionStatus? previousStatus = null;
            var now = DateTime.UtcNow;
            var actor = viewModel.User.Username;
            if (isEdit)
            {
                record = await db.Subscriptions.FirstOrDefaultAsync(x => x.Id == existing!.Id && x.CustomerId == selectedCustomerId.Value);
                if (record is null) { status.Text = "The subscription could not be found."; return; }
                previousStatus = record.Status;
            }
            else
            {
                record = new Subscription
                {
                    CustomerId = selectedCustomerId.Value,
                    Status = selectedStatus,
                    CreatedAt = now,
                    CreatedBy = actor,
                    LastModifiedAt = now,
                    LastModifiedBy = actor
                };
                db.Subscriptions.Add(record);
            }
            record.Title = title.Text.Trim();
            record.Status = selectedStatus;
            record.LastModifiedAt = now;
            record.LastModifiedBy = actor;
            record.StartDate = startDate.Date;
            record.NextDueDate = dueDate.Date;
            record.ExpiryDate = expiryDate;
            record.Frequency = string.IsNullOrWhiteSpace(frequency.Text) ? "Monthly" : frequency.Text.Trim();
            record.Notes = notes.Text?.Trim();
            await db.SaveChangesAsync();
            if (string.IsNullOrWhiteSpace(record.Barcode))
                record.Barcode = GetSubscriptionNumber(record);
            db.SubscriptionAuditEntries.Add(new SubscriptionAuditEntry
            {
                SubscriptionId = record.Id,
                Action = isEdit ? (previousStatus != selectedStatus ? "Status changed" : "Updated") : "Created",
                PreviousStatus = previousStatus.HasValue && previousStatus.Value != selectedStatus
                    ? (int)previousStatus.Value
                    : null,
                NewStatus = (int)selectedStatus,
                ChangedBy = actor,
                ChangedAt = now,
                Details = isEdit && previousStatus != selectedStatus
                    ? $"Status changed from {previousStatus} to {selectedStatus}."
                    : isEdit ? "Subscription details updated." : "Subscription created."
            });
            await db.SaveChangesAsync();
            await transaction.CommitAsync();
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
            if (!TryParseLocalizedAmount(amount.Text, out var parsedAmount) || parsedAmount <= 0 || !TryParseUserDate(paidAt.Text, out var parsedPaidAt))
            { status.Text = "Enter a positive amount and valid payment date."; return; }
            await using var db = new PharmacyDbContext(PharmacyDb.Options());
            Payment? record;
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
            ItemsSource = new[] { "Charge", "Payment", "Adjustment", "Reversal" },
            SelectedItem = existing?.Type.ToString() ?? "Payment"
        };
        var typeDescription = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Foreground = Brushes.LightGray,
            FontSize = 12,
            Margin = new Thickness(2, -2, 2, 4)
        };
        string DescribeLedgerType(string? selectedType)
        {
            var english = selectedType switch
            {
                "Charge" => "Adds an amount the customer owes; increases the balance.",
                "Payment" => "Records money received; reduces the balance and appears in Payment History.",
                "Adjustment" => "Adds a positive balance correction; increases the amount owed.",
                "Reversal" => "Records a credit to offset a previous transaction; reduces the balance. It does not automatically delete the original transaction.",
                _ => "Choose what this ledger entry represents."
            };
            var greek = selectedType switch
            {
                "Charge" => "Προσθέτει ποσό που οφείλει ο πελάτης και αυξάνει το υπόλοιπο.",
                "Payment" => "Καταγράφει χρήματα που εισπράχθηκαν, μειώνει το υπόλοιπο και εμφανίζεται στο ιστορικό πληρωμών.",
                "Adjustment" => "Προσθέτει θετική διόρθωση στο υπόλοιπο και αυξάνει το οφειλόμενο ποσό.",
                "Reversal" => "Καταγράφει πίστωση για αντιλογισμό προηγούμενης συναλλαγής και μειώνει το υπόλοιπο. Δεν διαγράφει αυτόματα την αρχική συναλλαγή.",
                _ => "Επιλέξτε τι αντιπροσωπεύει αυτή η εγγραφή."
            };
            return Tr.Loc.CurrentLanguage == AppLanguage.Greek ? greek : english;
        }
        void UpdateLedgerTypeDescription() => typeDescription.Text = DescribeLedgerType(type.SelectedItem?.ToString());
        type.SelectionChanged += (_, _) => UpdateLedgerTypeDescription();
        UpdateLedgerTypeDescription();
        var amount = new TextBox { Text = existing?.Amount.ToString("0.00", CultureInfo.InvariantCulture) ?? "", PlaceholderText = "Amount", MinHeight = 34 };
        var description = new TextBox { Text = existing?.Description ?? "", PlaceholderText = "Description", MinHeight = 34 };
        var entryDate = new TextBox { Text = (existing?.EntryDate ?? DateTime.Now).ToString("yyyy-MM-dd HH:mm"), PlaceholderText = "Entry date (yyyy-MM-dd HH:mm)", MinHeight = 34 };
        var form = new StackPanel { Spacing = 7, Margin = new Thickness(16), Children =
        {
            new TextBlock { Text = isEdit ? "EDIT LEDGER ENTRY" : "ADD LEDGER ENTRY", FontSize = 19, FontWeight = FontWeight.Bold },
            new TextBlock { Text = "Entry type" }, type, typeDescription,
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
                !TryParseLocalizedAmount(amount.Text, out var parsedAmount) || parsedAmount <= 0 ||
                string.IsNullOrWhiteSpace(description.Text) ||
                !TryParseUserDate(entryDate.Text, out var parsedDate))
            { status.Text = "Choose a valid entry type, positive amount, description and date."; return; }
            await using var db = new PharmacyDbContext(PharmacyDb.Options());
            LedgerEntry? record;
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
            if (parsedType == LedgerEntryType.Payment)
            {
                // Give the payment and ledger row a shared unique reference so
                // editing it from Payment History updates this exact ledger row.
                var paymentReference = record.SourceReference;
                if (string.IsNullOrWhiteSpace(paymentReference))
                    paymentReference = "LEDGER-" + Guid.NewGuid().ToString("N");

                var linkedPayment = await db.Payments.FirstOrDefaultAsync(x =>
                    x.CustomerId == selectedCustomerId.Value && x.Reference == paymentReference);
                if (linkedPayment is null)
                {
                    linkedPayment = new Payment
                    {
                        CustomerId = selectedCustomerId.Value,
                        Reference = paymentReference,
                        RecordedBy = viewModel.User.Username
                    };
                    db.Payments.Add(linkedPayment);
                }

                linkedPayment.Amount = parsedAmount;
                linkedPayment.Method = "Cash";
                linkedPayment.PaidAt = parsedDate;
                linkedPayment.Notes = description.Text.Trim();
                record.SourceReference = paymentReference;
                record.Description = $"Payment - Cash: {description.Text.Trim()}";
            }
            await db.SaveChangesAsync();
            dialog.Close();
            SetActivityStatus(isEdit ? "Ledger entry updated." : parsedType == LedgerEntryType.Payment
                ? "Payment recorded and ledger updated."
                : "Ledger entry added.");
            await RefreshCustomerWorkspaceAsync();
        };
        await dialog.ShowDialog(this);
    }


    private static bool TryParseLocalizedAmount(string? input, out decimal amount)
    {
        amount = 0m;
        if (string.IsNullOrWhiteSpace(input)) return false;

        var value = input.Trim()
            .Replace("€", string.Empty, StringComparison.Ordinal)
            .Replace(" ", string.Empty, StringComparison.Ordinal)
            .Replace("\u00A0", string.Empty, StringComparison.Ordinal);

        // Accept both Greek (1.234,56) and English (1,234.56) number formats.
        var comma = value.LastIndexOf(',');
        var dot = value.LastIndexOf('.');
        if (comma >= 0 && dot >= 0)
        {
            var decimalSeparator = comma > dot ? ',' : '.';
            var groupSeparator = decimalSeparator == ',' ? '.' : ',';
            value = value.Replace(groupSeparator.ToString(), string.Empty, StringComparison.Ordinal);
            value = value.Replace(decimalSeparator, '.');
        }
        else if (comma >= 0)
        {
            value = value.Replace(',', '.');
        }

        return decimal.TryParse(value, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
            CultureInfo.InvariantCulture, out amount);
    }

    private static bool TryParseUserDate(string? input, out DateTime date)
    {
        date = default;
        if (string.IsNullOrWhiteSpace(input)) return false;

        var value = input.Trim();
        var formats = new[]
        {
            "yyyy-MM-dd HH:mm", "yyyy-MM-dd H:mm", "yyyy-MM-dd",
            "dd/MM/yyyy HH:mm", "d/M/yyyy H:mm", "dd/MM/yyyy",
            "dd-MM-yyyy HH:mm", "d-M-yyyy H:mm", "dd-MM-yyyy"
        };

        return DateTime.TryParseExact(value, formats, CultureInfo.InvariantCulture,
                   DateTimeStyles.AllowWhiteSpaces, out date)
               || DateTime.TryParse(value, CultureInfo.GetCultureInfo("el-GR"),
                   DateTimeStyles.AllowWhiteSpaces, out date)
               || DateTime.TryParse(value, CultureInfo.GetCultureInfo("en-US"),
                   DateTimeStyles.AllowWhiteSpaces, out date);
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
        if (!TryParseLocalizedAmount(ActivityValue("PaymentAmount"), out var amount) || amount <= 0) { SetActivityStatus("Enter a payment amount greater than zero."); return; }
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
        if (!TryParseLocalizedAmount(ActivityValue("LedgerAmount"), out var amount) || amount <= 0 || string.IsNullOrWhiteSpace(ActivityValue("LedgerDescription"))) { SetActivityStatus("Enter a positive amount and description."); return; }
        if (!Enum.TryParse<LedgerEntryType>(ActivityValue("LedgerType"), true, out var type)) { SetActivityStatus("Choose Charge, Payment, Adjustment or Reversal."); return; }
        await using var db = new PharmacyDbContext(PharmacyDb.Options());
        var description = ActivityValue("LedgerDescription");
        var entryDate = DateTime.Now;
        var ledgerEntry = new LedgerEntry
        {
            CustomerId = selectedCustomerId.Value, Type = type, Amount = amount,
            Description = description, EntryDate = entryDate, RecordedBy = viewModel.User.Username
        };
        if (type == LedgerEntryType.Payment)
        {
            var paymentReference = "LEDGER-" + Guid.NewGuid().ToString("N");
            ledgerEntry.SourceReference = paymentReference;
            ledgerEntry.Description = $"Payment - Cash: {description}";
            db.Payments.Add(new Payment
            {
                CustomerId = selectedCustomerId.Value,
                Amount = amount,
                Method = "Cash",
                Reference = paymentReference,
                PaidAt = entryDate,
                Notes = description,
                RecordedBy = viewModel.User.Username
            });
        }
        db.LedgerEntries.Add(ledgerEntry);
        await db.SaveChangesAsync();
        SetActivityStatus(type == LedgerEntryType.Payment ? "Payment recorded and ledger updated." : "Ledger entry added.");
        await RefreshCustomerWorkspaceAsync();
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
        var query = db.Customers.AsNoTracking();
        if (showInactiveCustomersCheckBox?.IsChecked != true)
            query = query.Where(c => c.IsActive);
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
        selectedCustomerId = c.Id;
        isCreatingNewCustomer = false;
        UpdateCustomerTabsVisibility();
        var parts = SplitLegacyName(c);
        SetField("CustomerNumber", c.CustomerNumber); SetField("FirstName", c.FirstName ?? parts.First); SetField("Surname", c.Surname ?? parts.Last);
        SetField("Address", c.Address); SetField("City", c.City); SetField("Postcode", c.Postcode);
        SetField("Telephone", c.Telephone ?? c.Phone); SetField("Mobile", c.Mobile); SetField("Email", c.Email);
        SetField("Amka", c.Amka); SetField("Insurance", c.Insurance); SetField("Notes", c.Notes);
        customerActiveCheckBox.IsChecked = c.IsActive;
        customerStatus.Text = string.Format(Tr.Loc["Customers.Editing"], c.CustomerNumber);
    }

    private void ClearCustomerForm(bool showNewCustomerForm = true)
    {
        selectedCustomerId = null;
        isCreatingNewCustomer = showNewCustomerForm;
        foreach (var field in customerFields.Values) field.Text = "";
        if (customerActiveCheckBox is not null) customerActiveCheckBox.IsChecked = true;
        customerStatus.Text = Tr.Loc["Customers.NewStatus"];
        customerStatus.Foreground = Brushes.LightGreen;
        if (customerList is not null) customerList.SelectedItem = null;
        UpdateCustomerTabsVisibility();
    }

    private void UpdateCustomerTabsVisibility()
    {
        if (customerTabs is null) return;
        var hasCustomer = selectedCustomerId.HasValue;
        customerTabs.IsVisible = hasCustomer || isCreatingNewCustomer;
        if (customerDetailsTab is not null)
            customerDetailsTab.Header = hasCustomer ? Tr.Loc.TranslateText("Details") : string.Empty;
        foreach (var tab in customerTabs.Items.OfType<TabItem>())
        {
            if (!hasCustomer)
            {
                tab.IsVisible = isCreatingNewCustomer && ReferenceEquals(tab, customerDetailsTab);
                continue;
            }

            var header = tab.Header?.ToString() ?? string.Empty;
            tab.IsVisible = ReferenceEquals(tab, customerDetailsTab)
                || header is "Overview" or "Επισκόπηση"
                || (header is "Borrowings" or "Δανεισμοί" && CanAccessMenu(MenuBorrowing))
                || (header is "History" or "Ιστορικό" && CanAccessMenu(MenuBorrowing))
                || (header is "Subscriptions" or "Συνδρομές" && CanAccessMenu(MenuSubscriptions))
                || (header is "Ledger" or "Καρτέλα" && CanAccessMenu(MenuPayments));
        }
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
        customer.IsActive = customerActiveCheckBox?.IsChecked == true;
        if (!selectedCustomerId.HasValue) db.Customers.Add(customer);
        await db.SaveChangesAsync();
        selectedCustomerId = customer.Id;
        isCreatingNewCustomer = false;
        UpdateCustomerTabsVisibility();
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

    private async Task ShowUsersAsync()
    {
        if (viewModel.User.Role != PharmacyRole.Admin)
        {
            ShowNoMenuAccess();
            return;
        }

        showingExpiryCalendar = false;
        var root = new StackPanel { Margin = new Thickness(22), Spacing = 12 };
        var header = new Grid { ColumnDefinitions = CreateColumns(GridLength.Star, GridLength.Auto) };
        header.Children.Add(new TextBlock
        {
            Text = Tr.Loc["Navigation.Users"],
            FontSize = 24,
            FontWeight = FontWeight.Bold,
            VerticalAlignment = VerticalAlignment.Center
        });
        var add = new Button
        {
            Content = Tr.Loc.TranslateText("＋ New user"),
            Classes = { "accent" },
            MinWidth = 130,
            MinHeight = 38
        };
        add.Click += async (_, _) => await OpenUserEditorAsync(null);
        Grid.SetColumn(add, 1);
        header.Children.Add(add);
        root.Children.Add(header);
        root.Children.Add(new TextBlock
        {
            Text = Tr.Loc.TranslateText("Create accounts, assign roles, choose visible workspace menus and manage access."),
            Foreground = Brushes.LightGray,
            TextWrapping = TextWrapping.Wrap
        });

        var listHost = new StackPanel { Spacing = 8 };
        root.Children.Add(new ScrollViewer
        {
            Content = listHost,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            MaxHeight = 650
        });
        pageHost.Content = root;

        await using var db = new PharmacyDbContext(PharmacyDb.Options());
        var users = await db.Users.AsNoTracking().OrderBy(user => user.Username).ToListAsync();
        if (users.Count == 0)
        {
            listHost.Children.Add(new TextBlock { Text = Tr.Loc.TranslateText("No users found.") });
            return;
        }

        foreach (var user in users)
        {
            var card = new Border
            {
                Background = new SolidColorBrush(Color.Parse("#1D2B3B")),
                BorderBrush = new SolidColorBrush(Color.Parse("#34495E")),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(12)
            };
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            var info = new StackPanel { Spacing = 3, Width = 250, VerticalAlignment = VerticalAlignment.Center };
            info.Children.Add(new TextBlock { Text = user.Username, FontWeight = FontWeight.SemiBold, FontSize = 14 });
            info.Children.Add(new TextBlock
            {
                Text = $"{Tr.Loc.TranslateText(user.Role.ToString())} · {(user.IsActive ? Tr.Loc.TranslateText("Active") : Tr.Loc.TranslateText("Inactive"))}",
                Foreground = user.IsActive ? Brushes.LightGreen : Brushes.Orange,
                FontSize = 12
            });
            var allowedMenuCount = string.IsNullOrWhiteSpace(user.AllowedMenus)
                ? ConfigurableMenus.Length
                : user.AllowedMenus.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Distinct(StringComparer.Ordinal).Count();
            info.Children.Add(new TextBlock
            {
                Text = allowedMenuCount >= ConfigurableMenus.Length
                    ? Tr.Loc.TranslateText("All workspace menus")
                    : string.Format(Tr.Loc.TranslateText("Menus: {0} selected"), allowedMenuCount),
                Foreground = Brushes.LightGray,
                FontSize = 11
            });
            info.Children.Add(new TextBlock
            {
                Text = string.Format(Tr.Loc.TranslateText("Created {0}"), DateTime.SpecifyKind(user.CreatedAt, DateTimeKind.Utc).ToLocalTime().ToString("dd/MM/yyyy HH:mm")),
                Foreground = Brushes.Gray,
                FontSize = 11
            });
            row.Children.Add(info);

            var edit = new Button { Content = Tr.Loc.TranslateText("Edit user"), MinWidth = 85 };
            edit.Click += async (_, _) => await OpenUserEditorAsync(user);
            row.Children.Add(edit);

            var reset = new Button { Content = Tr.Loc.TranslateText("Reset password"), MinWidth = 105 };
            reset.Click += async (_, _) => await OpenUserPasswordResetAsync(user);
            row.Children.Add(reset);

            var toggle = new Button
            {
                Content = Tr.Loc.TranslateText(user.IsActive ? "Deactivate" : "Activate"),
                MinWidth = 85,
                IsEnabled = user.Id != viewModel.User.Id
            };
            toggle.Click += async (_, _) => await ToggleUserActiveAsync(user);
            row.Children.Add(toggle);

            var delete = new Button
            {
                Content = Tr.Loc.TranslateText("Delete"),
                MinWidth = 70,
                IsEnabled = user.Id != viewModel.User.Id
            };
            delete.Click += async (_, _) => await DeleteUserAsync(user);
            row.Children.Add(delete);

            card.Child = row;
            listHost.Children.Add(card);
        }
    }

    private async Task OpenUserEditorAsync(PharmacyUser? existing)
    {
        if (viewModel.User.Role != PharmacyRole.Admin) return;
        var isEdit = existing is not null;
        var username = new TextBox
        {
            Text = existing?.Username ?? string.Empty,
            PlaceholderText = Tr.Loc.TranslateText("Username"),
            MinHeight = 36
        };
        var password = new TextBox
        {
            PasswordChar = '•',
            PlaceholderText = Tr.Loc.TranslateText("Password (minimum 8 characters)"),
            MinHeight = 36,
            IsVisible = !isEdit
        };
        var role = new ComboBox { MinHeight = 36 };
        foreach (var roleOption in Enum.GetValues<PharmacyRole>())
        {
            role.Items.Add(new ComboBoxItem
            {
                Content = Tr.Loc.TranslateText(roleOption.ToString()),
                Tag = roleOption
            });
        }
        role.SelectedIndex = Array.IndexOf(Enum.GetValues<PharmacyRole>(), existing?.Role ?? PharmacyRole.Limited);
        var canSaveCredentials = new CheckBox
        {
            Content = Tr.Loc.TranslateText("Allow saving login credentials on this computer"),
            IsChecked = existing?.CanSaveCredentials ?? false
        };

        var menuPicker = new StackPanel { Spacing = 6 };
        menuPicker.Children.Add(new TextBlock
        {
            Text = Tr.Loc.TranslateText("Menus this user can see"),
            FontWeight = FontWeight.SemiBold
        });
        menuPicker.Children.Add(new TextBlock
        {
            Text = Tr.Loc.TranslateText("Select at least one menu. Users and Administrative are reserved for administrators."),
            Foreground = Brushes.LightGray,
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap
        });
        var menuChecks = new List<(string Key, CheckBox Check)>();
        var storedMenus = existing?.AllowedMenus;
        var existingMenus = string.IsNullOrWhiteSpace(storedMenus)
            ? ConfigurableMenus.Select(item => item.Key).ToHashSet(StringComparer.Ordinal)
            : storedMenus.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToHashSet(StringComparer.Ordinal);
        foreach (var menu in ConfigurableMenus)
        {
            var check = new CheckBox
            {
                Content = Tr.Loc[menu.LabelKey],
                IsChecked = isEdit ? (existing?.Role == PharmacyRole.Admin || existingMenus.Contains(menu.Key)) : false,
                MinWidth = 180,
                Margin = new Thickness(0, 2)
            };
            menuChecks.Add((menu.Key, check));
            menuPicker.Children.Add(check);
        }

        var selectAll = new Button { Content = Tr.Loc.TranslateText("Select all"), MinWidth = 100 };
        selectAll.Click += (_, _) => { foreach (var item in menuChecks) item.Check.IsChecked = true; };
        var clearAll = new Button { Content = Tr.Loc.TranslateText("Clear all"), MinWidth = 100 };
        clearAll.Click += (_, _) => { foreach (var item in menuChecks) item.Check.IsChecked = false; };
        menuPicker.Children.Insert(2, new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Children = { selectAll, clearAll }
        });
        menuPicker.IsVisible = role.SelectedItem is not ComboBoxItem { Tag: PharmacyRole.Admin };
        role.SelectionChanged += (_, _) => menuPicker.IsVisible = role.SelectedItem is not ComboBoxItem { Tag: PharmacyRole.Admin };

        if (existing?.Id == viewModel.User.Id)
            role.IsEnabled = false;

        var form = new StackPanel
        {
            Margin = new Thickness(18),
            Spacing = 8,
            Children =
            {
                new TextBlock { Text = isEdit ? Tr.Loc.TranslateText("Edit user") : Tr.Loc.TranslateText("New user"), FontSize = 20, FontWeight = FontWeight.Bold },
                new TextBlock { Text = Tr.Loc.TranslateText("Username") }, username,
                new TextBlock { Text = Tr.Loc.TranslateText("Password (minimum 8 characters)"), IsVisible = !isEdit }, password,
                new TextBlock { Text = Tr.Loc.TranslateText("Role") }, role,
                canSaveCredentials,
                menuPicker
            }
        };
        var status = new TextBlock { Foreground = Brushes.Orange, TextWrapping = TextWrapping.Wrap };
        var save = new Button { Content = Tr.Loc.TranslateText("Save"), Classes = { "accent" }, MinWidth = 110 };
        var cancel = new Button { Content = Tr.Loc.TranslateText("Cancel"), MinWidth = 100 };
        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
            Children = { status, cancel, save }
        };
        var dialog = new Window
        {
            Title = isEdit ? Tr.Loc.TranslateText("Edit user") : Tr.Loc.TranslateText("New user"),
            Width = 600,
            Height = 690,
            MinWidth = 520,
            MinHeight = 520,
            CanResize = true,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new StackPanel
            {
                Spacing = 10,
                Children =
                {
                    new ScrollViewer { Content = form, VerticalScrollBarVisibility = ScrollBarVisibility.Auto },
                    new Border { Padding = new Thickness(12), Child = actions }
                }
            }
        };
        cancel.Click += (_, _) => dialog.Close();
        save.Click += async (_, _) =>
        {
            var name = username.Text?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(name))
            {
                status.Text = Tr.Loc.TranslateText("Username is required.");
                return;
            }
            if (role.SelectedItem is not ComboBoxItem { Tag: PharmacyRole selectedRole })
            {
                status.Text = Tr.Loc.TranslateText("Select a valid role.");
                return;
            }
            var selectedMenus = menuChecks.Where(item => item.Check.IsChecked == true).Select(item => item.Key).ToList();
            if (selectedRole != PharmacyRole.Admin && selectedMenus.Count == 0)
            {
                status.Text = Tr.Loc.TranslateText("Select at least one menu.");
                return;
            }
            var newPassword = password.Text ?? string.Empty;
            if (!isEdit && newPassword.Length < 8)
            {
                status.Text = Tr.Loc.TranslateText("Password must contain at least 8 characters.");
                return;
            }

            await using var db = new PharmacyDbContext(PharmacyDb.Options());
            var existingUserId = existing?.Id ?? 0;
            if (await db.Users.AnyAsync(item => item.Username == name && item.Id != existingUserId))
            {
                status.Text = Tr.Loc.TranslateText("This username is already in use.");
                return;
            }

            PharmacyUser record;
            if (isEdit)
            {
                var existingRecord = await db.Users.FirstOrDefaultAsync(item => item.Id == existing!.Id);
                if (existingRecord is null)
                {
                    status.Text = Tr.Loc.TranslateText("The user could not be found.");
                    return;
                }
                record = existingRecord;
                if (record.Role == PharmacyRole.Admin && selectedRole != PharmacyRole.Admin && record.IsActive &&
                    await db.Users.CountAsync(item => item.Role == PharmacyRole.Admin && item.IsActive) <= 1)
                {
                    status.Text = Tr.Loc.TranslateText("At least one active administrator must remain.");
                    return;
                }
            }
            else
            {
                record = new PharmacyUser();
                db.Users.Add(record);
                record.PasswordHash = BCrypt.Net.BCrypt.HashPassword(newPassword);
            }

            record.Username = name;
            record.Role = selectedRole;
            record.AllowedMenus = selectedRole == PharmacyRole.Admin
                ? null
                : string.Join(",", ConfigurableMenus.Where(item => selectedMenus.Contains(item.Key)).Select(item => item.Key));
            record.CanSaveCredentials = canSaveCredentials.IsChecked == true;
            await db.SaveChangesAsync();

            if (record.Id == viewModel.User.Id)
            {
                viewModel.User.Username = record.Username;
                viewModel.User.CanSaveCredentials = record.CanSaveCredentials;
                UserLabel.Text = viewModel.UserDisplay;
            }
            dialog.Close();
            await ShowUsersAsync();
        };
        AttachDialogLocalization(dialog);
        await dialog.ShowDialog(this);
    }

    private async Task OpenUserPasswordResetAsync(PharmacyUser user)
    {
        if (viewModel.User.Role != PharmacyRole.Admin) return;
        var password = new TextBox
        {
            PasswordChar = '•',
            PlaceholderText = Tr.Loc.TranslateText("New password (minimum 8 characters)"),
            MinHeight = 36
        };
        var confirm = new TextBox
        {
            PasswordChar = '•',
            PlaceholderText = Tr.Loc.TranslateText("Confirm new password"),
            MinHeight = 36
        };
        var form = new StackPanel
        {
            Margin = new Thickness(18),
            Spacing = 8,
            Children =
            {
                new TextBlock { Text = string.Format(Tr.Loc.TranslateText("Reset password for {0}"), user.Username), FontSize = 18, FontWeight = FontWeight.Bold },
                password,
                confirm
            }
        };
        var status = new TextBlock { Foreground = Brushes.Orange, TextWrapping = TextWrapping.Wrap };
        var save = new Button { Content = Tr.Loc.TranslateText("Save"), Classes = { "accent" }, MinWidth = 100 };
        var cancel = new Button { Content = Tr.Loc.TranslateText("Cancel"), MinWidth = 100 };
        var dialog = new Window
        {
            Title = Tr.Loc.TranslateText("Reset password"),
            Width = 440,
            Height = 260,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
            Content = new StackPanel
            {
                Spacing = 8,
                Children =
                {
                    form,
                    status,
                    new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8, Children = { cancel, save } }
                }
            }
        };
        cancel.Click += (_, _) => dialog.Close();
        save.Click += async (_, _) =>
        {
            var value = password.Text ?? string.Empty;
            if (value.Length < 8)
            {
                status.Text = Tr.Loc.TranslateText("Password must contain at least 8 characters.");
                return;
            }
            if (value != (confirm.Text ?? string.Empty))
            {
                status.Text = Tr.Loc.TranslateText("Passwords do not match.");
                return;
            }
            await using var db = new PharmacyDbContext(PharmacyDb.Options());
            var record = await db.Users.FirstOrDefaultAsync(item => item.Id == user.Id);
            if (record is null) { status.Text = Tr.Loc.TranslateText("The user could not be found."); return; }
            record.PasswordHash = BCrypt.Net.BCrypt.HashPassword(value);
            await db.SaveChangesAsync();
            dialog.Close();
            await ShowUsersAsync();
        };
        AttachDialogLocalization(dialog);
        await dialog.ShowDialog(this);
    }

    private async Task ToggleUserActiveAsync(PharmacyUser user)
    {
        if (viewModel.User.Role != PharmacyRole.Admin || user.Id == viewModel.User.Id) return;
        await using var db = new PharmacyDbContext(PharmacyDb.Options());
        var record = await db.Users.FirstOrDefaultAsync(item => item.Id == user.Id);
        if (record is null) return;
        if (record.IsActive && record.Role == PharmacyRole.Admin &&
            await db.Users.CountAsync(item => item.Role == PharmacyRole.Admin && item.IsActive) <= 1)
        {
            await ShowUserMessageAsync(Tr.Loc.TranslateText("At least one active administrator must remain."));
            return;
        }
        record.IsActive = !record.IsActive;
        await db.SaveChangesAsync();
        await ShowUsersAsync();
    }

    private async Task DeleteUserAsync(PharmacyUser user)
    {
        if (viewModel.User.Role != PharmacyRole.Admin || user.Id == viewModel.User.Id) return;
        if (!await ConfirmUserDeleteAsync(user.Username)) return;
        await using var db = new PharmacyDbContext(PharmacyDb.Options());
        var record = await db.Users.FirstOrDefaultAsync(item => item.Id == user.Id);
        if (record is null) return;
        if (record.Role == PharmacyRole.Admin && record.IsActive &&
            await db.Users.CountAsync(item => item.Role == PharmacyRole.Admin && item.IsActive) <= 1)
        {
            await ShowUserMessageAsync(Tr.Loc.TranslateText("At least one active administrator must remain."));
            return;
        }
        db.Users.Remove(record);
        await db.SaveChangesAsync();
        await ShowUsersAsync();
    }

    private async Task<bool> ConfirmUserDeleteAsync(string username)
    {
        var confirmed = false;
        var dialog = new Window
        {
            Title = Tr.Loc.TranslateText("Confirm deletion"),
            Width = 420,
            Height = 180,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };
        var yes = new Button { Content = Tr.Loc.TranslateText("Delete"), Classes = { "accent" }, MinWidth = 90 };
        var no = new Button { Content = Tr.Loc.TranslateText("Cancel"), MinWidth = 90 };
        yes.Click += (_, _) => { confirmed = true; dialog.Close(); };
        no.Click += (_, _) => dialog.Close();
        dialog.Content = new StackPanel
        {
            Margin = new Thickness(18),
            Spacing = 12,
            Children =
            {
                new TextBlock { Text = string.Format(Tr.Loc.TranslateText("Delete user {0}?"), username), TextWrapping = TextWrapping.Wrap },
                new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8, Children = { no, yes } }
            }
        };
        AttachDialogLocalization(dialog);
        await dialog.ShowDialog(this);
        return confirmed;
    }

    private async Task ShowUserMessageAsync(string message)
    {
        var dialog = new Window
        {
            Title = Tr.Loc.TranslateText("Users"),
            Width = 420,
            Height = 170,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };
        var close = new Button { Content = Tr.Loc.TranslateText("Close"), MinWidth = 90 };
        close.Click += (_, _) => dialog.Close();
        dialog.Content = new StackPanel
        {
            Margin = new Thickness(18),
            Spacing = 12,
            Children = { new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap }, close }
        };
        AttachDialogLocalization(dialog);
        await dialog.ShowDialog(this);
    }

    private void ShowPlaceholder(string title)
    {
        if ((title == Tr.Loc["Navigation.Borrowing"] && !CanAccessMenu(MenuBorrowing)) ||
            (title == Tr.Loc["Navigation.Subscriptions"] && !CanAccessMenu(MenuSubscriptions)) ||
            (title == Tr.Loc["Navigation.Payments"] && !CanAccessMenu(MenuPayments)))
        { ShowNoMenuAccess(); return; }
        showingExpiryCalendar = false;
        pageHost.Content = new StackPanel { Margin = new Thickness(20), Children = { new TextBlock { Text = title, FontSize = 24, FontWeight = FontWeight.Bold }, new TextBlock { Text = Tr.Loc["Common.Queued"] } } };
    }

    private async void OnAppearanceClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var appearance = new AppearanceWindow();
        await appearance.ShowDialog(this);
    }

    private void OnLogoutClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var login = new LoginWindow();
        if (Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
            desktop.MainWindow = login;
        login.Show();
        Close();
    }
}