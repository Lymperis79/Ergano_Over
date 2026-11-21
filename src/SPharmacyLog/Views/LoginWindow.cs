using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using SPharmacyLog.Models;
using SPharmacyLog.Services;

namespace SPharmacyLog.Views;

public sealed class LoginWindow : Window
{
    private readonly PharmacyAuthService _auth = new();
    private readonly TextBox _user = new() { Watermark = "Username", Width = 300 };
    private readonly TextBox _pass = new() { Watermark = "Password (min. 8 characters)", PasswordChar = '•', Width = 300 };
    private readonly TextBlock _message = new() { TextWrapping = TextWrapping.Wrap, Foreground = Brushes.OrangeRed };
    private readonly Button _action = new() { Content = "Sign in", Width = 300 };
    private bool _firstAdmin;

    public LoginWindow()
    {
        Title = "S Pharmacy Log";
        Width = 440; Height = 500; CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Content = new StackPanel { Spacing = 12, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center,
            Children = { new TextBlock { Text = "S Pharmacy Log", FontSize = 28, FontWeight = FontWeight.Bold, HorizontalAlignment = HorizontalAlignment.Center },
                new TextBlock { Text = "Pharmacy customer, prescriptions & loan tracking", Foreground = Brushes.Gray, HorizontalAlignment = HorizontalAlignment.Center },
                _user, _pass, _message, _action } };
        _action.Click += async (_, _) => await SubmitAsync();
        Opened += async (_, _) => await RefreshFirstRunAsync();
    }

    private async Task RefreshFirstRunAsync()
    {
        _firstAdmin = !await _auth.HasUsersAsync();
        _action.Content = _firstAdmin ? "Create first administrator" : "Sign in";
        _message.Text = _firstAdmin ? "First launch: create the initial administrator. Password must be at least 8 characters." : "";
    }

    private async Task SubmitAsync()
    {
        _action.IsEnabled = false;
        try
        {
            if (_firstAdmin)
            {
                await _auth.CreateInitialAdminAsync(_user.Text ?? "", _pass.Text ?? "");
                _message.Text = "Administrator created. Please sign in.";
                _pass.Text = "";
                await RefreshFirstRunAsync();
                return;
            }
            var user = await _auth.LoginAsync(_user.Text ?? "", _pass.Text ?? "");
            if (user == null) { _message.Text = "Invalid username/password or inactive account."; return; }
            new MainWindow(user).Show();
            Close();
        }
        catch (Exception ex) { _message.Text = ex.Message; }
        finally { _action.IsEnabled = true; }
    }
}