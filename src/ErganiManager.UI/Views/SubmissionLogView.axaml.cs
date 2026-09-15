using Avalonia.Controls;
using Avalonia.Input;
using ErganiManager.UI.ViewModels;

namespace ErganiManager.UI.Views;

public partial class SubmissionLogView : UserControl
{
    public SubmissionLogView()
    {
        InitializeComponent();
    }

    private void OnRowPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Border { Tag: SubmissionLogRow row } && DataContext is SubmissionLogViewModel vm)
            vm.SelectedRow = row;
    }
}
