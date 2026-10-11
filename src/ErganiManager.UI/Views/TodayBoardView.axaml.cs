using Avalonia.Controls;
using ErganiManager.UI.ViewModels;

namespace ErganiManager.UI.Views;

public partial class TodayBoardView : UserControl
{
    public TodayBoardView()
    {
        InitializeComponent();

        // Refresh automatically only while the board is visible.
        AttachedToVisualTree   += (_, _) => (DataContext as TodayBoardViewModel)?.StartAutoRefresh();
        DetachedFromVisualTree += (_, _) => (DataContext as TodayBoardViewModel)?.StopAutoRefresh();
    }
}
