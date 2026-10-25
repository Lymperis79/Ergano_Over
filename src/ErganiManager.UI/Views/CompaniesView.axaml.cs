using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using ErganiManager.Core.Interfaces;
using ErganiManager.UI.ViewModels;

namespace ErganiManager.UI.Views;

public partial class CompaniesView : UserControl
{
    public CompaniesView()
    {
        InitializeComponent();
    }

    private async void OnChooseLogoClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not CompaniesViewModel vm) return;
        var top = TopLevel.GetTopLevel(this);
        if (top == null) return;

        var files = await top.StorageProvider.OpenFilePickerAsync(new Avalonia.Platform.Storage.FilePickerOpenOptions
        {
            Title = Tr.Loc["Vm.Select company picture"],
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new Avalonia.Platform.Storage.FilePickerFileType(Tr.Loc["Vm.Pictures"])
                    { Patterns = new[] { "*.png", "*.jpg", "*.jpeg", "*.bmp", "*.gif", "*.webp" } }
            }
        });
        if (files.Count == 0) return;

        try
        {
            await using var stream = await files[0].OpenReadAsync();
            if (stream.CanSeek && stream.Length > ErganiManager.UI.Services.CompanyLogoService.MaxFileBytes)
            {
                vm.StatusMessage = Tr.Loc["Vm.The picture file is too large (maximum 8 MB)."];
                return;
            }
            vm.SetLogo(ErganiManager.UI.Services.CompanyLogoService.PrepareFromFile(stream));
        }
        catch (Exception)
        {
            vm.StatusMessage = Tr.Loc["Vm.The selected file is not a valid picture."];
        }
    }

    private void OnEditClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: CompanyDto company } && DataContext is CompaniesViewModel vm)
            vm.StartEditCommand.Execute(company);
    }

    private void OnToggleActiveClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: CompanyDto company } && DataContext is CompaniesViewModel vm)
            vm.ToggleActiveCommand.Execute(company);
    }

    private void OnDeleteClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: CompanyDto item } && DataContext is CompaniesViewModel vm)
            vm.DeleteCommand.Execute(item);
    }
}