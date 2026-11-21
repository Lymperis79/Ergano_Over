using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace SPharmacyLog.UI.Views;

public sealed class BarcodeDisplayWindow : Window
{
    private readonly TextBlock medicineNameText;
    private readonly Image barcodeImage;

    public BarcodeDisplayWindow(string medicineName, WriteableBitmap barcodeBitmap)
    {
        Title = "Medicine barcode";
        Width = 360;
        Height = 390;
        MinWidth = 320;
        MinHeight = 350;
        CanResize = false;
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
            Margin = new Thickness(8, 4, 8, 12)
        };

        barcodeImage = new Image
        {
            Source = barcodeBitmap,
            Width = 300,
            Height = 300,
            Stretch = Stretch.Uniform,
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
                        Text = "Scan this barcode with your scanner",
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

    public void UpdateBarcode(string medicineName, WriteableBitmap barcodeBitmap)
    {
        medicineNameText.Text = medicineName;
        barcodeImage.Source = barcodeBitmap;
        Topmost = true;
    }
}
