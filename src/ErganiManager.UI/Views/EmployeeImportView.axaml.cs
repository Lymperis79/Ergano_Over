using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using ErganiManager.UI.ViewModels;

namespace ErganiManager.UI.Views;

public partial class EmployeeImportView : Window
{
    public EmployeeImportView()
    {
        InitializeComponent();
    }

    private async void OnBrowseClick(object? sender, RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null) return;

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select Employee Import File",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("Excel Workbook") { Patterns = new[] { "*.xlsx" } }
            }
        });

        if (files.Count == 0)
            return;

        var path = files[0].TryGetLocalPath();
        if (path != null && DataContext is EmployeeImportViewModel vm)
            vm.LoadFile(path);
    }
}
