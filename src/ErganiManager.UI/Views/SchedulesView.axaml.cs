using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using ErganiManager.UI.ViewModels;

namespace ErganiManager.UI.Views;

public partial class SchedulesView : UserControl
{
    public SchedulesView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (DataContext is SchedulesViewModel vm)
                vm.ImportRequested += OnImportRequested;
        };
    }

    private void OnCellPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Border { Tag: CalendarCellViewModel cell }
            && DataContext is SchedulesViewModel vm)
        {
            bool ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control)
                     || e.KeyModifiers.HasFlag(KeyModifiers.Meta);
            vm.OnCellClick(cell, ctrl);
        }
    }

    private async void OnImportRequested(object? sender, System.EventArgs e)
    {
        if (DataContext is not SchedulesViewModel vm) return;

        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null) return;

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(
            new FilePickerOpenOptions
            {
                Title             = "Import Schedule from Excel",
                AllowMultiple     = false,
                FileTypeFilter    = new[]
                {
                    new FilePickerFileType("Excel Files") { Patterns = new[] { "*.xlsx" } }
                }
            });

        var file = files.FirstOrDefault();
        if (file == null) return;

        var path = file.TryGetLocalPath();
        if (path != null)
            await vm.ImportFromFileAsync(path);
    }
}
