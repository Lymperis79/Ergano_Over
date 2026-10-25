using System;
using Avalonia.Controls;
using Avalonia.Threading;
using ErganiManager.UI.ViewModels;

namespace ErganiManager.UI.Views;

public partial class BarcodeScanWindow : Window
{
    private TextBox? _scanBox;

    public BarcodeScanWindow()
    {
        InitializeComponent();
        Opened    += (_, _) => { _scanBox = this.FindControl<TextBox>("ScanInputBox"); FocusScanBox(); };
        Activated += (_, _) => FocusScanBox();
        DataContextChanged += (_, _) =>
        {
            if (DataContext is not WorkCardScanViewModel vm) return;
            vm.PropertyChanged += (_, args) =>
            {
                if ((args.PropertyName == nameof(WorkCardScanViewModel.BarcodeInput)
                        && string.IsNullOrEmpty(vm.BarcodeInput))
                    || (args.PropertyName == nameof(WorkCardScanViewModel.HasResponse)
                        && !vm.HasResponse))
                    Dispatcher.UIThread.Post(FocusScanBox, DispatcherPriority.Input);
            };
        };
    }

    private void FocusScanBox()
    {
        _scanBox ??= this.FindControl<TextBox>("ScanInputBox");
        if (_scanBox == null) return;
        _scanBox.Focus();
        _scanBox.CaretIndex = _scanBox.Text?.Length ?? 0;
    }
}
