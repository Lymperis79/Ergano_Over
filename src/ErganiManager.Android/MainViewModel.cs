using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ErganiManager.Android;

public partial class MainViewModel : ObservableObject
{
    [ObservableProperty] private string _pageTitle = "Overview";
    [ObservableProperty] private string _serverAddress = string.Empty;
    [ObservableProperty] private string _connectionMessage =
        "Configure the secure mobile service endpoint to connect this device to your Ergani Manager data.";
    [ObservableProperty] private string _statusMessage =
        "The Android interface foundation is in place. Secure sign-in and live company data are the next integration stage.";

    [RelayCommand]
    private void SaveConnection()
    {
        ConnectionMessage = string.IsNullOrWhiteSpace(ServerAddress)
            ? "Enter the service address before saving."
            : "Address entered. Secure connection and sign-in will be enabled when the mobile API service is configured.";
    }

    [RelayCommand] private void OpenWorkCards() => SetPage("Work cards");
    [RelayCommand] private void OpenDailyStatus() => SetPage("Daily status");
    [RelayCommand] private void OpenEmployees() => SetPage("Employees");
    [RelayCommand] private void OpenSchedules() => SetPage("Work calendar");
    [RelayCommand] private void OpenOvertime() => SetPage("Overtime");
    [RelayCommand] private void OpenSubmissions() => SetPage("API submissions");
    [RelayCommand] private void OpenReports() => SetPage("Reports");
    [RelayCommand] private void OpenSettings() => SetPage("Settings");

    private void SetPage(string page)
    {
        PageTitle = page;
        StatusMessage = $"{page} is part of the mobile navigation. Live records and actions will be connected through the secured mobile API.";
    }
}
