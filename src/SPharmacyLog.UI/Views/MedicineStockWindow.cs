using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Microsoft.EntityFrameworkCore;
using SPharmacyLog.Data;
using SPharmacyLog.Data.Entities;
using SPharmacyLog.UI.Localization;

namespace SPharmacyLog.UI.Views;

public sealed class MedicineStockWindow : Window
{
    private readonly string currentUser;
    private readonly TextBox search = new() { MinWidth = 260, PlaceholderText = "Medicine / barcode / lot" };
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
            status.Text = $"{Tr.Loc.TranslateText("Stock lines")}: {currentStock.Count}  |  {Tr.Loc.TranslateText("Movement records")}: {movements.Count}";
        }
        catch (Exception ex) { status.Text = Tr.Loc.TranslateText("Could not load stock") + ": " + ex.Message; }
    }

    private async Task OpenMovementFormAsync(MedicineStockMovementType type)
    {
        var formTitle = type switch { MedicineStockMovementType.Count => "Stock count", MedicineStockMovementType.Add => "Add stock", _ => "Remove stock" };
        var barcode = new TextBox { MinWidth = 320, Watermark = Tr.Loc.TranslateText("Scan medicine barcode") };
        var medicineSearch = new TextBox { MinWidth = 320, Watermark = Tr.Loc.TranslateText("Type to filter medicines") };
        var medicine = new ComboBox { MinWidth = 320 };
        var quantity = new TextBox { MinWidth = 320, Text = "1" };
        var expiry = new DatePicker { SelectedDate = null };
        var batch = new TextBox { MinWidth = 320, Watermark = Tr.Loc.TranslateText("Optional batch / lot") };
        var movementDate = new DatePicker { SelectedDate = DateTime.Today };
        var performed = new TextBox { MinWidth = 320, Text = currentUser };
        var notes = new TextBox { MinWidth = 320, AcceptsReturn = true, Height = 70, TextWrapping = TextWrapping.Wrap };
        var dialog = new Window { Title = Tr.Loc.TranslateText(formTitle), Width = 540, Height = 700, MinWidth = 460, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var allMedicines = new List<MedicineCatalogItem>();
        var visibleMedicines = new List<MedicineCatalogItem>();
        await using (var db = new PharmacyDbContext(PharmacyDb.Options()))
        {
            allMedicines = await db.Medicines.AsNoTracking().Where(m => m.IsActive).OrderBy(m => m.Name).ToListAsync();
            var alternates = await db.MedicineBarcodes.AsNoTracking().ToListAsync();
            visibleMedicines = allMedicines.ToList();
            medicine.ItemsSource = visibleMedicines.Select(m => m.Name).ToList();
            medicineSearch.TextChanged += (_, _) =>
            {
                var filter = medicineSearch.Text?.Trim();
                visibleMedicines = string.IsNullOrWhiteSpace(filter) ? allMedicines.ToList() : allMedicines.Where(m => m.Name.Contains(filter, StringComparison.CurrentCultureIgnoreCase) || (m.Barcode?.Contains(filter, StringComparison.OrdinalIgnoreCase) ?? false)).ToList();
                medicine.ItemsSource = visibleMedicines.Select(m => m.Name).ToList();
                medicine.SelectedIndex = -1;
            };
            barcode.KeyDown += (_, e) =>
            {
                if (e.Key != Key.Enter) return;
                e.Handled = true;
                var scanned = barcode.Text?.Trim();
                if (string.IsNullOrWhiteSpace(scanned)) return;
                var match = allMedicines.FirstOrDefault(m => string.Equals(m.Barcode, scanned, StringComparison.OrdinalIgnoreCase))
                    ?? allMedicines.FirstOrDefault(m => alternates.Any(b => b.MedicineId == m.Id && string.Equals(b.BarcodeValue, scanned, StringComparison.OrdinalIgnoreCase)));
                if (match != null) { medicineSearch.Text = ""; medicine.SelectedIndex = visibleMedicines.FindIndex(m => m.Id == match.Id); }
                else status.Text = Tr.Loc.TranslateText("No medicine matches this barcode. Select or create the medicine in the Medicine workspace.");
            };
        }
        var fields = new StackPanel { Spacing = 8, Margin = new Thickness(14) };
        AddField(fields, "Barcode scan", barcode);
        AddField(fields, "Search medicine", medicineSearch);
        AddField(fields, "Medicine", medicine);
        AddField(fields, type == MedicineStockMovementType.Count ? "Counted quantity" : "Quantity", quantity);
        AddField(fields, "Expiry date (optional)", expiry);
        AddField(fields, "Batch / Lot (optional)", batch);
        AddField(fields, "Movement date", movementDate);
        AddField(fields, "Performed by", performed);
        AddField(fields, "Notes (optional)", notes);
        var save = new Button { Content = Tr.Loc.TranslateText("Save"), MinWidth = 100, Classes = { "accent" } };
        var cancel = new Button { Content = Tr.Loc.TranslateText("Cancel"), MinWidth = 100 };
        var footer = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, Children = { save, cancel } };
        var root = new Grid { RowDefinitions = new RowDefinitions("*,Auto"), Children = { new ScrollViewer { Content = fields }, footer }, Margin = new Thickness(10) };
        Grid.SetRow(footer, 1); dialog.Content = root;
        dialog.Opened += (_, _) => barcode.Focus();
        cancel.Click += (_, _) => dialog.Close();
        save.Click += async (_, _) =>
        {
            var selectedName = medicine.SelectedItem?.ToString();
            var selected = visibleMedicines.FirstOrDefault(m => string.Equals(m.Name, selectedName, StringComparison.CurrentCultureIgnoreCase));
            if (selected == null) { status.Text = Tr.Loc.TranslateText("Select a medicine before saving."); return; }
            if (!decimal.TryParse(quantity.Text, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.CurrentCulture, out var qty) || qty < 0 || (type != MedicineStockMovementType.Count && qty <= 0))
            { status.Text = Tr.Loc.TranslateText("Enter a valid quantity."); return; }
            if (type == MedicineStockMovementType.Remove && qty <= 0) { status.Text = Tr.Loc.TranslateText("Removal quantity must be greater than zero."); return; }
            if (movementDate.SelectedDate == null) { status.Text = Tr.Loc.TranslateText("Select a movement date."); return; }
            await using var db = new PharmacyDbContext(PharmacyDb.Options());
            var expiryDate = expiry.SelectedDate?.Date;
            var lotValue = string.IsNullOrWhiteSpace(batch.Text) ? null : batch.Text.Trim();
            var movedAt = movementDate.SelectedDate.Value.Date + DateTime.Now.TimeOfDay;
            var actor = string.IsNullOrWhiteSpace(performed.Text) ? currentUser : performed.Text.Trim();
            var noteValue = string.IsNullOrWhiteSpace(notes.Text) ? null : notes.Text.Trim();
            var scannedValue = string.IsNullOrWhiteSpace(barcode.Text) ? selected.Barcode : barcode.Text.Trim();
            if (type == MedicineStockMovementType.Remove)
            {
                var allMovements = await db.MedicineStockMovements.Where(m => m.MedicineId == selected.Id).OrderBy(m => m.MovementDate).ThenBy(m => m.Id).ToListAsync();
                var balances = CalculateBalances(allMovements);
                var buckets = balances.Where(b => b.Value > 0 && (!expiryDate.HasValue || b.Key.Expiry == expiryDate) && (string.IsNullOrWhiteSpace(lotValue) || b.Key.Lot.Equals(lotValue, StringComparison.OrdinalIgnoreCase)))
                    .Select(b => new { b.Key.Expiry, b.Key.Lot, Balance = b.Value })
                    .OrderBy(b => b.Expiry ?? DateTime.MaxValue).ThenBy(b => b.Lot).ToList();
                var available = buckets.Sum(b => b.Balance);
                if (qty > available) { status.Text = Tr.Loc.TranslateText("Insufficient stock") + ". " + Tr.Loc.TranslateText("Available") + ": " + available.ToString("0.##"); return; }
                var remaining = qty;
                foreach (var bucket in buckets)
                {
                    if (remaining <= 0) break;
                    var take = Math.Min(remaining, bucket.Balance);
                    db.MedicineStockMovements.Add(new MedicineStockMovement { MedicineId = selected.Id, Type = type, Quantity = take, MovementDate = movedAt, ExpiryDate = bucket.Expiry, BatchLot = string.IsNullOrEmpty(bucket.Lot) ? null : bucket.Lot, BarcodeScanned = scannedValue, PerformedBy = actor, Notes = noteValue });
                    remaining -= take;
                }
            }
            else
            {
                var item = new MedicineStockMovement { MedicineId = selected.Id, Type = type, Quantity = type == MedicineStockMovementType.Count ? 0 : qty, CountedQuantity = type == MedicineStockMovementType.Count ? qty : null, MovementDate = movedAt, ExpiryDate = expiryDate, BarcodeScanned = scannedValue, BatchLot = lotValue, PerformedBy = actor, Notes = noteValue };
                db.MedicineStockMovements.Add(item);
            }
            await db.SaveChangesAsync();
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
