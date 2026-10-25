using Avalonia.Controls;
using Avalonia.Interactivity;
using ErganiManager.Core.Interfaces;
using ErganiManager.UI.ViewModels;

namespace ErganiManager.UI.Views;

public partial class WorkCardHistoryView : UserControl
{
    public WorkCardHistoryView()
    {
        InitializeComponent();
    }
    private void OnResponseClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: WorkCardHistoryDto record } && DataContext is WorkCardHistoryViewModel vm)
            vm.ShowResponseForCommand.Execute(record);
    }

    private void OnPdfClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: WorkCardHistoryDto record } && DataContext is WorkCardHistoryViewModel vm)
            _ = vm.DownloadPdfCommand.ExecuteAsync(record);
    }
}
