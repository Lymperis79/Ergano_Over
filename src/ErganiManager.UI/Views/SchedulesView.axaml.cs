using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using ErganiManager.UI.ViewModels;

namespace ErganiManager.UI.Views;

public partial class SchedulesView : UserControl
{
    private readonly DispatcherTimer _menuCloseTimer = new() { Interval = TimeSpan.FromMilliseconds(350) };
    private MenuItem? _activeHoverMenu;

    public SchedulesView()
    {
        InitializeComponent();
        _menuCloseTimer.Tick += (_, _) =>
        {
            _menuCloseTimer.Stop();
            if (_activeHoverMenu is not null)
                _activeHoverMenu.IsSubMenuOpen = false;
            _activeHoverMenu = null;
        };
        DataContextChanged += (_, _) =>
        {
            if (DataContext is SchedulesViewModel vm)
                vm.ImportRequested += OnImportRequested;
        };
    }

    private void OnRootMenuItemPointerEntered(object? sender, PointerEventArgs e)
    {
        if (sender is not MenuItem menuItem || menuItem.Items.Count == 0)
            return;

        _menuCloseTimer.Stop();
        _activeHoverMenu = menuItem;
        menuItem.IsSubMenuOpen = true;
        AttachSubmenuHoverHandlers(menuItem);
    }

    private void AttachSubmenuHoverHandlers(MenuItem parent)
    {
        foreach (var child in parent.Items.OfType<MenuItem>())
        {
            child.PointerEntered -= OnSubmenuItemPointerEntered;
            child.PointerExited -= OnSubmenuItemPointerExited;
            child.PointerEntered += OnSubmenuItemPointerEntered;
            child.PointerExited += OnSubmenuItemPointerExited;
            AttachSubmenuHoverHandlers(child);
        }
    }

    private void OnSubmenuItemPointerEntered(object? sender, PointerEventArgs e)
    {
        _menuCloseTimer.Stop();
    }

    private void OnSubmenuItemPointerExited(object? sender, PointerEventArgs e)
    {
        ScheduleHoverMenuClose();
    }

    private void OnRootMenuItemPointerExited(object? sender, PointerEventArgs e)
    {
        ScheduleHoverMenuClose();
    }

    private void ScheduleHoverMenuClose()
    {
        if (_activeHoverMenu is null)
            return;

        _menuCloseTimer.Stop();
        _menuCloseTimer.Start();
    }

    private void OnCellPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Border { Tag: CalendarCellViewModel cell }
            && DataContext is SchedulesViewModel vm)
        {
            bool ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control)
                     || e.KeyModifiers.HasFlag(KeyModifiers.Meta);
            vm.OnCellClick(cell, ctrl);
        }
    }

    private void OnLogResponseClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: ScheduleLogRow row } && DataContext is SchedulesViewModel vm)
            vm.ShowResponseCommand.Execute(row);
    }

    private void OnLogPdfClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: ScheduleLogRow row } && DataContext is SchedulesViewModel vm)
            _ = vm.DownloadPdfCommand.ExecuteAsync(row);
    }

    private async void OnImportRequested(object? sender, System.EventArgs e)
    {
        if (DataContext is not SchedulesViewModel vm) return;

        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null) return;

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(
            new FilePickerOpenOptions
            {
                Title             = Tr.Loc["Vm.Import Schedule from Excel"],
                AllowMultiple     = false,
                FileTypeFilter    = new[]
                {
                    new FilePickerFileType(Tr.Loc["Vm.Excel Files"]) { Patterns = new[] { "*.xlsx" } }
                }
            });

        var file = files.FirstOrDefault();
        if (file == null) return;

        var path = file.TryGetLocalPath();
        if (path != null)
            await vm.PrepareErganiImportFromFileAsync(path);
    }
}
