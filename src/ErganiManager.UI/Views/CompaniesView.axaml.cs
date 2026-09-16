using Avalonia.Controls;
using Avalonia.Interactivity;
using ErganiManager.Core.Interfaces;
using ErganiManager.UI.ViewModels;

namespace ErganiManager.UI.Views;

public partial class CompaniesView : UserControl
{
    public CompaniesView()
    {
        InitializeComponent();
    }

    private void OnEditClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: CompanyDto company } && DataContext is CompaniesViewModel vm)
            vm.StartEditCommand.Execute(company);
    }

    private void OnToggleActiveClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: CompanyDto company } && DataContext is CompaniesViewModel vm)
            vm.ToggleActiveCommand.Execute(company);
    }

    private void OnDeleteClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: CompanyDto item } && DataContext is CompaniesViewModel vm)
            vm.DeleteCommand.Execute(item);
    }
}