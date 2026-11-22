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
    private readonly TextBox customerSearch = new() { MinWidth = 300, Watermark = "Search customer..." };
    private readonly ListBox customerSuggestions = new() { MaxHeight = 190, MinWidth = 300 };
    private List<CustomerFilterOption> customerOptions = new();
    private List<CustomerFilterOption> visibleCustomerMatches = new();
    private int? selectedCustomerId;
    private bool isUpdatingCustomerSearch;
    private readonly TextBox reportDate = new() { MinWidth = 150, Text = DateTime.Today.ToString("yyyy-MM-dd") };
    private readonly TextBlock summary = new() { FontSize = 15, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap };
    private readonly StackPanel resultsContent = new() { Spacing = 6 };
    private List<string> currentReportLines = new();
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
        customerFilterPanel.Children.Add(customerSearch);
        customerFilterPanel.Children.Add(customerSuggestions);
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
        customerSearch.TextChanged += (_, _) =>
        {
            if (!isUpdatingCustomerSearch)
            {
                selectedCustomerId = null;
                FilterCustomers(customerSearch.Text ?? string.Empty);
                exportButton.IsEnabled = false;
            }
        };
        customerSuggestions.SelectionChanged += (_, _) =>
        {
            var selectedIndex = customerSuggestions.SelectedIndex;
            if (selectedIndex < 0 || selectedIndex >= visibleCustomerMatches.Count)
                return;

            var option = visibleCustomerMatches[selectedIndex];
            selectedCustomerId = option.Id;
            isUpdatingCustomerSearch = true;
            customerSearch.Text = option.Display;
            isUpdatingCustomerSearch = false;
            customerSuggestions.IsVisible = false;
            exportButton.IsEnabled = false;
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
        var resultsHeader = new TextBlock
        {
            Text = Tr.Loc.TranslateText("Report details"),
            FontSize = 16,
            FontWeight = FontWeight.Bold,
            Margin = new Thickness(2, 0, 2, 6)
        };
        var resultsCard = new Border
        {
            Padding = new Thickness(14),
            Background = new SolidColorBrush(Color.Parse("#151B24")),
            BorderBrush = new SolidColorBrush(Color.Parse("#303B4A")),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Child = new StackPanel
            {
                Spacing = 8,
                Children =
                {
                    resultsHeader,
                    new ScrollViewer
                    {
                        MinHeight = 360,
                        MaxHeight = 560,
                        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                        Content = resultsContent
                    }
                }
            }
        };

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

        var detailLines = currentReportLines.ToList();
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
        var customers = await db.Customers.AsNoTracking().OrderBy(customer => customer.FullName).ToListAsync();
        customerOptions = customers.Select(customer => new CustomerFilterOption(customer.Id,
            $"{customer.CustomerNumber} | {customer.FullName}",
            string.Join(" ", new[] { customer.CustomerNumber, customer.FullName, customer.Address, customer.Postcode, customer.City, customer.Telephone, customer.Phone, customer.Amka, customer.Insurance }
                .Where(value => !string.IsNullOrWhiteSpace(value))))).ToList();
        customerSearch.Text = string.Empty;
        selectedCustomerId = null;
        visibleCustomerMatches.Clear();
        customerSuggestions.ItemsSource = Array.Empty<string>();
        customerSuggestions.IsVisible = false;
    }

    private void FilterCustomers(string searchText)
    {
        var query = searchText.Trim();
        if (string.IsNullOrWhiteSpace(query))
        {
            customerSuggestions.ItemsSource = Array.Empty<CustomerFilterOption>();
            customerSuggestions.IsVisible = false;
            return;
        }
        var matches = customerOptions.Where(option => option.SearchText.Contains(query, StringComparison.CurrentCultureIgnoreCase)).Take(100).ToArray();
        customerSuggestions.SelectedIndex = -1;
        visibleCustomerMatches = matches.ToList();
        customerSuggestions.ItemsSource = matches.Select(option => option.Display).ToArray();
        customerSuggestions.IsVisible = matches.Length > 0;
    }
    private sealed record CustomerFilterOption(int Id, string Display, string SearchText);

    private void RenderResults(IEnumerable<string> sourceLines)
    {
        currentReportLines = sourceLines.ToList();
        resultsContent.Children.Clear();
        var rowIndex = 0;

        foreach (var rawLine in currentReportLines)
        {
            if (string.IsNullOrWhiteSpace(rawLine))
            {
                resultsContent.Children.Add(new Border { Height = 5 });
                continue;
            }

            var line = rawLine.Trim();
            if (line.All(character => character == '='))
            {
                resultsContent.Children.Add(new Border
                {
                    Height = 1,
                    Margin = new Thickness(0, 5),
                    Background = new SolidColorBrush(Color.Parse("#344355"))
                });
                continue;
            }

            var isSection = line.EndsWith(':');
            var isIndented = rawLine.StartsWith("  ", StringComparison.Ordinal);
            var text = new TextBlock
            {
                Text = line,
                TextWrapping = TextWrapping.Wrap,
                FontSize = isSection ? 14 : isIndented ? 12 : 13,
                FontWeight = isSection ? FontWeight.Bold : isIndented ? FontWeight.Normal : FontWeight.SemiBold,
                Foreground = new SolidColorBrush(Color.Parse(isSection ? "#75C8F2" : isIndented ? "#C3CEDA" : "#F0F4F8"))
            };
            var background = isSection ? "#202F40" : (rowIndex++ % 2 == 0 ? "#1A2430" : "#17212C");
            resultsContent.Children.Add(new Border
            {
                Padding = new Thickness(isIndented ? 12 : 10, isSection ? 9 : 7),
                Margin = new Thickness(0, 1),
                CornerRadius = new CornerRadius(6),
                Background = new SolidColorBrush(Color.Parse(background)),
                BorderBrush = new SolidColorBrush(Color.Parse(isSection ? "#344E66" : "#263443")),
                BorderThickness = new Thickness(isSection ? 1 : 0),
                Child = text
            });
        }

        if (currentReportLines.Count == 0)
        {
            resultsContent.Children.Add(new TextBlock
            {
                Text = Tr.Loc.TranslateText("No report details available."),
                Foreground = new SolidColorBrush(Color.Parse("#9BA8B8")),
                FontStyle = FontStyle.Italic,
                Margin = new Thickness(8)
            });
        }
    }

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
                    RenderResults(Array.Empty<string>());
                    break;
            }
            exportButton.IsEnabled = true;
        }
        catch (Exception ex)
        {
            summary.Text = Tr.Loc.TranslateText("The report could not be generated.") + " " + ex.Message;
            RenderResults(Array.Empty<string>());
        }
    }

    private async Task GenerateCustomerReportAsync()
    {
        await using var db = new PharmacyDbContext(PharmacyDb.Options());
        var selectedId = selectedCustomerId;
        var customersQuery = db.Customers.AsNoTracking().AsQueryable();
        if (selectedId.HasValue)
            customersQuery = customersQuery.Where(customer => customer.Id == selectedId.Value);
        var customers = await customersQuery.OrderBy(customer => customer.FullName).ToListAsync();
        if (customers.Count == 0)
        {
            summary.Text = Tr.Loc.TranslateText("No customers found.");
            RenderResults(Array.Empty<string>());
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
        RenderResults(lines);
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
            $"{subscription.NextDueDate:dd/MM/yyyy} | {subscription.Customer?.CustomerNumber} | {subscription.Customer?.FullName} | {subscription.Title} | {subscription.Frequency}").ToList();
        if (lines.Count == 0)
            lines.Add(Tr.Loc.TranslateText("No open subscriptions are due in the next 7 days."));
        summary.Text = $"{Tr.Loc.TranslateText("Period")}: {start:dd/MM/yyyy} - {end.AddDays(-1):dd/MM/yyyy}    |    {Tr.Loc.TranslateText("Open subscriptions due")}: {subscriptions.Count}";
        RenderResults(lines);
    }

    private async Task GenerateDailyStatusAsync()
    {
        if (!DateTime.TryParse(reportDate.Text, out var selectedDate))
        {
            summary.Text = Tr.Loc.TranslateText("Enter a valid date using yyyy-MM-dd.");
            RenderResults(Array.Empty<string>());
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
        RenderResults(lines);
    }
}
