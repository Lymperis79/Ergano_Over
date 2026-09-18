using System;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using CommunityToolkit.Mvvm.ComponentModel;
using ErganiManager.UI.ViewModels;

namespace ErganiManager.UI.Views;

public partial class AdminShellView : Window
{
    public AdminShellView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (DataContext is not AdminShellViewModel shell) return;

        // Forward error StatusMessages from section ViewModels to the global notification bar
        shell.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName != nameof(AdminShellViewModel.CurrentSectionViewModel)) return;
            if (shell.CurrentSectionViewModel is not ObservableObject vm) return;

            vm.PropertyChanged += (_, vmArgs) =>
            {
                if (vmArgs.PropertyName != "StatusMessage") return;
                var msg = vm.GetType().GetProperty("StatusMessage")?.GetValue(vm) as string;
                if (!string.IsNullOrEmpty(msg) && msg.StartsWith("❌"))
                    shell.ShowError(msg);
                else if (!string.IsNullOrEmpty(msg) && msg.StartsWith("✅"))
                    shell.ShowNotification(msg, isError: false);
            };
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
            CompaniesViewModel => new CompaniesView { DataContext = data },
            BranchesViewModel => new BranchesView { DataContext = data },
            EmployeesViewModel => new EmployeesView { DataContext = data },
            UsersViewModel => new UsersView { DataContext = data },
            SchedulesViewModel => new SchedulesView { DataContext = data },
            WorkCardHistoryViewModel => new WorkCardHistoryView { DataContext = data },
            WorkCardScanViewModel   => new WorkCardScanView    { DataContext = data },
            OvertimeViewModel       => new OvertimeView        { DataContext = data },
            SubmissionLogViewModel => new SubmissionLogView { DataContext = data },
            _ => null
        };
    }

    public bool Match(object? data) => data is ViewModelBase;
}
