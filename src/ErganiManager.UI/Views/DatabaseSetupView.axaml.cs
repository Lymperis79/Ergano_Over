using Avalonia.Controls;
using Avalonia.Input;
using ErganiManager.UI.ViewModels;

namespace ErganiManager.UI.Views;

public partial class DatabaseSetupView : Window
{
    public DatabaseSetupView()
    {
        InitializeComponent();
        KeyDown += OnWindowKeyDown;
    }
    private void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Return && e.Key != Key.Enter) return;
        if (DataContext is not DatabaseSetupViewModel vm) return;

        // Step 2 (create the first administrator): Enter presses the button below.
        if (vm.IsDatabaseStepComplete && !vm.IsCreatingAdmin &&
            vm.CreateFirstAdminCommand.CanExecute(null))
        {
            e.Handled = true;
            vm.CreateFirstAdminCommand.Execute(null);
        }
    }
}
