using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using System;
using System.Threading.Tasks;
using ErganiManager.UI.ViewModels;

namespace ErganiManager.UI.Views;

public partial class SubmissionLogView : UserControl
{
    public SubmissionLogView()
    {
        InitializeComponent();
    }

    private void OnRowPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Border { Tag: SubmissionLogRow row } && DataContext is SubmissionLogViewModel vm)
            vm.SelectedRow = row;
    }
    private async void OnDownloadPdfClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is not SubmissionLogViewModel vm || vm.SelectedRow is not { HasPdf: true } row)
            return;

        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel?.StorageProvider is not { } storageProvider)
            return;

        try
        {
            var file = await storageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Save Ergani submission PDF",
                SuggestedFileName = $"Ergani_{row.SubmissionType}_{row.Protocol}.pdf",
                DefaultExtension = "pdf",
                FileTypeChoices = new[]
                {
                    new FilePickerFileType("PDF document") { Patterns = new[] { "*.pdf" } }
                }
            });

            if (file == null) return;
            var path = file.TryGetLocalPath();
            if (string.IsNullOrWhiteSpace(path))
                throw new InvalidOperationException("The selected location is not a local file path.");

            await vm.SaveSelectedPdfAsync(path);
        }
        catch (Exception ex)
        {
            vm.StatusMessage = ex.Message;
        }
    }

}
