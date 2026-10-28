using System;
using System.ComponentModel;
using ErganiManager.UI.Localization;
using ErganiManager.UI.Services;
using Avalonia.Media;
using Avalonia.Interactivity;
using Avalonia.Input;
using Avalonia.Controls.Primitives;
using Avalonia;
using System.Linq;
using System.IO;
using System.Text.RegularExpressions;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CommunityToolkit.Mvvm.ComponentModel;
using ErganiManager.UI.ViewModels;

namespace ErganiManager.UI.Views;

public partial class AdminShellView : Window
{
    private ObservableObject? _statusMessageViewModel;
    private PropertyChangedEventHandler? _statusMessageHandler;
    public AdminShellView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        Opened += (_, _) => { UpdateNavHighlight(); ShowLicenseButton(); };
    }

    // -- License (the 🔑 button) ---------------------------------------------------------------

    /// <summary>The key button is only there when the program is license protected; it warns before the end date.</summary>
    private void ShowLicenseButton()
    {
        var button = this.FindControl<Button>("LicenseButton");
        if (button == null || !ErganiManager.Licensing.LicenseState.Enforced) return;

        button.IsVisible = true;
        var payload = ErganiManager.Licensing.LicenseState.Current;
        var days = payload?.DaysLeft(DateOnly.FromDateTime(DateTime.Now));
        var text = payload == null ? string.Empty : payload.Licensee;
        if (days is { } left && left <= 30)
        {
            button.Content = "🔑⚠";
            text += "\n" + string.Format(Tr.Loc["Vm.License expires in {0} day(s)."], Math.Max(0, left));
        }
        ToolTip.SetTip(button, text);
    }

    private async void OnLicenseClick(object? sender, RoutedEventArgs e)
    {
        await new ActivationWindow(startup: false).ShowDialog(this);
        ShowLicenseButton();
    }

    // -- Colours of the interface (the 🎨 button) ---------------------------------------------

    private Flyout? _appearanceFlyout;

    private void OnAppearanceClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control button) return;

        _appearanceFlyout ??= new Flyout { Placement = PlacementMode.BottomEdgeAlignedRight };
        _appearanceFlyout.Content = BuildAppearancePanel();
        _appearanceFlyout.ShowAt(button);
    }

    /// <summary>The picker: background styles and accent colours. A click applies and saves at once.</summary>
    private Control BuildAppearancePanel()
    {
        var accent = Color.Parse(AppearanceService.Accent);

        var panel = new StackPanel { Spacing = 10, MinWidth = 320, MaxWidth = 380 };
        panel.Children.Add(new TextBlock { Text = Tr.Loc["AppearanceTitle"], FontWeight = FontWeight.Bold, FontSize = 15 });

        panel.Children.Add(new TextBlock { Text = Tr.Loc["AppearanceBackground"], FontSize = 12, Opacity = 0.7 });
        var styles = new WrapPanel();
        foreach (var preset in AppearanceService.Presets)
        {
            var selected = preset.Key == AppearanceService.Theme;
            var key = preset.Key;

            var sample = new Border
            {
                Width = 26, Height = 26, CornerRadius = new CornerRadius(5),
                Background = new SolidColorBrush(Color.Parse(preset.Card)),
                BorderBrush = new SolidColorBrush(Color.Parse(preset.Line)),
                BorderThickness = new Thickness(1)
            };
            var row = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 8 };
            row.Children.Add(sample);
            row.Children.Add(new TextBlock { Text = Tr.Loc["Vm.Theme " + key], VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center });

            var item = new Border
            {
                Margin = new Thickness(0, 0, 8, 8), Padding = new Thickness(8, 5), CornerRadius = new CornerRadius(6),
                BorderThickness = new Thickness(selected ? 2 : 1),
                BorderBrush = selected ? new SolidColorBrush(accent) : new SolidColorBrush(Color.Parse("#555555")),
                Cursor = new Cursor(StandardCursorType.Hand),
                Background = Brushes.Transparent,
                Child = row
            };
            item.PointerPressed += (_, _) => { AppearanceService.Choose(theme: key); RefreshAppearancePanel(); };
            styles.Children.Add(item);
        }
        panel.Children.Add(styles);

        panel.Children.Add(new TextBlock { Text = Tr.Loc["AppearanceAccent"], FontSize = 12, Opacity = 0.7 });
        var accents = new WrapPanel();
        foreach (var hex in AppearanceService.AccentColors)
        {
            var selected = string.Equals(hex, AppearanceService.Accent, StringComparison.OrdinalIgnoreCase);
            var color = hex;

            var dot = new Border
            {
                Width = 30, Height = 30, CornerRadius = new CornerRadius(15),
                Margin = new Thickness(0, 0, 8, 8),
                Background = new SolidColorBrush(Color.Parse(hex)),
                BorderBrush = selected ? Brushes.White : Brushes.Transparent,
                BorderThickness = new Thickness(3),
                Cursor = new Cursor(StandardCursorType.Hand),
                Child = selected ? new TextBlock
                {
                    Text = "✓", Foreground = Brushes.White, FontWeight = FontWeight.Bold,
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                    VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
                } : null
            };
            dot.PointerPressed += (_, _) => { AppearanceService.Choose(accent: color); RefreshAppearancePanel(); };
            accents.Children.Add(dot);
        }
        panel.Children.Add(accents);

        var reset = new Button { Content = Tr.Loc["AppearanceReset"], HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left };
        reset.Click += (_, _) => { AppearanceService.Reset(); RefreshAppearancePanel(); };
        panel.Children.Add(reset);

        return panel;
    }

    /// <summary>Shows the new selection marks after a click.</summary>
    private void RefreshAppearancePanel()
    {
        if (_appearanceFlyout != null) _appearanceFlyout.Content = BuildAppearancePanel();
    }

    /// <summary>Marks the menu button of the open section (class "active", styled in the XAML).</summary>
    private void UpdateNavHighlight()
    {
        if (DataContext is not AdminShellViewModel shell) return;

        var active = shell.ActiveSection.ToString();
        foreach (var button in this.GetVisualDescendants().OfType<Button>().Where(b => b.Classes.Contains("navItem")))
            button.Classes.Set("active", string.Equals(button.CommandParameter as string, active, StringComparison.Ordinal));
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (DataContext is not AdminShellViewModel shell) return;

        // Keep the highlighted menu item in step with the open section.
        shell.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(AdminShellViewModel.ActiveSection))
                Dispatcher.UIThread.Post(UpdateNavHighlight);
        };
        Dispatcher.UIThread.Post(UpdateNavHighlight);

        // Forward error StatusMessages from section ViewModels to the global notification bar
        shell.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName != nameof(AdminShellViewModel.CurrentSectionViewModel)) return;

            // Detach the previous section handler before subscribing to the new section.
            // Otherwise revisiting a section can register duplicate handlers and show the
            // same success notification more than once.
            if (_statusMessageViewModel != null && _statusMessageHandler != null)
                _statusMessageViewModel.PropertyChanged -= _statusMessageHandler;

            _statusMessageViewModel = shell.CurrentSectionViewModel as ObservableObject;
            _statusMessageHandler = null;
            if (_statusMessageViewModel is not ObservableObject vm) return;

            _statusMessageHandler = (_, vmArgs) =>
            {
                if (vmArgs.PropertyName != "StatusMessage") return;
                var msg = vm.GetType().GetProperty("StatusMessage")?.GetValue(vm) as string;
                if (!string.IsNullOrEmpty(msg) && msg.StartsWith("❌"))
                    shell.ShowError(msg);
                else if (!string.IsNullOrEmpty(msg) && msg.StartsWith("✅"))
                {
                    string? filePath = null;
                    var match = Regex.Match(msg, @"([A-Za-z]:\\.*?\.(?:pdf|xlsx|xls|csv))", RegexOptions.IgnoreCase);
                    if (match.Success && File.Exists(match.Groups[1].Value))
                        filePath = match.Groups[1].Value;

                    shell.ShowNotification(msg, isError: false, filePath: filePath);
                }
            };
            vm.PropertyChanged += _statusMessageHandler;
        };
    }
}


/// <summary>
/// Maps each section ViewModel type to its corresponding UserControl view.
/// Used by AdminShellView's ContentControl so navigating sections just means
/// swapping CurrentSectionViewModel — no manual view instantiation needed.
/// </summary>
public class SectionViewModelTemplateSelector : IDataTemplate
{
    public Control? Build(object? data)
    {
        return data switch
        {
            DashboardViewModel => new DashboardView { DataContext = data },
            CompaniesViewModel => new CompaniesView { DataContext = data },
            BranchesViewModel => new BranchesView { DataContext = data },
            EmployeesViewModel => new EmployeesView { DataContext = data },
            UsersViewModel => new UsersView { DataContext = data },
            SchedulesViewModel => new SchedulesView { DataContext = data },
            WorkCardHistoryViewModel => new WorkCardHistoryView { DataContext = data },
            WorkCardScanViewModel   => new WorkCardScanView    { DataContext = data },
            TodayBoardViewModel     => new TodayBoardView      { DataContext = data },
            OvertimeViewModel       => new OvertimeView        { DataContext = data },
            ReportsViewModel        => new ReportsView         { DataContext = data },
            SubmissionLogViewModel => new SubmissionLogView { DataContext = data },
            EmailLogViewModel       => new EmailLogView        { DataContext = data },
            AdministrationViewModel => new AdministrationView { DataContext = data },
            HelpViewModel           => new HelpView           { DataContext = data },
            _ => null
        };
    }

    public bool Match(object? data) => data is ViewModelBase;
}
