using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using System.Globalization;

namespace ErganiManager.Android;

public partial class MainViewModel : ObservableObject
{
    private MobileApiClient? _api;
    [ObservableProperty] private string _pageTitle = "Sign in";
    [ObservableProperty] private string _serverAddress = string.Empty;
    [ObservableProperty] private string _username = string.Empty;
    [ObservableProperty] private string _password = string.Empty;
    [ObservableProperty] private string _connectionMessage = "Connect to the HTTPS mobile API hosted beside your Ergani II installation.";
    [ObservableProperty] private string _statusMessage = "Use your existing Ergani II account. No separate mobile user database is created.";
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private bool _isAuthenticated;
    [ObservableProperty] private string _companyName = string.Empty;
    [ObservableProperty] private bool _isDailyStatusVisible;
    [ObservableProperty] private string _selectedStatusDate = DateOnly.FromDateTime(DateTime.Today).ToString("yyyy-MM-dd");
    [ObservableProperty] private string _graceMinutes = "10";
    [ObservableProperty] private string _dailyStatusMessage = "Select a date and refresh to load attendance.";
    public ObservableCollection<MobileTodayEmployeeStatus> TodayStatuses { get; } = new();
    public ObservableCollection<MobileEmployee> Employees { get; } = new();

    [RelayCommand]
    private async Task ConnectAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        try
        {
            _api?.Dispose();
            _api = new MobileApiClient(ServerAddress);

            ConnectionMessage = "Checking HTTPS connection to the Mobile API...";
            await _api.CheckHealthAsync();

            ConnectionMessage = "API reachable. Signing in...";
            var session = await _api.LoginAsync(Username, Password);
            IsAuthenticated = true;
            CompanyName = session.CompanyName ?? "Company selection required";
            Password = string.Empty;
            ConnectionMessage = $"Signed in as {session.Username} ({session.Role}).";
            if (session.CompanyId.HasValue)
            {
                var employees = await _api.GetEmployeesAsync();
                Employees.Clear();
                foreach (var employee in employees) Employees.Add(employee);
                StatusMessage = $"Connected to {CompanyName}. {Employees.Count} active employees loaded from Ergani II.";
            }
            else StatusMessage = "This administrator has no fixed company. Company selection is the next step.";
            PageTitle = "Overview";
        }
        catch (Exception ex)
        {
            IsAuthenticated = false;
            ConnectionMessage = GetFullError(ex);
            StatusMessage = "Connection diagnostics are shown above. If the API health check succeeds, verify the login response and credentials.";
            _api?.Dispose(); _api = null;
        }
        finally { IsBusy = false; }
    }

    private static string GetFullError(Exception exception)
    {
        var messages = new List<string>();
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (!string.IsNullOrWhiteSpace(current.Message) && (messages.Count == 0 || messages[^1] != current.Message))
                messages.Add(current.Message);
        }
        return string.Join(" → ", messages);
    }

    [RelayCommand]
    private async Task LogoutAsync()
    {
        if (_api is not null) { try { await _api.LogoutAsync(); } catch { } _api.Dispose(); _api = null; }
        Employees.Clear(); IsAuthenticated = false; CompanyName = string.Empty;
        PageTitle = "Sign in"; ConnectionMessage = "Signed out.";
        StatusMessage = "Sign in with your existing Ergani II account.";
    }

    [RelayCommand] private void OpenWorkCards() => SetPage("Work cards");
    [RelayCommand]
    private async Task OpenDailyStatusAsync()
    {
        if (!IsAuthenticated || _api is null) { StatusMessage = "Please sign in first."; return; }
        IsDailyStatusVisible = true;
        PageTitle = "Daily status";
        await RefreshDailyStatusAsync();
    }

    [RelayCommand]
    private async Task RefreshDailyStatusAsync()
    {
        if (IsBusy || _api is null) return;
        if (!DateOnly.TryParseExact(SelectedStatusDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        { DailyStatusMessage = "Enter date as yyyy-MM-dd."; return; }
        if (!int.TryParse(GraceMinutes, out var grace) || grace < 0 || grace > 240)
        { DailyStatusMessage = "Grace minutes must be between 0 and 240."; return; }
        IsBusy = true;
        try
        {
            var rows = await _api.GetTodayStatusAsync(date, grace);
            TodayStatuses.Clear();
            foreach (var row in rows) TodayStatuses.Add(row);
            DailyStatusMessage = rows.Count + " employees · " + date.ToString("dd/MM/yyyy");
        }
        catch (Exception ex) { DailyStatusMessage = GetFullError(ex); }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private void BackToOverview() { IsDailyStatusVisible = false; PageTitle = "Overview"; }
    [RelayCommand] private void OpenEmployees() => SetPage("Employees");
    [RelayCommand] private void OpenSchedules() => SetPage("Work calendar");
    [RelayCommand] private void OpenOvertime() => SetPage("Overtime");
    [RelayCommand] private void OpenSubmissions() => SetPage("API submissions");
    [RelayCommand] private void OpenReports() => SetPage("Reports");
    [RelayCommand] private void OpenSettings() => SetPage("Settings");
    private void SetPage(string page)
    {
        IsDailyStatusVisible = false;
        PageTitle = page;
        StatusMessage = page == "Employees" && IsAuthenticated
            ? $"{Employees.Count} active employees loaded from the existing employee service."
            : $"{page} will be connected through existing Ergani II Core services.";
    }
}
