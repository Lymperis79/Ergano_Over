using Avalonia.Controls;
using Avalonia.Interactivity;
using ErganiManager.Core.Interfaces;
using ErganiManager.UI.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace ErganiManager.UI.Views;

public partial class EmployeesView : UserControl
{
    public EmployeesView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, System.EventArgs e)
    {
        if (DataContext is EmployeesViewModel vm)
            vm.ImportRequested += OnImportRequested;
    }

    private async void OnImportRequested(object? sender, System.EventArgs e)
    {
        if (DataContext is not EmployeesViewModel vm)
            return;

        var session = Program.Services.GetRequiredService<Core.Interfaces.ICompanyContext>();
        if (session.ActiveCompanyId is not int companyId)
            return;

        var employeeService = Program.Services.GetRequiredService<IEmployeeService>();
        var branchService = Program.Services.GetRequiredService<IBranchService>();
        var importVm = new EmployeeImportViewModel(employeeService, branchService, companyId);

        importVm.ImportCompleted += async (_, _) => await vm.LoadCommand.ExecuteAsync(null);

        var importWindow = new EmployeeImportView { DataContext = importVm };
        var parentWindow = TopLevel.GetTopLevel(this) as Window;
        await importWindow.ShowDialog(parentWindow ?? importWindow);
    }

    private void OnEditClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: EmployeeDto employee } && DataContext is EmployeesViewModel vm)
            vm.StartEditCommand.Execute(employee);
    }

    private void OnToggleActiveClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: EmployeeDto employee } && DataContext is EmployeesViewModel vm)
            vm.ToggleActiveCommand.Execute(employee);
    }

    private void OnDeleteClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: EmployeeDto item } && DataContext is EmployeesViewModel vm)
            vm.DeleteCommand.Execute(item);
    }
}