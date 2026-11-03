using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using System.Globalization;

namespace ErganiManager.Android;

public partial class MainViewModel : ObservableObject
{
    private MobileApiClient? _api;
    [ObservableProperty] private string _pageTitle = "Sign in";
    [ObservableProperty] private string _serverAddress = "https://thinkpad-p16s.local:5001";
    [ObservableProperty] private string _username = string.Empty;
    [ObservableProperty] private string _password = string.Empty;
    [ObservableProperty] private string _connectionMessage = "Connect to the HTTPS mobile API hosted beside your Ergani II installation.";
    [ObservableProperty] private string _statusMessage = "Use your existing Ergani II account. No separate mobile user database is created.";
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private bool _isAuthenticated;
    [ObservableProperty] private string _companyName = string.Empty;
    [ObservableProperty] private MobileCompany? _selectedCompany;
    [ObservableProperty] private bool _isCompanySelectionRequired;
    public ObservableCollection<MobileCompany> Companies { get; } = new();
    [ObservableProperty] private bool _isDailyStatusVisible;
    [ObservableProperty] private bool _isWorkCalendarVisible;
    [ObservableProperty] private MobileEmployee? _selectedCalendarEmployee;
    [ObservableProperty] private string _selectedCalendarMonth = DateOnly.FromDateTime(DateTime.Today).ToString("yyyy-MM");
    [ObservableProperty] private string _workCalendarMessage = "Choose an employee to load the work calendar.";
    public ObservableCollection<MobileScheduleDay> WorkCalendarDays { get; } = new();
    [ObservableProperty] private string _selectedStatusDate = DateOnly.FromDateTime(DateTime.Today).ToString("yyyy-MM-dd");
    [ObservableProperty] private string _graceMinutes = "10";
    [ObservableProperty] private string _dailyStatusMessage = "Select a date and refresh to load attendance.";
    public ObservableCollection<MobileTodayEmployeeStatus> TodayStatuses { get; } = new();
    public ObservableCollection<MobileEmployee> Employees { get; } = new();

    [RelayCommand]
    private async Task CheckApiHealthAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        try
        {
            using var healthClient = new MobileApiClient(ServerAddress);
            ConnectionMessage = "Checking API health (GET /api/mobile/health)...";
            await healthClient.CheckHealthAsync();
            ConnectionMessage = "API HEALTH OK — received HTTP 200 from /api/mobile/health. You can now sign in.";
        }
        catch (Exception ex)
        {
            ConnectionMessage = "API HEALTH FAILED — " + GetFullError(ex);
        }
        finally { IsBusy = false; }
    }

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
            Password = string.Empty;
            ConnectionMessage = $"Signed in as {session.Username} ({session.Role}).";

            Companies.Clear();
            SelectedCompany = null;
            IsCompanySelectionRequired = false;

            if (session.CompanyId.HasValue)
            {
                CompanyName = session.CompanyName ?? "Assigned company";
                await LoadCompanyDataAsync();
            }
            else
            {
                // Super-admin accounts have no fixed CompanyId. Load the companies
                // they can access and require an explicit selection before tenant data.
                var companies = await _api.GetCompaniesAsync();
                foreach (var company in companies) Companies.Add(company);
                IsCompanySelectionRequired = true;
                CompanyName = "No company selected";
                StatusMessage = companies.Count == 0
                    ? "No active companies are available to this account."
                    : "Choose a company below before loading employees or daily status.";
            }
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
        Employees.Clear();
        Companies.Clear();
        SelectedCompany = null;
        IsCompanySelectionRequired = false;
        IsAuthenticated = false;
        CompanyName = string.Empty;
        PageTitle = "Sign in"; ConnectionMessage = "Signed out.";
        StatusMessage = "Sign in with your existing Ergani II account.";
    }

    [RelayCommand]
    private async Task SelectCompanyAsync()
    {
        if (IsBusy || _api is null || SelectedCompany is null) return;
        IsBusy = true;
        try
        {
            var selected = await _api.SelectCompanyAsync(SelectedCompany.Id);
            CompanyName = selected.Name;
            IsCompanySelectionRequired = false;
            await LoadCompanyDataAsync();
            StatusMessage = $"Connected to {CompanyName}. {Employees.Count} active employees loaded from Ergani II.";
        }
        catch (Exception ex)
        {
            StatusMessage = GetFullError(ex);
            IsCompanySelectionRequired = true;
        }
        finally { IsBusy = false; }
    }

    private async Task LoadCompanyDataAsync()
    {
        if (_api is null) return;
        var employees = await _api.GetEmployeesAsync();
        Employees.Clear();
        foreach (var employee in employees) Employees.Add(employee);
        StatusMessage = $"Connected to {CompanyName}. {Employees.Count} active employees loaded from Ergani II.";
    }

    [RelayCommand] private void OpenWorkCards() => SetPage("Work cards");
    [RelayCommand]
    private async Task OpenDailyStatusAsync()
    {
        if (!IsAuthenticated || _api is null) { StatusMessage = "Please sign in first."; return; }
        if (IsCompanySelectionRequired) { StatusMessage = "Select a company before opening daily status."; return; }
        IsWorkCalendarVisible = false;
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
    [RelayCommand]
    private async Task OpenSchedulesAsync()
    {
        if (!IsAuthenticated || _api is null) { StatusMessage = "Please sign in first."; return; }
        if (IsCompanySelectionRequired) { StatusMessage = "Select a company before opening the work calendar."; return; }
        IsDailyStatusVisible = false;
        IsWorkCalendarVisible = true;
        PageTitle = "Work calendar";
        SelectedCalendarEmployee ??= Employees.FirstOrDefault();
        await RefreshWorkCalendarAsync();
    }

    [RelayCommand]
    private async Task RefreshWorkCalendarAsync()
    {
        if (IsBusy || _api is null) return;
        if (SelectedCalendarEmployee is null) { WorkCalendarMessage = "No active employee is available."; return; }
        if (!DateOnly.TryParseExact(SelectedCalendarMonth + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var month))
        { WorkCalendarMessage = "Select a valid month."; return; }
        IsBusy = true;
        try
        {
            var rows = await _api.GetScheduleMonthAsync(SelectedCalendarEmployee.Id, month.Year, month.Month);
            WorkCalendarDays.Clear();
            foreach (var row in rows.OrderBy(x => x.ScheduleDate)) WorkCalendarDays.Add(row);
            WorkCalendarMessage = $"{SelectedCalendarEmployee.FirstName} {SelectedCalendarEmployee.LastName} · {month.ToString("MMMM yyyy", CultureInfo.CurrentCulture)} · {rows.Count} scheduled day(s)";
        }
        catch (Exception ex) { WorkCalendarMessage = GetFullError(ex); }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private async Task PreviousCalendarMonthAsync()
    {
        if (DateOnly.TryParseExact(SelectedCalendarMonth + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var month))
        { SelectedCalendarMonth = month.AddMonths(-1).ToString("yyyy-MM"); await RefreshWorkCalendarAsync(); }
    }

    [RelayCommand]
    private async Task NextCalendarMonthAsync()
    {
        if (DateOnly.TryParseExact(SelectedCalendarMonth + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var month))
        { SelectedCalendarMonth = month.AddMonths(1).ToString("yyyy-MM"); await RefreshWorkCalendarAsync(); }
    }

    [RelayCommand] private void BackFromWorkCalendar() { IsWorkCalendarVisible = false; PageTitle = "Overview"; }
    [RelayCommand] private void OpenOvertime() => SetPage("Overtime");
    [RelayCommand] private void OpenSubmissions() => SetPage("API submissions");
    [RelayCommand] private void OpenReports() => SetPage("Reports");
    [RelayCommand] private void OpenSettings() => SetPage("Settings");
    private void SetPage(string page)
    {
        if (IsCompanySelectionRequired)
        {
            StatusMessage = "Select a company before opening " + page + ".";
            return;
        }
        IsDailyStatusVisible = false;
        IsWorkCalendarVisible = false;
        PageTitle = page;
        StatusMessage = page == "Employees" && IsAuthenticated
            ? $"{Employees.Count} active employees loaded from the existing employee service."
            : $"{page} will be connected through existing Ergani II Core services.";
    }
}
