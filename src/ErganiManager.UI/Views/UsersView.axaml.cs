using Avalonia.Controls;
using Avalonia.Interactivity;
using ErganiManager.Core.Interfaces;
using ErganiManager.UI.ViewModels;

namespace ErganiManager.UI.Views;

public partial class UsersView : UserControl
{
    public UsersView()
    {
        InitializeComponent();
    }

    private void OnToggleActiveClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: AppUserDto user } && DataContext is UsersViewModel vm)
            vm.ToggleActiveCommand.Execute(user);
    }

    private void OnResetPasswordClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: AppUserDto user } && DataContext is UsersViewModel vm)
            vm.StartResetPasswordCommand.Execute(user);
    }

    private void OnDeleteClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: AppUserDto item } && DataContext is UsersViewModel vm)
            vm.DeleteCommand.Execute(item);
    }
}