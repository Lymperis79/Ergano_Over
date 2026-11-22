using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Microsoft.EntityFrameworkCore;
using SPharmacyLog.Data;
using SPharmacyLog.Data.Entities;
using SPharmacyLog.UI.Localization;
using SPharmacyLog.UI.Services;

namespace SPharmacyLog.UI.Views;

/// <summary>
/// Interactive reports backed by the pharmacy customer, ledger, subscription and borrowing records.
/// </summary>
public sealed class ReportsWindow : Window
{
    private readonly ComboBox reportType = new() { MinWidth = 250 };
    private readonly ComboBox customerFilter = new() { MinWidth = 300, IsEditable = true };
    private List<CustomerFilterOption> customerOptions = new();
    private bool isFilteringCustomers;
    private readonly TextBox reportDate = new() { MinWidth = 150, Text = DateTime.Today.ToString("yyyy-MM-dd") };
    private readonly TextBlock summary = new() { FontSize = 15, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap };
    private readonly ListBox results = new() { MinHeight = 300 };
    private readonly StackPanel customerFilterPanel = new() { Spacing = 4 };
    private readonly StackPanel dateFilterPanel = new() { Spacing = 4 };
    private readonly Button exportButton = new() { Content = Tr.Loc.TranslateText("Export to Excel"), MinWidth = 150, MinHeight = 38, IsEnabled = false };

    public ReportsWindow()
    {
        Title = Tr.Loc.TranslateText("Reports");
        Width = 980;
        Height = 760;
        MinWidth = 760;
        MinHeight = 560;
        CanResize = true;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        reportType.Items.Add(new ComboBoxItem { Content = Tr.Loc.TranslateText("Customer balances and payments"), Tag = "customers" });
        reportType.Items.Add(new ComboBoxItem { Content = Tr.Loc.TranslateText("Upcoming subscriptions - next 7 days"), Tag = "upcoming" });
        reportType.Items.Add(new ComboBoxItem { Content = Tr.Loc.TranslateText("Daily subscriptions and borrowing status"), Tag = "daily" });
        reportType.SelectedIndex = 0;

        customerFilterPanel.Children.Add(new TextBlock { Text = Tr.Loc.TranslateText("Customer") });
        customerFilterPanel.Children.Add(customerFilter);
        dateFilterPanel.Children.Add(new TextBlock { Text = Tr.Loc.TranslateText("Activity date (yyyy-MM-dd)") });
        dateFilterPanel.Children.Add(reportDate);

        var filters = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 14 };
        var typePanel = new StackPanel { Spacing = 4, Children = { new TextBlock { Text = Tr.Loc.TranslateText("Report") }, reportType } };
        filters.Children.Add(typePanel);
        filters.Children.Add(customerFilterPanel);
        filters.Children.Add(dateFilterPanel);

        var generate = new Button
        {
            Content = Tr.Loc.TranslateText("Generate report"),
            MinWidth = 150,
            MinHeight = 38,
            Classes = { "accent" }
        };
        generate.Click += async (_, _) => await GenerateReportAsync();
        exportButton.Click += async (_, _) => await ExportReportAsync();
        reportType.SelectionChanged += (_, _) => { UpdateFilterVisibility(); exportButton.IsEnabled = false; };
        customerFilter.SelectionChanged += (_, _) => exportButton.IsEnabled = false;
        customerFilter.PropertyChanged += (_, e) =>
        {
            if (e.Property != ComboBox.TextProperty || isFilteringCustomers)
                return;

            // ComboBox updates Text while committing a selection. Do not rebuild Items
            // during that selection transaction.
            if (customerFilter.SelectedItem is ComboBoxItem selected &&
                string.Equals(selected.Content?.ToString(), customerFilter.Text, StringComparison.CurrentCulture))
                return;

            FilterCustomers(customerFilter.Text ?? string.Empty);
        };
        reportDate.TextChanged += (_, _) => exportButton.IsEnabled = false;

        var reportHeader = new StackPanel
        {
            Spacing = 3,
            Children =
            {
                new TextBlock { Text = Tr.Loc.TranslateText("Reports"), FontSize = 28, FontWeight = FontWeight.Bold },
                new TextBlock
                {
                    Text = Tr.Loc.TranslateText("Review customer accounts, subscriptions and daily activity"),
                    FontSize = 13,
                    Foreground = new SolidColorBrush(Color.Parse("#8B96A5"))
                }
            }
        };
        var actionRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        actionRow.Children.Add(generate);
        actionRow.Children.Add(exportButton);

        var summaryContent = new StackPanel { Spacing = 6 };
        summaryContent.Children.Add(new TextBlock
        {
            Text = Tr.Loc.TranslateText("Report summary"),
            FontSize = 12,
            FontWeight = FontWeight.Bold,
            Foreground = new SolidColorBrush(Color.Parse("#9BA8B8"))
        });
        summaryContent.Children.Add(summary);

        var summaryCard = new Border
        {
            Padding = new Thickness(16),
            Background = new SolidColorBrush(Color.Parse("#202733")),
            BorderBrush = new SolidColorBrush(Color.Parse("#354152")),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Child = summaryContent
        };
        var resultsCard = new Border
        {
            Padding = new Thickness(8),
            Background = new SolidColorBrush(Color.Parse("#151B24")),
            BorderBrush = new SolidColorBrush(Color.Parse("#303B4A")),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Child = results
        };
        results.FontSize = 14;
        results.Padding = new Thickness(4);
        results.MinHeight = 380;

        var content = new StackPanel
        {
            Margin = new Thickness(22),
            Spacing = 16,
            Children = { reportHeader, filters, actionRow, summaryCard, resultsCard }
        };
        Content = new ScrollViewer { Content = content };
        Opened += async (_, _) =>
        {
            await LoadCustomersAsync();
            UpdateFilterVisibility();
            await GenerateReportAsync();
        };
    }


    private async Task ExportReportAsync()
    {
        var selectedItem = reportType.SelectedItem as ComboBoxItem;
        var reportName = selectedItem?.Content?.ToString() ?? "Report";
        var safeName = string.Concat(reportName.Select(character =>
            Path.GetInvalidFileNameChars().Contains(character) ? '_' : character));
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = Tr.Loc.TranslateText("Export report to Excel"),
            SuggestedFileName = $"{safeName}_{DateTime.Now:yyyyMMdd_HHmm}.xlsx",
            DefaultExtension = "xlsx",
            FileTypeChoices = new[]
            {
                new FilePickerFileType("Excel Workbook") { Patterns = new[] { "*.xlsx" } }
            }
        });
        if (file is null) return;

        var detailLines = results.ItemsSource is IEnumerable<string> lines
            ? lines.ToList()
            : new List<string>();
        try
        {
            await using var output = await file.OpenWriteAsync();
            await ExcelReportExporter.WriteAsync(output, reportName, summary.Text ?? string.Empty, detailLines);
        }
        catch (Exception ex)
        {
            summary.Text = Tr.Loc.TranslateText("The Excel file could not be saved.") + " " + ex.Message;
        }
    }

    private void UpdateFilterVisibility()
    {
        var selected = (reportType.SelectedItem as ComboBoxItem)?.Tag?.ToString();
        customerFilterPanel.IsVisible = selected == "customers";
        dateFilterPanel.IsVisible = selected == "daily";
    }

    private async Task LoadCustomersAsync()
    {
        await using var db = new PharmacyDbContext(PharmacyDb.Options());
        var customers = await db.Customers.AsNoTracking()
            .OrderBy(customer => customer.FullName)
            .ToListAsync();
        customerOptions = customers.Select(customer => new CustomerFilterOption(
            customer.Id,
            $"{customer.CustomerNumber} | {customer.FullName}",
            string.Join(" ", new[]
            {
                customer.CustomerNumber, customer.FullName, customer.Address,
                customer.Postcode, customer.City, customer.Telephone,
                customer.Phone, customer.Amka, customer.Insurance
            }.Where(value => !string.IsNullOrWhiteSpace(value)))))
            .ToList();
        isFilteringCustomers = true;
        customerFilter.Items.Clear();
        customerFilter.Items.Add(new ComboBoxItem { Content = Tr.Loc.TranslateText("All customers"), Tag = null });
        foreach (var option in customerOptions)
            customerFilter.Items.Add(new ComboBoxItem { Content = option.Display, Tag = option.Id });
        customerFilter.SelectedIndex = 0;
        customerFilter.Text = Tr.Loc.TranslateText("All customers");
        isFilteringCustomers = false;
    }

    private void FilterCustomers(string searchText)
    {
        // Defer collection changes until Avalonia has finished processing the
        // ComboBox text/selection change that raised this callback.
        Dispatcher.UIThread.Post(() => ApplyCustomerFilter(searchText), DispatcherPriority.Background);
    }

    private void ApplyCustomerFilter(string searchText)
    {
        if (!IsVisible || isFilteringCustomers)
            return;

        var query = searchText.Trim();
        var matches = string.IsNullOrWhiteSpace(query)
            ? customerOptions
            : customerOptions.Where(option => option.SearchText.Contains(query, StringComparison.CurrentCultureIgnoreCase)).ToList();

        // Preserve an existing customer selection only when it is still present.
        var selectedId = (customerFilter.SelectedItem as ComboBoxItem)?.Tag as int?;
        isFilteringCustomers = true;
        try
        {
            customerFilter.Items.Clear();
            var allCustomersItem = new ComboBoxItem
            {
                Content = Tr.Loc.TranslateText("All customers"),
                Tag = null
            };
            customerFilter.Items.Add(allCustomersItem);

            ComboBoxItem? selectedMatch = null;
            foreach (var option in matches)
            {
                var item = new ComboBoxItem { Content = option.Display, Tag = option.Id };
                customerFilter.Items.Add(item);
                if (selectedId.HasValue && option.Id == selectedId.Value)
                    selectedMatch = item;
            }

            if (selectedMatch is not null)
                customerFilter.SelectedItem = selectedMatch;
            else if (string.IsNullOrWhiteSpace(query))
                customerFilter.SelectedItem = allCustomersItem;
            else
                customerFilter.SelectedIndex = -1;

            customerFilter.Text = searchText;
        }
        finally
        {
            isFilteringCustomers = false;
        }
    }

    private sealed record CustomerFilterOption(int Id, string Display, string SearchText);

    private async Task GenerateReportAsync()
    {
        var selected = (reportType.SelectedItem as ComboBoxItem)?.Tag?.ToString();
        exportButton.IsEnabled = false;
        try
        {
            switch (selected)
            {
                case "customers":
                    await GenerateCustomerReportAsync();
                    break;
                case "upcoming":
                    await GenerateUpcomingSubscriptionsAsync();
                    break;
                case "daily":
                    await GenerateDailyStatusAsync();
                    break;
                default:
                    summary.Text = Tr.Loc.TranslateText("Select a report.");
                    results.ItemsSource = Array.Empty<string>();
                    break;
            }
            exportButton.IsEnabled = true;
        }
        catch (Exception ex)
        {
            summary.Text = Tr.Loc.TranslateText("The report could not be generated.") + " " + ex.Message;
            results.ItemsSource = Array.Empty<string>();
        }
    }

    private async Task GenerateCustomerReportAsync()
    {
        await using var db = new PharmacyDbContext(PharmacyDb.Options());
        var selectedCustomerId = (customerFilter.SelectedItem as ComboBoxItem)?.Tag is int customerId ? customerId : (int?)null;
        var customersQuery = db.Customers.AsNoTracking().AsQueryable();
        if (selectedCustomerId.HasValue)
            customersQuery = customersQuery.Where(customer => customer.Id == selectedCustomerId.Value);
        var customers = await customersQuery.OrderBy(customer => customer.FullName).ToListAsync();
        if (customers.Count == 0)
        {
            summary.Text = Tr.Loc.TranslateText("No customers found.");
            results.ItemsSource = Array.Empty<string>();
            return;
        }

        var lines = new List<string>();
        decimal grandBalance = 0;
        decimal grandPayments = 0;
        foreach (var customer in customers)
        {
            var ledger = await db.LedgerEntries.AsNoTracking()
                .Where(entry => entry.CustomerId == customer.Id)
                .OrderBy(entry => entry.EntryDate)
                .ToListAsync();
            var payments = await db.Payments.AsNoTracking()
                .Where(payment => payment.CustomerId == customer.Id)
                .OrderBy(payment => payment.PaidAt)
                .ToListAsync();
            var charges = ledger.Where(entry => entry.Type == LedgerEntryType.Charge || entry.Type == LedgerEntryType.Adjustment).Sum(entry => entry.Amount);
            var credits = ledger.Where(entry => entry.Type == LedgerEntryType.Payment || entry.Type == LedgerEntryType.Reversal).Sum(entry => entry.Amount);
            var balance = charges - credits;
            var paid = payments.Sum(payment => payment.Amount);
            grandBalance += balance;
            grandPayments += paid;

            lines.Add("============================================================");
            lines.Add($"{customer.CustomerNumber} | {customer.FullName}");
            lines.Add($"{Tr.Loc.TranslateText("Total charges")}: {charges:C}    {Tr.Loc.TranslateText("Total payments")}: {paid:C}    {Tr.Loc.TranslateText("Balance owed")}: {balance:C}");
            lines.Add(Tr.Loc.TranslateText("Payment history") + ":");
            if (payments.Count == 0)
                lines.Add("  " + Tr.Loc.TranslateText("No payments recorded."));
            else
                lines.AddRange(payments.Select(payment =>
                    $"  {payment.PaidAt:dd/MM/yyyy HH:mm} | {payment.Amount:C} | {Tr.Loc.TranslateText(payment.Method)} | {payment.Reference} | {payment.Notes}"));
            lines.Add(Tr.Loc.TranslateText("Account ledger") + ":");
            if (ledger.Count == 0)
                lines.Add("  " + Tr.Loc.TranslateText("No ledger entries."));
            else
                lines.AddRange(ledger.Select(entry =>
                    $"  {entry.EntryDate:dd/MM/yyyy HH:mm} | {Tr.Loc.TranslateText(entry.Type.ToString())} | {entry.Amount:C} | {entry.Description}"));
        }

        summary.Text = $"{Tr.Loc.TranslateText("Customers")}: {customers.Count}    |    {Tr.Loc.TranslateText("Payments received")}: {grandPayments:C}    |    {Tr.Loc.TranslateText("Total balance owed")}: {grandBalance:C}";
        results.ItemsSource = lines;
    }

    private async Task GenerateUpcomingSubscriptionsAsync()
    {
        var start = DateTime.Today;
        var end = start.AddDays(7);
        await using var db = new PharmacyDbContext(PharmacyDb.Options());
        var subscriptions = await db.Subscriptions.AsNoTracking()
            .Include(subscription => subscription.Customer)
            .Where(subscription => subscription.Status == SubscriptionStatus.Active &&
                subscription.NextDueDate >= start && subscription.NextDueDate < end)
            .OrderBy(subscription => subscription.NextDueDate)
            .ThenBy(subscription => subscription.Customer!.FullName)
            .ToListAsync();

        var lines = subscriptions.Select(subscription =>
            $"{subscription.NextDueDate:dd/MM/yyyy} | {subscription.Customer?.CustomerNumber} | {subscription.Customer?.FullName} | {subscription.Title} | {subscription.Frequency} | {subscription.Barcode ?? $"SUB-{subscription.Id:D8}"}").ToList();
        if (lines.Count == 0)
            lines.Add(Tr.Loc.TranslateText("No open subscriptions are due in the next 7 days."));
        summary.Text = $"{Tr.Loc.TranslateText("Period")}: {start:dd/MM/yyyy} - {end.AddDays(-1):dd/MM/yyyy}    |    {Tr.Loc.TranslateText("Open subscriptions due")}: {subscriptions.Count}";
        results.ItemsSource = lines;
    }

    private async Task GenerateDailyStatusAsync()
    {
        if (!DateTime.TryParse(reportDate.Text, out var selectedDate))
        {
            summary.Text = Tr.Loc.TranslateText("Enter a valid date using yyyy-MM-dd.");
            results.ItemsSource = Array.Empty<string>();
            return;
        }

        var dayStart = selectedDate.Date;
        var dayEnd = dayStart.AddDays(1);
        await using var db = new PharmacyDbContext(PharmacyDb.Options());
        var subscriptions = await db.Subscriptions.AsNoTracking()
            .Include(subscription => subscription.Customer)
            .ToListAsync();
        var auditEntries = await db.SubscriptionAuditEntries.AsNoTracking()
            .Include(entry => entry.Subscription)
            .ThenInclude(subscription => subscription!.Customer)
            .ToListAsync();
        var dailyAudit = auditEntries.Where(entry =>
        {
            var localTime = DateTime.SpecifyKind(entry.ChangedAt, DateTimeKind.Utc).ToLocalTime();
            return localTime >= dayStart && localTime < dayEnd;
        }).OrderBy(entry => entry.ChangedAt).ToList();

        var borrowings = await db.Borrowings.AsNoTracking()
            .Include(borrowing => borrowing.Customer)
            .Where(borrowing =>
                (borrowing.BorrowedAt >= dayStart && borrowing.BorrowedAt < dayEnd) ||
                (borrowing.ReturnedAt.HasValue && borrowing.ReturnedAt.Value >= dayStart && borrowing.ReturnedAt.Value < dayEnd))
            .OrderBy(borrowing => borrowing.BorrowedAt)
            .ToListAsync();

        var opened = dailyAudit.Where(entry =>
            entry.Action.Equals("Created", StringComparison.OrdinalIgnoreCase) ||
            entry.NewStatus == (int)SubscriptionStatus.Active && entry.PreviousStatus != (int)SubscriptionStatus.Active).ToList();
        var closed = dailyAudit.Where(entry =>
            entry.NewStatus == (int)SubscriptionStatus.Executed && entry.PreviousStatus != (int)SubscriptionStatus.Executed).ToList();

        var lines = new List<string>
        {
            $"{Tr.Loc.TranslateText("Subscriptions opened on this date")}: {opened.Count}",
            $"{Tr.Loc.TranslateText("Subscriptions closed on this date")}: {closed.Count}",
            $"{Tr.Loc.TranslateText("Currently open subscriptions")}: {subscriptions.Count(item => item.Status == SubscriptionStatus.Active)}",
            $"{Tr.Loc.TranslateText("Currently closed / executed subscriptions")}: {subscriptions.Count(item => item.Status == SubscriptionStatus.Executed)}",
            "",
            Tr.Loc.TranslateText("Current open subscriptions") + ":"
        };
        lines.AddRange(subscriptions.Where(item => item.Status == SubscriptionStatus.Active)
            .OrderBy(item => item.NextDueDate)
            .Select(item => $"  {item.Customer?.CustomerNumber} | {item.Customer?.FullName} | {item.Title} | {Tr.Loc.TranslateText("Next due")}: {item.NextDueDate:dd/MM/yyyy}"));
        if (!subscriptions.Any(item => item.Status == SubscriptionStatus.Active)) lines.Add("  " + Tr.Loc.TranslateText("None"));
        lines.Add("");
        lines.Add(Tr.Loc.TranslateText("Current closed / executed subscriptions") + ":");
        lines.AddRange(subscriptions.Where(item => item.Status == SubscriptionStatus.Executed)
            .OrderBy(item => item.Customer?.FullName)
            .Select(item => $"  {item.Customer?.CustomerNumber} | {item.Customer?.FullName} | {item.Title} | {Tr.Loc.TranslateText("Last changed")}: {item.LastModifiedAt:dd/MM/yyyy}"));
        if (!subscriptions.Any(item => item.Status == SubscriptionStatus.Executed)) lines.Add("  " + Tr.Loc.TranslateText("None"));
        lines.Add("");
        lines.Add(Tr.Loc.TranslateText("Opened subscriptions") + ":");
        lines.AddRange(opened.Select(entry =>
            $"  {DateTime.SpecifyKind(entry.ChangedAt, DateTimeKind.Utc).ToLocalTime():HH:mm} | {entry.Subscription?.Customer?.CustomerNumber} | {entry.Subscription?.Customer?.FullName} | {entry.Subscription?.Title}"));
        if (opened.Count == 0) lines.Add("  " + Tr.Loc.TranslateText("None"));
        lines.Add("");
        lines.Add(Tr.Loc.TranslateText("Closed / executed subscriptions") + ":");
        lines.AddRange(closed.Select(entry =>
            $"  {DateTime.SpecifyKind(entry.ChangedAt, DateTimeKind.Utc).ToLocalTime():HH:mm} | {entry.Subscription?.Customer?.CustomerNumber} | {entry.Subscription?.Customer?.FullName} | {entry.Subscription?.Title}"));
        if (closed.Count == 0) lines.Add("  " + Tr.Loc.TranslateText("None"));
        lines.Add("");
        lines.Add(Tr.Loc.TranslateText("Borrowed and returned medicines") + ":");
        foreach (var borrowing in borrowings)
        {
            var borrowedToday = borrowing.BorrowedAt >= dayStart && borrowing.BorrowedAt < dayEnd;
            var returnedToday = borrowing.ReturnedAt.HasValue && borrowing.ReturnedAt.Value >= dayStart && borrowing.ReturnedAt.Value < dayEnd;
            var activity = borrowedToday && returnedToday ? "Borrowed and returned" : borrowedToday ? "Borrowed" : "Returned";
            var activityTime = returnedToday && !borrowedToday ? borrowing.ReturnedAt!.Value : borrowing.BorrowedAt;
            lines.Add($"  {activityTime:HH:mm} | {activity} | {borrowing.Customer?.CustomerNumber} | {borrowing.Customer?.FullName} | {borrowing.MedicineName} | Qty {borrowing.Quantity} | {borrowing.Status}");
        }
        if (borrowings.Count == 0) lines.Add("  " + Tr.Loc.TranslateText("No medicine borrowing or return activity on this date."));

        summary.Text = $"{dayStart:dddd, dd MMMM yyyy}    |    {Tr.Loc.TranslateText("Opened")}: {opened.Count}    |    {Tr.Loc.TranslateText("Closed")}: {closed.Count}    |    {Tr.Loc.TranslateText("Borrowing / return records")}: {borrowings.Count}";
        results.ItemsSource = lines;
    }
}
