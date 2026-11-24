using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using SPharmacyLog.UI.Services;

namespace SPharmacyLog.UI.Views;

public sealed class CompanySettingsWindow : UserControl
{
    public event Action? SettingsSaved;
    private readonly TextBox companyName;
    private readonly Image logoPreview;
    private byte[]? selectedLogo;
    private string? selectedExtension;
    private bool removeLogo;

    public CompanySettingsWindow()
    {
        var settings = CompanySettingsService.Load();
        Background = new SolidColorBrush(Color.Parse("#202733"));

        companyName = new TextBox
        {
            Text = settings.Name,
            PlaceholderText = "Enter company name",
            MinHeight = 38
        };

        logoPreview = new Image
        {
            Width = 92,
            Height = 92,
            Stretch = Stretch.Uniform,
            IsVisible = false
        };
        if (!string.IsNullOrWhiteSpace(settings.LogoFile) && File.Exists(settings.LogoFile))
        {
            try
            {
                logoPreview.Source = new Bitmap(settings.LogoFile);
                logoPreview.IsVisible = true;
            }
            catch { }
        }

        var browse = new Button { Content = "Choose logo…", MinWidth = 120 };
        browse.Click += async (_, _) => await ChooseLogoAsync();
        var clear = new Button { Content = "Remove logo", MinWidth = 110 };
        clear.Click += (_, _) =>
        {
            selectedLogo = null;
            selectedExtension = null;
            removeLogo = true;
            logoPreview.Source = null;
            logoPreview.IsVisible = false;
        };

        var save = new Button { Content = "Save", Classes = { "accent" }, MinWidth = 100 };
        save.Click += (_, _) =>
        {
            CompanySettingsService.Save(companyName.Text ?? "", selectedLogo, selectedExtension, removeLogo);
            SettingsSaved?.Invoke();
        };
        cancel.Click += (_, _) => Close(false);

        var logoActions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Children = { browse, clear }
        };
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
            Children = { save }
        };
        Content = new StackPanel
        {
            Margin = new Thickness(22),
            Spacing = 12,
            Children =
            {
                new TextBlock { Text = "Company identity", FontSize = 21, FontWeight = FontWeight.SemiBold },
                new TextBlock { Text = "Company name" },
                companyName,
                new TextBlock { Text = "Company logo (optional)", FontWeight = FontWeight.SemiBold },
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 14, Children = { logoPreview, new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = 8, Children = { logoActions, new TextBlock { Text = "PNG, JPG or BMP image", Foreground = Brushes.LightGray, FontSize = 12 } } } } },
                buttons
            }
        };
    }

    private async Task ChooseLogoAsync()
    {
        var storage = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (storage is null) return;
        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Choose company logo",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("Image files") { Patterns = new[] { "*.png", "*.jpg", "*.jpeg", "*.bmp" } }
            }
        });
        var file = files.FirstOrDefault();
        if (file is null) return;

        await using var stream = await file.OpenReadAsync();
        using var memory = new MemoryStream();
        await stream.CopyToAsync(memory);
        selectedLogo = memory.ToArray();
        selectedExtension = Path.GetExtension(file.Name);
        using var previewStream = new MemoryStream(selectedLogo);
        logoPreview.Source = new Bitmap(previewStream);
        logoPreview.IsVisible = true;
        removeLogo = false;
    }
}
