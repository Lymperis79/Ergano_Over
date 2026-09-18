using Avalonia.Controls;
using Avalonia.Input;
using ErganiManager.UI.ViewModels;

namespace ErganiManager.UI.Views;

public partial class LoginView : Window
{
    public LoginView()
    {
        InitializeComponent();
        KeyDown += OnWindowKeyDown;
    }

    private void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Return && e.Key != Key.Enter) return;
        if (DataContext is not LoginViewModel vm) return;
        if (vm.LoginCommand.CanExecute(null))
        {
            e.Handled = true;
            vm.LoginCommand.Execute(null);
        }
    }
}
