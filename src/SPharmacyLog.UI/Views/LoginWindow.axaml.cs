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
    private ComboBox languageBox = null!;
    private bool isApplyingTranslations;

    public LoginWindow()
    {
        InitializeComponent();
        languageBox = this.FindControl<ComboBox>("LanguageBox")!;
        UsernameBox.KeyDown += (_, e) => { if (e.Key == Key.Enter) { e.Handled = true; PasswordBox.Focus(); } };
        PasswordBox.KeyDown += async (_, e) => { if (e.Key == Key.Enter) { e.Handled = true; OnSubmitClick(this, new RoutedEventArgs()); } };

        DataContext = viewModel;
        Opened += async (_, _) => await InitializeLoginAsync();

        Tr.Loc.LanguageChanged += (_, _) => ApplyTranslations();
        ApplyTranslations();
    }


    private async Task InitializeLoginAsync()
    {
        await viewModel.InitializeAsync();
        ApplyTranslations();
    }

    private void ApplyTranslations()
    {
        isApplyingTranslations = true;
        Title = Tr.Loc["App.Title"];
        if (languageBox is not null) languageBox.SelectedIndex = Tr.Loc.CurrentLanguage == AppLanguage.Greek ? 1 : 0;
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
                case TextBox textBox when !string.IsNullOrEmpty(textBox.Watermark):
                    textBox.Watermark = Tr.Loc.TranslateText(textBox.Watermark);
                    break;
                case ComboBoxItem comboItem when comboItem.Content is string itemText:
                    comboItem.Content = Tr.Loc.TranslateText(itemText);
                    break;
            }
        }
        isApplyingTranslations = false;
    }

    private void OnLanguageChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (isApplyingTranslations) return;
        if (languageBox?.SelectedIndex == 1) Tr.Loc.SetLanguage(AppLanguage.Greek);
        else if (languageBox?.SelectedIndex == 0) Tr.Loc.SetLanguage(AppLanguage.English);
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
