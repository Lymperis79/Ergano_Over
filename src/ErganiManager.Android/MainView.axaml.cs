using Avalonia.Controls;

namespace ErganiManager.Android;

public partial class MainView : UserControl
{
    public MainView()
    {
        InitializeComponent();
        DataContext = new MainViewModel();
    }

    private void InitializeComponent()
        => AvaloniaXamlLoader.Load(this);
}
