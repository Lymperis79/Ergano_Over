using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Microsoft.EntityFrameworkCore;
using SPharmacyLog.Data;
using SPharmacyLog.UI.Localization;
using SPharmacyLog.UI.Services;

namespace SPharmacyLog.UI.Views;

public sealed class MedicineCatalogWindow : UserControl
{
    private readonly TextBox searchBox;
    private readonly StackPanel rowsPanel;
    private readonly TextBlock statusText;
    private readonly Button updateButton;

    public MedicineCatalogWindow()
    {
        var title = new TextBlock
        {
            Text = Tr.Loc.TranslateText("Medicine catalogue"),
            FontSize = 23,
            FontWeight = FontWeight.Bold
        };
        var source = new TextBlock
        {
            Text = Tr.Loc.TranslateText("Source: drugbase-gr, compiled from EOF and EMA public filings. Licence: CC BY 4.0."),
            TextWrapping = TextWrapping.Wrap,
            Foreground = Brushes.LightGray
        };

        searchBox = new TextBox
        {
            MinWidth = 260,
            MinHeight = 36,
            PlaceholderText = Tr.Loc.TranslateText("Search by medicine name or barcode")
        };
        var searchButton = new Button { Content = Tr.Loc.TranslateText("Search"), MinWidth = 90 };
        searchButton.Click += async (_, _) => await RefreshRowsAsync();
        searchBox.KeyDown += async (_, e) =>
        {
            if (e.Key == Avalonia.Input.Key.Enter)
            {
                e.Handled = true;
                await RefreshRowsAsync();
            }
        };

        updateButton = new Button
        {
            Content = Tr.Loc.TranslateText("Update catalogue from drugbase-gr"),
            Classes = { "accent" },
            MinHeight = 36
        };
        updateButton.Click += async (_, _) => await UpdateCatalogueAsync();
        var refreshButton = new Button { Content = Tr.Loc.TranslateText("Refresh"), MinHeight = 36 };
        refreshButton.Click += async (_, _) => await RefreshRowsAsync();

        statusText = new TextBlock
        {
            Text = Tr.Loc.TranslateText("Loading catalogue…"),
            TextWrapping = TextWrapping.Wrap,
            Foreground = Brushes.LightGray
        };
        rowsPanel = new StackPanel { Spacing = 0 };

        var header = new Grid
        {
            ColumnDefinitions = CreateTableColumns(),
            Background = new SolidColorBrush(Color.Parse("#263447")),
            Margin = new Avalonia.Thickness(0, 8, 0, 0),
            Children =
            {
                new TextBlock { Text = Tr.Loc.TranslateText("Medicine"), FontWeight = FontWeight.Bold, Margin = new Avalonia.Thickness(10, 9) },
                new TextBlock { Text = Tr.Loc.TranslateText("Barcode(s)"), FontWeight = FontWeight.Bold, Margin = new Avalonia.Thickness(10, 9) }
            }
        };
        Grid.SetColumn(header.Children[1], 1);

        // A StackPanel measures children with effectively unlimited height, which
        // prevents a nested ScrollViewer from receiving a bounded viewport.
        // Use a Grid with a star-sized final row so both scrollbars can work.
        rowsPanel.MinWidth = 850;
        var body = new Grid
        {
            Margin = new Avalonia.Thickness(18),
            RowDefinitions = new RowDefinitions
            {
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Star)
            },
            RowSpacing = 10,
            Children =
            {
                title,
                source,
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { searchBox, searchButton, updateButton, refreshButton } },
                statusText,
                header,
                new ScrollViewer
                {
                    Content = rowsPanel,
                    HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Visible,
                    VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Visible
                }
            }
        };
        for (var rowIndex = 0; rowIndex < body.Children.Count; rowIndex++)
            Grid.SetRow(body.Children[rowIndex], rowIndex);
        Content = body;

        _ = RefreshRowsAsync();
    }

    private static ColumnDefinitions CreateTableColumns()
    {
        var columns = new ColumnDefinitions();
        columns.Add(new ColumnDefinition(new GridLength(2, GridUnitType.Star)));
        columns.Add(new ColumnDefinition(new GridLength(2, GridUnitType.Star)));
        return columns;
    }

    private async Task RefreshRowsAsync()
    {
        try
        {
            await using var db = new PharmacyDbContext(PharmacyDb.Options());
            var term = searchBox.Text?.Trim();
            var query = db.Medicines.AsNoTracking().Include(item => item.Barcodes).AsQueryable();
            if (!string.IsNullOrWhiteSpace(term))
            {
                query = query.Where(item =>
                    item.Name.Contains(term) ||
                    (item.Barcode != null && item.Barcode.Contains(term)) ||
                    item.Barcodes.Any(barcode => barcode.BarcodeValue.Contains(term)));
            }

            var total = await db.Medicines.CountAsync();
            var matches = await query.OrderBy(item => item.Name).Take(1000).ToListAsync();
            rowsPanel.Children.Clear();
            foreach (var medicine in matches)
            {
                var barcodeText = string.Join(", ", medicine.Barcodes
                    .Select(item => item.BarcodeValue)
                    .Distinct()
                    .OrderBy(value => value)
                    .Take(5));
                if (string.IsNullOrWhiteSpace(barcodeText))
                    barcodeText = medicine.Barcode ?? string.Empty;

                var row = new Grid
                {
                    ColumnDefinitions = new ColumnDefinitions("2*,2*"),
                    Children =
                    {
                        new TextBlock { Text = medicine.Name, Margin = new Avalonia.Thickness(10, 8), TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center },
                        new TextBlock { Text = barcodeText, Margin = new Avalonia.Thickness(10, 8), TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center, FontFamily = new FontFamily("Consolas") }
                    }
                };
                Grid.SetColumn(row.Children[1], 1);
                rowsPanel.Children.Add(new Border
                {
                    BorderBrush = new SolidColorBrush(Color.Parse("#354152")),
                    BorderThickness = new Avalonia.Thickness(0, 0, 0, 1),
                    Child = row
                });
            }

            statusText.Text = string.IsNullOrWhiteSpace(term)
                ? $"{Tr.Loc.TranslateText("Total medicines")}: {total:N0}"
                : $"{matches.Count:N0} {Tr.Loc.TranslateText("matching medicines")} (max. 1,000 shown)";
        }
        catch (Exception ex)
        {
            statusText.Text = $"{Tr.Loc.TranslateText("Could not load the medicine catalogue")}: {ex.Message}";
        }
    }

    private async Task UpdateCatalogueAsync()
    {
        updateButton.IsEnabled = false;
        try
        {
            statusText.Text = Tr.Loc.TranslateText("Downloading and importing the public medicine registry…");
            var result = await MedicineCatalogImportService.UpdateFromPublicRegistryAsync(message => statusText.Text = message);
            statusText.Text = $"{Tr.Loc.TranslateText("Catalogue update completed")}: {result.RowsRead:N0} rows; {result.MedicinesAdded:N0} new medicines; {result.BarcodesAdded:N0} barcode mappings; {result.RowsWithoutBarcode:N0} rows without a usable barcode.";
            await RefreshRowsAsync();
        }
        catch (Exception ex)
        {
            statusText.Text = $"{Tr.Loc.TranslateText("Catalogue update failed")}: {ex.Message}";
        }
        finally
        {
            updateButton.IsEnabled = true;
        }
    }
}
