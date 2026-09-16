using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace ErganiManager.UI.Views;

public partial class TerminalView : Window
{
    private TextBox? _scanInputBox;

    public TerminalView()
    {
        InitializeComponent();

        Opened += OnOpened;
        Activated += (_, _) => RefocusScanInput();

        // Any click anywhere on the terminal refocuses the scan box — this is
        // a locked-down kiosk screen, the operator should never need to click
        // into the field manually, but this guards against accidental focus loss.
        PointerPressed += (_, _) => RefocusScanInput();
    }

    private void OnOpened(object? sender, System.EventArgs e)
    {
        _scanInputBox = this.FindControl<TextBox>("ScanInputBox");
        RefocusScanInput();

        // Belt-and-suspenders: periodically ensure focus hasn't drifted away
        // from the scan input, since this is the only interactive element a
        // locked terminal should ever respond to.
        var timer = new DispatcherTimer { Interval = System.TimeSpan.FromSeconds(2) };
        timer.Tick += (_, _) => RefocusScanInput();
        timer.Start();
    }

    private void RefocusScanInput()
    {
        if (_scanInputBox != null && !_scanInputBox.IsFocused)
        {
            Dispatcher.UIThread.Post(() => _scanInputBox.Focus());
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        // Block common ways out of a kiosk screen. Alt+F4, Alt+Tab, and the
        // Windows key are the main escape routes on a shared terminal PC.
        if (e.Key == Key.F4 && e.KeyModifiers.HasFlag(KeyModifiers.Alt))
        {
            e.Handled = true;
        }
        else if (e.Key == Key.Tab && e.KeyModifiers.HasFlag(KeyModifiers.Alt))
        {
            e.Handled = true;
        }
        else if (e.Key is Key.LWin or Key.RWin)
        {
            e.Handled = true;
        }
    }
}
