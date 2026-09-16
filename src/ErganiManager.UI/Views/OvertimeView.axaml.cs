using Avalonia.Controls;
using Avalonia.Interactivity;
using ErganiManager.Core.Interfaces;
using ErganiManager.UI.ViewModels;

namespace ErganiManager.UI.Views;

public partial class OvertimeView : UserControl
{
    public OvertimeView()
    {
        InitializeComponent();
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: OvertimeDto record } && DataContext is OvertimeViewModel vm)
            vm.CancelOvertimeCommand.Execute(record);
    }

    private void OnDeleteClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: OvertimeDto record } && DataContext is OvertimeViewModel vm)
            vm.DeleteCommand.Execute(record);
    }
}
