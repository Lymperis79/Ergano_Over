using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Microsoft.EntityFrameworkCore;
using SPharmacyLog.Data;
using SPharmacyLog.Data.Entities;
using SPharmacyLog.UI.Localization;
using SPharmacyLog.UI.Services;

namespace SPharmacyLog.UI.Views;

public sealed class MedicineStockWindow : Window
{
    private readonly string currentUser;
    private readonly TextBox search = new() { MinWidth = 260, PlaceholderText = Tr.Loc.TranslateText("Medicine / barcode / lot") };
    private readonly StackPanel stockRows = new() { Spacing = 2 };
    private readonly StackPanel historyRows = new() { Spacing = 2 };
    private readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap };
    private List<StockRow> currentStock = new();

    public MedicineStockWindow(string username)
    {
        currentUser = string.IsNullOrWhiteSpace(username) ? "Unknown user" : username;
        Title = Tr.Loc.TranslateText("Medicine stock control");
        Width = 1180; Height = 820; MinWidth = 900; MinHeight = 600;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var heading = new TextBlock { Text = Tr.Loc.TranslateText("Medicine stock control"), FontSize = 25, FontWeight = FontWeight.Bold };
        var count = ActionButton("Stock count", "▦", async () => await OpenMovementFormAsync(MedicineStockMovementType.Count));
        var add = ActionButton("Add stock", "＋", async () => await OpenMovementFormAsync(MedicineStockMovementType.Add));
        var remove = ActionButton("Remove stock", "−", async () => await OpenMovementFormAsync(MedicineStockMovementType.Remove));
        var refresh = new Button { Content = Tr.Loc.TranslateText("Refresh"), MinHeight = 36 };
        refresh.Click += async (_, _) => await LoadAsync();
        var find = new Button { Content = Tr.Loc.TranslateText("Search") };
        find.Click += async (_, _) => await LoadAsync();
        search.KeyDown += async (_, e) => { if (e.Key == Key.Enter) { e.Handled = true; await LoadAsync(); } };
        var toolbar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { search, find, count, add, remove, refresh } };

        var stockHeader = HeaderGrid(new[] { "Medicine", "Expiry", "Batch / Lot", "Quantity", "Last movement", "Counted / moved by" }, "2*,*,*,*,*,*");
        var historyHeader = HeaderGrid(new[] { "Date", "Medicine", "Action", "Qty / Count", "Expiry", "Batch", "User", "Notes" }, "*,2*,*,*,*,*,*,2*");
        var stockSection = new StackPanel { Spacing = 5, Children = {
            new TextBlock { Text = Tr.Loc.TranslateText("CURRENT STOCK"), FontWeight = FontWeight.Bold, FontSize = 15 },
            stockHeader,
            new ScrollViewer { Content = stockRows, VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto }
        }};
        var historySection = new StackPanel { Spacing = 5, Children = {
            new TextBlock { Text = Tr.Loc.TranslateText("STOCK MOVEMENT HISTORY"), FontWeight = FontWeight.Bold, FontSize = 15 },
            historyHeader,
            new ScrollViewer { Content = historyRows, VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto }
        }};
        var root = new Grid { Margin = new Thickness(18), RowDefinitions = new RowDefinitions("Auto,Auto,2*,2*,Auto"), RowSpacing = 10, Children = { heading, toolbar, stockSection, historySection, status } };
        Grid.SetRow(toolbar, 1); Grid.SetRow(stockSection, 2); Grid.SetRow(historySection, 3); Grid.SetRow(status, 4);
        Content = root;
        _ = LoadAsync();
    }

    private Button ActionButton(string key, string glyph, Func<Task> action)
    {
        var button = new Button { Content = $"{glyph} {Tr.Loc.TranslateText(key)}", MinHeight = 36, Classes = { "accent" } };
        button.Click += async (_, _) => await action();
        return button;
    }

    private static Grid HeaderGrid(string[] labels, string columns)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions(columns), Background = new SolidColorBrush(Color.Parse("#263447")) };
        for (var i = 0; i < labels.Length; i++)
        {
            var label = new TextBlock { Text = Tr.Loc.TranslateText(labels[i]), FontWeight = FontWeight.Bold, Margin = new Thickness(7, 6), VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(label, i); grid.Children.Add(label);
        }
        return grid;
    }

    private async Task LoadAsync()
    {
        try
        {
            await using var db = new PharmacyDbContext(PharmacyDb.Options());
            var medicines = await db.Medicines.AsNoTracking().ToListAsync();
            var movements = await db.MedicineStockMovements.Include(x => x.Medicine).AsNoTracking()
                .OrderBy(x => x.MovementDate).ThenBy(x => x.Id).ToListAsync();
            var term = search.Text?.Trim();
            var stock = new List<StockRow>();
            foreach (var medicineGroup in movements.GroupBy(m => m.MedicineId))
            {
                var med = medicines.FirstOrDefault(m => m.Id == medicineGroup.Key);
                if (med == null) continue;
                foreach (var bucket in CalculateBalances(medicineGroup))
                {
                    if (bucket.Value <= 0) continue;
                    var last = medicineGroup.Where(m => m.ExpiryDate?.Date == bucket.Key.Expiry && (m.BatchLot ?? "") == bucket.Key.Lot).OrderByDescending(m => m.MovementDate).ThenByDescending(m => m.Id).First();
                    stock.Add(new StockRow(med.Name, med.Barcode ?? "", bucket.Key.Expiry, bucket.Key.Lot, bucket.Value, last.MovementDate, last.PerformedBy, med.Id));
                }
            }
            if (!string.IsNullOrWhiteSpace(term))
                stock = stock.Where(s => s.Name.Contains(term, StringComparison.CurrentCultureIgnoreCase)
                    || s.Barcode.Contains(term, StringComparison.CurrentCultureIgnoreCase)
                    || s.Lot.Contains(term, StringComparison.CurrentCultureIgnoreCase)).ToList();
            currentStock = stock.OrderBy(s => s.Name).ThenBy(s => s.Expiry).ToList();
            stockRows.Children.Clear();
            foreach (var row in currentStock)
            {
                var values = new[] { row.Name, row.Expiry?.ToString("yyyy-MM-dd") ?? "—", row.Lot, row.Quantity.ToString("0.##"), row.LastMovement.ToString("yyyy-MM-dd HH:mm"), row.LastUser };
                var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("2*,*,*,*,*,*"), MinWidth = 900 };
                for (var i = 0; i < values.Length; i++)
                {
                    var cell = new TextBlock { Text = values[i], Margin = new Thickness(7, 6), TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
                    if (i == 3) cell.FontWeight = FontWeight.Bold;
                    Grid.SetColumn(cell, i); grid.Children.Add(cell);
                }
                stockRows.Children.Add(new Border { BorderBrush = new SolidColorBrush(Color.Parse("#354152")), BorderThickness = new Thickness(0, 0, 0, 1), Child = grid });
            }

            var filteredHistory = movements.OrderByDescending(m => m.MovementDate).ThenByDescending(m => m.Id);
            if (!string.IsNullOrWhiteSpace(term))
                filteredHistory = filteredHistory.Where(m => m.Medicine!.Name.Contains(term, StringComparison.CurrentCultureIgnoreCase)
                    || (m.BarcodeScanned?.Contains(term, StringComparison.CurrentCultureIgnoreCase) ?? false)
                    || (m.BatchLot?.Contains(term, StringComparison.CurrentCultureIgnoreCase) ?? false)).OrderByDescending(m => m.MovementDate).ThenByDescending(m => m.Id);
            historyRows.Children.Clear();
            foreach (var m in filteredHistory.Take(500))
            {
                var action = m.Type switch { MedicineStockMovementType.Count => Tr.Loc.TranslateText("Stock count"), MedicineStockMovementType.Add => Tr.Loc.TranslateText("Stock added"), _ => Tr.Loc.TranslateText("Stock removed") };
                var amount = m.Type == MedicineStockMovementType.Count ? $"= {m.CountedQuantity:0.##}" : $"{(m.Type == MedicineStockMovementType.Remove ? "-" : "+")}{m.Quantity:0.##}";
                var values = new[] { m.MovementDate.ToString("yyyy-MM-dd HH:mm"), m.Medicine?.Name ?? "", action, amount, m.ExpiryDate?.ToString("yyyy-MM-dd") ?? "—", m.BatchLot ?? "", m.PerformedBy, m.Notes ?? "" };
                var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,2*,*,*,*,*,*,2*"), MinWidth = 1050 };
                for (var i = 0; i < values.Length; i++)
                {
                    var cell = new TextBlock { Text = values[i], Margin = new Thickness(6, 5), TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
                    Grid.SetColumn(cell, i); grid.Children.Add(cell);
                }
                historyRows.Children.Add(new Border { BorderBrush = new SolidColorBrush(Color.Parse("#354152")), BorderThickness = new Thickness(0, 0, 0, 1), Child = grid });
            }
            status.Text = $"{Tr.Loc.TranslateText("Stock lines")}: {currentStock.Count}  |  {Tr.Loc.TranslateText("Total quantity")}: {currentStock.Sum(s => s.Quantity):0.##}  |  {Tr.Loc.TranslateText("Movement records")}: {movements.Count}";
        }
        catch (Exception ex) { status.Text = Tr.Loc.TranslateText("Could not load stock") + ": " + ex.Message; }
    }

    private async Task OpenMovementFormAsync(MedicineStockMovementType type)
    {
        var formTitle = type switch { MedicineStockMovementType.Count => "Stock count", MedicineStockMovementType.Add => "Add stock", _ => "Remove stock" };
        var isRemove = type == MedicineStockMovementType.Remove;
        var isCount = type == MedicineStockMovementType.Count;
        var barcode = new TextBox { MinWidth = 320, PlaceholderText = Tr.Loc.TranslateText("Scan medicine barcode") };
        var medicineSearch = new TextBox { MinWidth = 320, PlaceholderText = Tr.Loc.TranslateText("Type medicine name or barcode") };
        var medicine = new ComboBox { MinWidth = 320 };
        var scanFeedback = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = Brushes.LightGray };
        var quantity = new TextBox { MinWidth = 320, Text = "1" };
        var expiry = new DatePicker { SelectedDate = null };
        var batch = new TextBox { MinWidth = 320, PlaceholderText = Tr.Loc.TranslateText("Optional batch / lot") };
        var notes = new TextBox { MinWidth = 320, AcceptsReturn = true, Height = 65, TextWrapping = TextWrapping.Wrap };
        var availableText = new TextBlock
        {
            Text = isRemove ? Tr.Loc.TranslateText("Select a medicine to see available stock.") : "",
            FontWeight = FontWeight.SemiBold,
            TextWrapping = TextWrapping.Wrap
        };
        var dialog = new Window
        {
            Title = Tr.Loc.TranslateText(formTitle),
            Width = 520,
            Height = isCount ? 570 : 510,
            MinWidth = 440,
            MinHeight = 440,
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };

        List<MedicineCatalogItem> allMedicines;
        List<MedicineCatalogItem> visibleMedicines;
        Dictionary<int, string> searchIndex;
        Dictionary<string, int> barcodeIndex;
        await using (var db = new PharmacyDbContext(PharmacyDb.Options()))
        {
            allMedicines = await db.Medicines.AsNoTracking().OrderBy(m => m.Name).ToListAsync();
            var alternateBarcodes = await db.MedicineBarcodes.AsNoTracking()
                .Select(b => new { b.MedicineId, b.BarcodeValue })
                .ToListAsync();
            var alternateGroups = alternateBarcodes
                .GroupBy(b => b.MedicineId)
                .ToDictionary(g => g.Key, g => g.Select(b => b.BarcodeValue).ToList());

            searchIndex = allMedicines.ToDictionary(
                m => m.Id,
                m => string.Join(" ", new[] { m.Name, m.Barcode ?? "" }
                    .Concat(alternateGroups.GetValueOrDefault(m.Id) ?? new List<string>())));
            barcodeIndex = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in allMedicines)
            {
                if (!string.IsNullOrWhiteSpace(item.Barcode))
                    barcodeIndex[item.Barcode.Trim()] = item.Id;
                foreach (var alternate in alternateGroups.GetValueOrDefault(item.Id) ?? new List<string>())
                    if (!string.IsNullOrWhiteSpace(alternate))
                        barcodeIndex[alternate.Trim()] = item.Id;
            }
        }

        visibleMedicines = allMedicines.ToList();
        medicine.ItemsSource = visibleMedicines.Select(m => m.Name).ToList();
        var searchVersion = 0;
        medicineSearch.TextChanged += (_, _) =>
        {
            var version = ++searchVersion;
            _ = ApplyMedicineFilterAsync(version);
        };

        async Task ApplyMedicineFilterAsync(int version)
        {
            // Debounce input so the catalogue is filtered only after the user pauses typing.
            await Task.Delay(200);
            if (version != searchVersion) return;

            var previouslySelectedId = visibleMedicines.ElementAtOrDefault(medicine.SelectedIndex)?.Id;
            var filter = medicineSearch.Text?.Trim();
            var matches = string.IsNullOrWhiteSpace(filter)
                ? allMedicines
                : allMedicines.Where(m => searchIndex[m.Id].Contains(filter, StringComparison.CurrentCultureIgnoreCase)).ToList();

            if (version != searchVersion) return;
            visibleMedicines = matches;
            medicine.ItemsSource = visibleMedicines.Select(m => m.Name).ToList();
            medicine.SelectedIndex = previouslySelectedId.HasValue
                ? visibleMedicines.FindIndex(m => m.Id == previouslySelectedId.Value)
                : -1;

            if (visibleMedicines.Count == 0)
                scanFeedback.Text = Tr.Loc.TranslateText("No matching medicine found.");
            else if (!string.IsNullOrWhiteSpace(filter))
                scanFeedback.Text = $"{visibleMedicines.Count} {Tr.Loc.TranslateText("matching medicines")}";
            else
                scanFeedback.Text = "";
        }

        async Task UpdateAvailableStockAsync()
        {
            if (!isRemove) return;
            var selected = visibleMedicines.ElementAtOrDefault(medicine.SelectedIndex);
            if (selected == null)
            {
                availableText.Text = Tr.Loc.TranslateText("Select a medicine to see available stock.");
                return;
            }

            await using var stockDb = new PharmacyDbContext(PharmacyDb.Options());
            var movements = await stockDb.MedicineStockMovements
                .Where(m => m.MedicineId == selected.Id)
                .AsNoTracking()
                .OrderBy(m => m.MovementDate).ThenBy(m => m.Id)
                .ToListAsync();
            var total = CalculateBalances(movements).Values.Where(v => v > 0).Sum();
            availableText.Text = $"{Tr.Loc.TranslateText("Available stock")}: {total:0.##}";
            if (total <= 0)
                availableText.Text += " — " + Tr.Loc.TranslateText("No stock available.");
        }

        medicine.SelectionChanged += async (_, _) => await UpdateAvailableStockAsync();

        barcode.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter) return;
            e.Handled = true;
            var scanned = barcode.Text?.Trim();
            if (string.IsNullOrWhiteSpace(scanned)) return;

            Gs1DataMatrixParser.ParsedPack? parsedPack = null;
            var isGs1 = Gs1DataMatrixParser.TryParse(scanned, out parsedPack, out var parseError);
            string? gtin = isGs1 ? parsedPack?.Gtin14 : null;
            string? eofBarcode = isGs1 ? parsedPack?.ResolvedEofBarcode : null;

            // Also accept ordinary EAN/UPC/GTIN scans and normalize them to GTIN-14.
            if (gtin is null)
            {
                var digits = scanned.StartsWith("]d2", StringComparison.Ordinal) ? scanned[3..] : scanned;
                if (digits.Length is 8 or 12 or 13 or 14 && digits.All(ch => ch is >= '0' and <= '9'))
                    gtin = digits.PadLeft(14, '0');
            }

            int? matchedMedicineId = null;
            if (!string.IsNullOrWhiteSpace(gtin) && barcodeIndex.TryGetValue(gtin, out var gtinId))
                matchedMedicineId = gtinId;
            if (!matchedMedicineId.HasValue && !string.IsNullOrWhiteSpace(eofBarcode)
                && barcodeIndex.TryGetValue(eofBarcode, out var eofId))
                matchedMedicineId = eofId;
            if (!matchedMedicineId.HasValue && barcodeIndex.TryGetValue(scanned, out var exactId))
                matchedMedicineId = exactId;

            if (matchedMedicineId.HasValue)
            {
                medicineSearch.Text = "";
                searchVersion++;
                visibleMedicines = allMedicines;
                medicine.ItemsSource = visibleMedicines.Select(m => m.Name).ToList();
                medicine.SelectedIndex = visibleMedicines.FindIndex(m => m.Id == matchedMedicineId.Value);

                if (parsedPack is not null)
                {
                    // Reuse the GS1 batch details so incoming stock is recorded against
                    // the correct expiry/lot; removal can optionally target that same batch.
                    expiry.SelectedDate = parsedPack.ExpiryDate;
                    batch.Text = parsedPack.BatchLot;
                    scanFeedback.Text = $"{Tr.Loc.TranslateText("GS1 QR decoded")}: {parsedPack.Gtin14} | {parsedPack.BatchLot} | {parsedPack.ExpiryDate:yyyy-MM-dd} — {Tr.Loc.TranslateText("Medicine matched")}: {allMedicines.First(m => m.Id == matchedMedicineId.Value).Name}";
                }
                else
                {
                    scanFeedback.Text = Tr.Loc.TranslateText("Barcode matched") + ": " + allMedicines.First(m => m.Id == matchedMedicineId.Value).Name;
                }
            }
            else
            {
                if (!string.IsNullOrWhiteSpace(gtin))
                    medicineSearch.Text = gtin;
                else
                    medicineSearch.Text = scanned;
                scanFeedback.Text = isGs1
                    ? $"{Tr.Loc.TranslateText("QR decoded, but no medicine matches GTIN")}: {gtin}"
                    : $"{Tr.Loc.TranslateText("Barcode / QR not recognized")}: {parseError}";
            }
        };

        var fields = new StackPanel { Spacing = 9, Margin = new Thickness(16) };
        fields.Children.Add(new TextBlock { Text = Tr.Loc.TranslateText("Scan or select a medicine"), FontSize = 17, FontWeight = FontWeight.Bold });
        AddField(fields, "Barcode scan", barcode);
        AddField(fields, "Search medicine", medicineSearch);
        AddField(fields, "Medicine", medicine);
        fields.Children.Add(scanFeedback);
        if (isRemove)
            fields.Children.Add(new Border
            {
                Padding = new Thickness(10),
                CornerRadius = new CornerRadius(6),
                Background = new SolidColorBrush(Color.Parse("#203B4B")),
                Child = availableText
            });

        AddField(fields, isCount ? "Counted quantity" : "Quantity", quantity);

        var advancedFields = new StackPanel { Spacing = 8, Margin = new Thickness(4, 8) };
        AddField(advancedFields, isRemove ? "Expiry filter (optional)" : "Expiry date (optional)", expiry);
        AddField(advancedFields, "Batch / Lot (optional)", batch);
        AddField(advancedFields, "Notes (optional)", notes);
        fields.Children.Add(new Expander
        {
            Header = Tr.Loc.TranslateText("Optional details"),
            IsExpanded = false,
            Content = advancedFields
        });

        fields.Children.Add(new TextBlock
        {
            Text = Tr.Loc.TranslateText(isCount
                ? "A count without expiry and batch resets total medicine stock. With expiry or batch details, the count replaces stock for that matching group."
                : isRemove
                    ? "Stock is removed from the earliest-expiring batches first. Use optional details only to restrict removal to a particular expiry or batch."
                    : "Expiry and batch details help track stock by batch."),
            TextWrapping = TextWrapping.Wrap,
            Foreground = Brushes.LightGray,
            FontSize = 12
        });

        var save = new Button
        {
            Content = Tr.Loc.TranslateText(isRemove ? "Confirm removal" : isCount ? "Save count" : "Save stock"),
            MinWidth = 130,
            MinHeight = 38,
            Classes = { "accent" }
        };
        var cancel = new Button { Content = Tr.Loc.TranslateText("Cancel"), MinWidth = 100, MinHeight = 38 };
        var footer = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Right,
            Children = { cancel, save }
        };
        var root = new Grid
        {
            RowDefinitions = new RowDefinitions("*,Auto"),
            Children = { new ScrollViewer { Content = fields }, footer },
            Margin = new Thickness(10)
        };
        Grid.SetRow(footer, 1);
        dialog.Content = root;
        dialog.Opened += (_, _) => barcode.Focus();
        cancel.Click += (_, _) => dialog.Close();

        save.Click += async (_, _) =>
        {
            var selected = visibleMedicines.ElementAtOrDefault(medicine.SelectedIndex);
            if (selected == null)
            {
                scanFeedback.Text = Tr.Loc.TranslateText("Select a medicine before saving.");
                return;
            }

            if (!decimal.TryParse(quantity.Text, System.Globalization.NumberStyles.Number,
                    System.Globalization.CultureInfo.CurrentCulture, out var qty)
                || qty < 0 || (!isCount && qty <= 0))
            {
                scanFeedback.Text = Tr.Loc.TranslateText("Enter a valid quantity.");
                return;
            }

            await using var db = new PharmacyDbContext(PharmacyDb.Options());
            var expiryDate = expiry.SelectedDate?.Date;
            var lotValue = string.IsNullOrWhiteSpace(batch.Text) ? null : batch.Text.Trim();
            var movedAt = DateTime.Now;
            var actor = currentUser;
            var noteValue = string.IsNullOrWhiteSpace(notes.Text) ? null : notes.Text.Trim();
            var scannedValue = string.IsNullOrWhiteSpace(barcode.Text) ? selected.Barcode : barcode.Text.Trim();

            if (isRemove)
            {
                var allMovements = await db.MedicineStockMovements
                    .Where(m => m.MedicineId == selected.Id)
                    .OrderBy(m => m.MovementDate).ThenBy(m => m.Id)
                    .ToListAsync();
                var balances = CalculateBalances(allMovements);
                var buckets = balances
                    .Where(b => b.Value > 0
                        && (!expiryDate.HasValue || b.Key.Expiry == expiryDate)
                        && (string.IsNullOrWhiteSpace(lotValue) || b.Key.Lot.Equals(lotValue, StringComparison.OrdinalIgnoreCase)))
                    .Select(b => new { b.Key.Expiry, b.Key.Lot, Balance = b.Value })
                    .OrderBy(b => b.Expiry ?? DateTime.MaxValue)
                    .ThenBy(b => b.Lot)
                    .ToList();
                var available = buckets.Sum(b => b.Balance);
                if (qty > available)
                {
                    scanFeedback.Text = $"{Tr.Loc.TranslateText("Insufficient stock")}. {Tr.Loc.TranslateText("Available")}: {available:0.##}";
                    return;
                }

                var remaining = qty;
                foreach (var bucket in buckets)
                {
                    if (remaining <= 0) break;
                    var take = Math.Min(remaining, bucket.Balance);
                    db.MedicineStockMovements.Add(new MedicineStockMovement
                    {
                        MedicineId = selected.Id,
                        Type = type,
                        Quantity = take,
                        MovementDate = movedAt,
                        ExpiryDate = bucket.Expiry,
                        BatchLot = string.IsNullOrEmpty(bucket.Lot) ? null : bucket.Lot,
                        BarcodeScanned = scannedValue,
                        PerformedBy = actor,
                        Notes = noteValue
                    });
                    remaining -= take;
                }
            }
            else
            {
                db.MedicineStockMovements.Add(new MedicineStockMovement
                {
                    MedicineId = selected.Id,
                    Type = type,
                    Quantity = isCount ? 0 : qty,
                    CountedQuantity = isCount ? qty : null,
                    MovementDate = movedAt,
                    ExpiryDate = expiryDate,
                    BarcodeScanned = scannedValue,
                    BatchLot = lotValue,
                    PerformedBy = actor,
                    Notes = noteValue
                });
            }

            try
            {
                await db.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                scanFeedback.Text = Tr.Loc.TranslateText("Stock save failed") + ": " + ex.Message;
                return;
            }

            dialog.Close();
            status.Text = Tr.Loc.TranslateText("Stock movement saved.");
            await LoadAsync();
        };

        await dialog.ShowDialog(this);
    }

    private static Dictionary<(DateTime? Expiry, string Lot), decimal> CalculateBalances(IEnumerable<MedicineStockMovement> movements)
    {
        var balances = new Dictionary<(DateTime? Expiry, string Lot), decimal>();
        foreach (var movement in movements.OrderBy(m => m.MovementDate).ThenBy(m => m.Id))
        {
            var key = (movement.ExpiryDate?.Date, movement.BatchLot ?? "");
            if (movement.Type == MedicineStockMovementType.Count)
            {
                // A count with no expiry and no lot is a full medicine count and resets all batches.
                if (!movement.ExpiryDate.HasValue && string.IsNullOrWhiteSpace(movement.BatchLot)) balances.Clear();
                else if (movement.ExpiryDate.HasValue && string.IsNullOrWhiteSpace(movement.BatchLot))
                    foreach (var oldKey in balances.Keys.Where(k => k.Expiry == movement.ExpiryDate.Value.Date).ToList()) balances.Remove(oldKey);
                else if (!movement.ExpiryDate.HasValue && !string.IsNullOrWhiteSpace(movement.BatchLot))
                    foreach (var oldKey in balances.Keys.Where(k => k.Lot.Equals(movement.BatchLot, StringComparison.OrdinalIgnoreCase)).ToList()) balances.Remove(oldKey);
                balances[key] = movement.CountedQuantity ?? 0;
            }
            else if (movement.Type == MedicineStockMovementType.Add) balances[key] = balances.GetValueOrDefault(key) + movement.Quantity;
            else balances[key] = balances.GetValueOrDefault(key) - movement.Quantity;
        }
        return balances;
    }
    private static void AddField(Panel panel, string label, Control control)
    {
        panel.Children.Add(new TextBlock { Text = Tr.Loc.TranslateText(label), FontWeight = FontWeight.SemiBold });
        panel.Children.Add(control);
    }

    private sealed record StockRow(string Name, string Barcode, DateTime? Expiry, string Lot, decimal Quantity, DateTime LastMovement, string LastUser, int MedicineId);
}
