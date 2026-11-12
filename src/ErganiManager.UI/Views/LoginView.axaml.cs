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
        Opened += (_, _) =>
        {
            if (DataContext is LoginViewModel vm && vm.AutoLoginOnOpen && vm.AutoLoginEnabled && vm.RememberCredentials)
            {
                // Start only after the login window is open and bindings are ready.
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    if (vm.LoginCommand.CanExecute(null))
                        vm.LoginCommand.Execute(null);
                });
            }
            else
            {
                Avalonia.Threading.Dispatcher.UIThread.Post(() => UsernameBox.Focus());
            }
        };
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
