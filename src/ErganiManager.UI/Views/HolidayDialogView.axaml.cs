using Avalonia.Controls;
using Avalonia.Interactivity;
using ErganiManager.UI.ViewModels;

namespace ErganiManager.UI.Views;

public partial class HolidayDialogView : UserControl
{
    public HolidayDialogView()
    {
        InitializeComponent();
    }

    private void OnResponseClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: HolidayRow row } && DataContext is HolidayDialogViewModel vm)
            vm.ShowResponseCommand.Execute(row);
    }

    private void OnPdfClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: HolidayRow row } && DataContext is HolidayDialogViewModel vm)
            _ = vm.DownloadPdfCommand.ExecuteAsync(row);
    }

    private void OnDeleteClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: HolidayRow row } && DataContext is HolidayDialogViewModel vm)
            _ = vm.DeleteRowCommand.ExecuteAsync(row);
    }
}
