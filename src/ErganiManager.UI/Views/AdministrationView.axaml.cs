using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using ErganiManager.UI.ViewModels;

namespace ErganiManager.UI.Views;

public partial class AdministrationView : UserControl
{
    public AdministrationView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (DataContext is AdministrationViewModel vm)
            {
                vm.DatabaseDeleted -= OnDatabaseDeleted;
                vm.DatabaseDeleted += OnDatabaseDeleted;
            }
        };
    }

    private async void OnBackupClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not AdministrationViewModel vm) return;
        var top = TopLevel.GetTopLevel(this);
        if (top == null) return;

        var file = await top.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Backup database",
            SuggestedFileName = vm.SuggestedBackupName,
            ShowOverwritePrompt = true
        });

        var path = file?.TryGetLocalPath();
        if (!string.IsNullOrEmpty(path))
            await vm.BackupToAsync(path);
    }

    private void OnDatabaseDeleted(object? sender, EventArgs e)
    {
        // Back to the first-run wizard, which recreates the schema.
        if (Application.Current is App app &&
            Application.Current.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            app.ShowDatabaseSetup(desktop);
    }
}
