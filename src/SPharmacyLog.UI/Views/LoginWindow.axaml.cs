using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Input;
using Avalonia.VisualTree;
using SPharmacyLog.UI.Localization;
using SPharmacyLog.UI.ViewModels;

namespace SPharmacyLog.UI.Views;

public partial class LoginWindow : Window
{
    private readonly LoginViewModel viewModel = new();

    public LoginWindow()
    {
        InitializeComponent();
        UsernameBox.KeyDown += (_, e) => { if (e.Key == Key.Enter) { e.Handled = true; PasswordBox.Focus(); } };
        PasswordBox.KeyDown += (_, e) => { if (e.Key == Key.Enter) { e.Handled = true; OnSubmitClick(this, new RoutedEventArgs()); } };

        DataContext = viewModel;
        Opened += async (_, _) => await InitializeLoginAsync();

        ApplyTranslations();
    }


    private async Task InitializeLoginAsync()
    {
        await viewModel.InitializeAsync();
        var remembered = SPharmacyLog.UI.Services.RememberedLoginService.Load();
        if (remembered is not null)
        {
            UsernameBox.Text = remembered.Value.Username;
            PasswordBox.Text = remembered.Value.Password;
            RememberCredentialsBox.IsChecked = true;
            PasswordBox.Focus();
        }
        else UsernameBox.Focus();
        ApplyTranslations();
    }

    private void ApplyTranslations()
    {
        Title = ApplicationBranding.Name;
        ProductNameText.Text = ApplicationBranding.Name;
        UsernameBox.PlaceholderText = Tr.Loc["Login.Username"];
        PasswordBox.PlaceholderText = Tr.Loc["Login.Password"];
        SubmitButton.Content = viewModel.SubmitText;
        MessageText.Text = Tr.Loc.TranslateText(viewModel.Message);
        foreach (var control in this.GetVisualDescendants().OfType<Control>())
        {
            switch (control)
            {
                case TextBlock textBlock when !string.IsNullOrEmpty(textBlock.Text):
                    textBlock.Text = Tr.Loc.TranslateText(textBlock.Text);
                    break;
                case Button button when button.Content is string content:
                    button.Content = Tr.Loc.TranslateText(content);
                    break;
                case TextBox textBox when !string.IsNullOrEmpty(textBox.PlaceholderText):
                    textBox.PlaceholderText = Tr.Loc.TranslateText(textBox.PlaceholderText);
                    break;
            }
        }
    }

    private async void OnSubmitClick(object? sender, RoutedEventArgs e)
    {
        SubmitButton.IsEnabled = false;

        viewModel.Username = UsernameBox.Text ?? string.Empty;
        viewModel.Password = PasswordBox.Text ?? string.Empty;

        try
        {
            var user = await viewModel.SubmitAsync();

            MessageText.Text = Tr.Loc.TranslateText(viewModel.Message);
            SubmitButton.Content = viewModel.SubmitText;
            PasswordBox.Text = viewModel.Password;

            if (RememberCredentialsBox.IsChecked != true)
                SPharmacyLog.UI.Services.RememberedLoginService.Delete();

            if (user is not null)
            {
                if (RememberCredentialsBox.IsChecked == true)
                    SPharmacyLog.UI.Services.RememberedLoginService.Save(viewModel.Username.Trim(), viewModel.Password);

                var main = new MainWindow(user);
                if (Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
                    desktop.MainWindow = main;
                main.Show();
                Close();
            }
        }
        finally
        {
            SubmitButton.IsEnabled = true;
        }
    }
}
