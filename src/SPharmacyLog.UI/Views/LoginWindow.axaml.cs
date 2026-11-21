using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using SPharmacyLog.UI.Localization;
using SPharmacyLog.UI.ViewModels;

namespace SPharmacyLog.UI.Views;

public partial class LoginWindow : Window
{
    private readonly LoginViewModel viewModel = new();

    public LoginWindow()
    {
        InitializeComponent();

        DataContext = viewModel;
        Opened += async (_, _) => await InitializeLoginAsync();

        Tr.Loc.LanguageChanged += (_, _) => ApplyTranslations();
        ApplyTranslations();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private async Task InitializeLoginAsync()
    {
        await viewModel.InitializeAsync();
        ApplyTranslations();
    }

    private void ApplyTranslations()
    {
        Title = Tr.Loc["App.Title"];
        UsernameBox.PlaceholderText = Tr.Loc["Login.Username"];
        PasswordBox.PlaceholderText = Tr.Loc["Login.Password"];
        SubmitButton.Content = viewModel.SubmitText;
        MessageText.Text = viewModel.Message;
    }

    private async void OnSubmitClick(object? sender, RoutedEventArgs e)
    {
        SubmitButton.IsEnabled = false;

        viewModel.Username = UsernameBox.Text ?? string.Empty;
        viewModel.Password = PasswordBox.Text ?? string.Empty;

        try
        {
            var user = await viewModel.SubmitAsync();

            MessageText.Text = viewModel.Message;
            SubmitButton.Content = viewModel.SubmitText;
            PasswordBox.Text = viewModel.Password;

            if (user is not null)
            {
                new MainWindow(user).Show();
                Close();
            }
        }
        finally
        {
            SubmitButton.IsEnabled = true;
        }
    }
}
