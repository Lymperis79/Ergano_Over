using System.IO;
using System.Text;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using ErganiManager.UI.ViewModels;

namespace ErganiManager.UI.Views;

public partial class ReportsView : UserControl
{
    public ReportsView() => InitializeComponent();

    private async void OnExportClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not ReportsViewModel vm) return;
        var top = TopLevel.GetTopLevel(this);
        if (top == null) return;

        var file = await top.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = Tr.Loc["Vm.Export report"],
            SuggestedFileName = vm.SuggestedFileName,
            DefaultExtension = "csv",
            FileTypeChoices = new[] { new FilePickerFileType("CSV") { Patterns = new[] { "*.csv" } } }
        });

        var path = file?.TryGetLocalPath();
        if (string.IsNullOrEmpty(path)) return;

        // UTF-8 with BOM so Excel shows Greek names correctly
        await File.WriteAllTextAsync(path, vm.BuildCsv(), new UTF8Encoding(true));
    }

    private async void OnExportExcelClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not ReportsViewModel vm) return;
        var top = TopLevel.GetTopLevel(this);
        if (top == null) return;

        var file = await top.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = Tr.Loc["Vm.Export report"],
            SuggestedFileName = vm.ExcelSuggestedFileName,
            DefaultExtension = "xlsx",
            FileTypeChoices = new[] { new FilePickerFileType("Excel") { Patterns = new[] { "*.xlsx" } } }
        });

        var path = file?.TryGetLocalPath();
        if (string.IsNullOrEmpty(path)) return;

        await vm.ExportTimeWorkbookAsync(path);
    }
}
