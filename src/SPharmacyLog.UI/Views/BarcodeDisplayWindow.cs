using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using SPharmacyLog.UI.Localization;

namespace SPharmacyLog.UI.Views;

public sealed class BarcodeDisplayWindow : Window
{
    private readonly TextBlock medicineNameText;
    private readonly Image barcodeImage;

    public BarcodeDisplayWindow(string medicineName, WriteableBitmap barcodeBitmap)
    {
        Title = Tr.Loc.TranslateText("Medicine barcode");
        Tr.Loc.LanguageChanged += OnLanguageChanged;
        Closed += (_, _) => Tr.Loc.LanguageChanged -= OnLanguageChanged;
        Width = 340;
        Height = 380;
        MinWidth = 260;
        MinHeight = 300;
        MaxWidth = 600;
        MaxHeight = 700;
        CanResize = true;
        ShowInTaskbar = false;
        Topmost = true;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        medicineNameText = new TextBlock
        {
            Text = medicineName,
            FontSize = 18,
            FontWeight = FontWeight.Bold,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Margin = new Thickness(8, 4, 8, 8)
        };

        barcodeImage = new Image
        {
            Source = barcodeBitmap,
            Stretch = Stretch.Uniform,
            MaxWidth = 320,
            MaxHeight = 320,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };

        Content = new Border
        {
            Padding = new Thickness(16),
            Background = new SolidColorBrush(Color.Parse("#172536")),
            Child = new StackPanel
            {
                Spacing = 8,
                VerticalAlignment = VerticalAlignment.Center,
                Children =
                {
                    medicineNameText,
                    barcodeImage,
                    new TextBlock
                    {
                        Text = Tr.Loc.TranslateText("Scan this barcode with your scanner"),
                        FontSize = 12,
                        Foreground = Brushes.LightGray,
                        TextAlignment = TextAlignment.Center,
                        HorizontalAlignment = HorizontalAlignment.Stretch,
                        Margin = new Thickness(4, 8, 4, 0)
                    }
                }
            }
        };
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        Title = Tr.Loc.TranslateText("Medicine barcode");
        if (Content is Border border && border.Child is StackPanel panel && panel.Children.Count > 2 && panel.Children[2] is TextBlock instruction)
            instruction.Text = Tr.Loc.TranslateText("Scan this barcode with your scanner");
    }

    public void UpdateBarcode(string medicineName, WriteableBitmap barcodeBitmap)
    {
        medicineNameText.Text = medicineName;
        barcodeImage.Source = barcodeBitmap;
        Topmost = true;
    }
}
