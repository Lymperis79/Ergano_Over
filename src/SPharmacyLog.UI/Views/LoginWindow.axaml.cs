using Avalonia.Controls;
using Avalonia.Interactivity;
using SPharmacyLog.Core;

namespace SPharmacyLog.UI.Views;

public partial class LoginWindow : Window
{
    private readonly PharmacyAuthService auth = new();
    private bool creatingFirstAdministrator;

    public LoginWindow()
    {
        InitializeComponent();
        Opened += async (_, _) => await InitializeLoginAsync();
    }

    private void InitializeComponent() => Avalonia.Markup.Xaml.AvaloniaXamlLoader.Load(this);

    private async Task InitializeLoginAsync()
    {
        creatingFirstAdministrator = !await auth.HasUsersAsync();
        SubmitButton.Content = creatingFirstAdministrator ? "Create first administrator" : "Sign in";
        MessageText.Text = creatingFirstAdministrator
            ? "First launch: create the administrator account (minimum 8-character password)."
            : string.Empty;
    }

    private async void OnSubmitClick(object? sender, RoutedEventArgs e)
    {
        SubmitButton.IsEnabled = false;
        try
        {
            var username = UsernameBox.Text?.Trim() ?? string.Empty;
            var password = PasswordBox.Text ?? string.Empty;

            if (creatingFirstAdministrator)
            {
                await auth.CreateFirstAdminAsync(username, password);
                MessageText.Text = "Administrator created. Sign in with the new credentials.";
                PasswordBox.Text = string.Empty;
                await InitializeLoginAsync();
                return;
            }

            var user = await auth.LoginAsync(username, password);
            if (user is null)
            {
                MessageText.Text = "Login failed. Check the username, password, and account status.";
                return;
            }

            var main = new MainWindow(user);
            main.Show();
            Close();
        }
        catch (Exception ex)
        {
            MessageText.Text = ex.Message;
        }
        finally
        {
            SubmitButton.IsEnabled = true;
        }
    }
}