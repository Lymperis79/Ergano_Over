using Avalonia.Controls;
using Avalonia.Interactivity;
using ErganiManager.Core.Interfaces;
using ErganiManager.UI.ViewModels;

namespace ErganiManager.UI.Views;

public partial class BranchesView : UserControl
{
    public BranchesView()
    {
        InitializeComponent();
    }

    private void OnEditClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: BranchDto branch } && DataContext is BranchesViewModel vm)
            vm.StartEditCommand.Execute(branch);
    }

    private void OnToggleActiveClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: BranchDto branch } && DataContext is BranchesViewModel vm)
            vm.ToggleActiveCommand.Execute(branch);
    }

    private void OnDeleteClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: BranchDto item } && DataContext is BranchesViewModel vm)
            vm.DeleteCommand.Execute(item);
    }
}