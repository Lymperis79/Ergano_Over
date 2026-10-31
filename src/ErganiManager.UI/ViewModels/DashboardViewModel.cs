using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Net.Http;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ErganiManager.Core.Interfaces;
using ErganiManager.Core.Models;
using ErganiManager.Data;
using ErganiManager.Data.Entities;
using Microsoft.EntityFrameworkCore;
using ErganiManager.UI.Services;
using ErganiManager.ErganiApi.Services;
using ErganiManager.ErganiApi.Models;

namespace ErganiManager.UI.ViewModels;

public partial class DashboardViewModel : ViewModelBase, IAdminSectionViewModel
{
    private readonly IConnectionStateService _connectionState;
    private readonly AutomaticDatabaseBackupService _automaticBackup;
    private readonly IInternetCheckService _internetCheck;
    private readonly IErganiHealthCheckService _erganiHealth;
    private readonly ICredentialProtector _credentialProtector;
    private readonly DailyReconciliationService _reconciliation;
    private UserSession? _session;

    [ObservableProperty] private string _companyName = string.Empty;
    [ObservableProperty] private int _branchCount;
    [ObservableProperty] private int _employeeCount;
    [ObservableProperty] private int _submissionCount;
    [ObservableProperty] private int _errorCount;
    [ObservableProperty] private int _pendingCount;
    [ObservableProperty] private int _successfulCount;
    [ObservableProperty] private int _failedCount;
    [ObservableProperty] private string _periodText = string.Empty;
    [ObservableProperty] private string _lastUpdatedText = string.Empty;
    [ObservableProperty] private string _statusMessage = string.Empty;
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private bool _hasCompany;
    [ObservableProperty] private string _backupStatus = string.Empty;
    [ObservableProperty] private string _backupLastSuccess = string.Empty;
    [ObservableProperty] private string _backupNextRun = string.Empty;

    [ObservableProperty] private string _internetHealthText = string.Empty;
    [ObservableProperty] private string _databaseHealthText = string.Empty;
    [ObservableProperty] private string _erganiHealthText = string.Empty;
    [ObservableProperty] private string _emailHealthText = string.Empty;
    [ObservableProperty] private string _healthLastCheckedText = string.Empty;
    [ObservableProperty] private int _queuedEmailCount;
    [ObservableProperty] private int _failedEmailCount;
    [ObservableProperty] private string _reconciliationSummary = string.Empty;
    [ObservableProperty] private string _reconciliationLastRun = string.Empty;
    [ObservableProperty] private string _weatherText = string.Empty;

    private static readonly HttpClient DashboardWeatherHttpClient = new() { Timeout = TimeSpan.FromSeconds(8) };

    public bool CanRefresh => !IsLoading;
    public bool HasCategories => Categories.Count > 0;
    public bool NoCategories => Categories.Count == 0;
    public bool HasRecentActivity => RecentActivity.Count > 0;
    public bool NoRecentActivity => RecentActivity.Count == 0;
    public bool HasStatusMessage => !string.IsNullOrWhiteSpace(StatusMessage);

    partial void OnIsLoadingChanged(bool value) => OnPropertyChanged(nameof(CanRefresh));
    partial void OnStatusMessageChanged(string value) => OnPropertyChanged(nameof(HasStatusMessage));

    public ObservableCollection<DashboardCategoryRow> Categories { get; } = new();
    public ObservableCollection<DashboardActivityRow> RecentActivity { get; } = new();

    public DashboardViewModel(
        IConnectionStateService connectionState,
        AutomaticDatabaseBackupService automaticBackup,
        IInternetCheckService internetCheck,
        IErganiHealthCheckService erganiHealth,
        ICredentialProtector credentialProtector,
        DailyReconciliationService reconciliation)
    {
        _connectionState = connectionState;
        _automaticBackup = automaticBackup;
        _internetCheck = internetCheck;
        _erganiHealth = erganiHealth;
        _credentialProtector = credentialProtector;
        _reconciliation = reconciliation;
        PeriodText = Loc["Dashboard.CurrentMonth"];
        RefreshBackupSummary();
    }

    private void RefreshBackupSummary()
    {
        BackupStatus = _automaticBackup.Status;
        BackupLastSuccess = _automaticBackup.LastSuccessLocal is { } last
            ? last.ToString("dd/MM/yyyy HH:mm") : Loc["Dashboard.NotYet"];
        BackupNextRun = _automaticBackup.NextRunLocal is { } next
            ? next.ToString("dd/MM/yyyy HH:mm") : Loc["Dashboard.NotScheduled"];
    }

    public void Initialize(UserSession session)
    {
        _session = session;
        HasCompany = session.CompanyId.HasValue;
        if (session.CompanyId.HasValue)
            _ = RefreshDashboardWeatherAsync(session.CompanyId.Value, session.BranchId);
        _ = RefreshAsync();
    }

    [RelayCommand]
    public async Task RefreshAsync()
    {
        if (IsLoading) return;
        IsLoading = true;
        StatusMessage = string.Empty;
        RefreshBackupSummary();

        try
        {
            var companyId = _session?.CompanyId;
            if (companyId.HasValue)
                _ = RefreshDashboardWeatherAsync(companyId.Value, _session?.BranchId);
            if (!companyId.HasValue)
            {
                CompanyName = Loc["Dashboard.NoCompany"];
                InternetHealthText = DatabaseHealthText = ErganiHealthText = EmailHealthText = Loc["Dashboard.NotConfigured"];
                BranchCount = EmployeeCount = SubmissionCount = ErrorCount = PendingCount = 0;
                SuccessfulCount = FailedCount = 0;
                Categories.Clear();
                RecentActivity.Clear();
                OnPropertyChanged(nameof(HasCategories));
                OnPropertyChanged(nameof(NoCategories));
                OnPropertyChanged(nameof(HasRecentActivity));
                OnPropertyChanged(nameof(NoRecentActivity));
                HasCompany = false;
                WeatherText = string.Empty;
                StatusMessage = Loc["Dashboard.SelectCompany"];
                return;
            }

            HasCompany = true;
            var options = _connectionState.GetDbOptions();
            await using var db = new AppDbContext(options);

            var company = await db.Companies.AsNoTracking()
                .FirstOrDefaultAsync(c => c.Id == companyId.Value);
            CompanyName = company?.Name ?? Loc["Dashboard.UnknownCompany"];
            var reconciliation = _reconciliation.GetLatestReport(companyId.Value);
            if (reconciliation == null)
            {
                ReconciliationSummary = Loc["Dashboard.ReconciliationNotRun"];
                ReconciliationLastRun = string.Empty;
            }
            else
            {
                ReconciliationSummary = string.Format(Loc["Dashboard.ReconciliationSummary"],
                    reconciliation.Findings.Count, reconciliation.UnsubmittedScans, reconciliation.FailedSubmissions);
                ReconciliationLastRun = string.Format(Loc["Dashboard.ReconciliationLastRun"],
                    reconciliation.WorkDate.ToString("dd/MM/yyyy"), reconciliation.GeneratedAtLocal.ToString("dd/MM/yyyy HH:mm"));
            }
            DatabaseHealthText = "✓ " + Loc["Dashboard.Success"];

            var monthStart = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1, 0, 0, 0, DateTimeKind.Utc);
            var nextMonth = monthStart.AddMonths(1);
            var recentStart = DateTime.UtcNow.AddDays(-30);

            BranchCount = await db.Branches.CountAsync(b => b.CompanyId == companyId.Value && b.IsActive);
            EmployeeCount = await db.Employees.CountAsync(e => e.CompanyId == companyId.Value && e.IsActive);

            var companyLogs = db.ApiSubmissionLogs.AsNoTracking()
                .Where(l => l.CompanyId == companyId.Value);

            SubmissionCount = await companyLogs.CountAsync(l => l.SubmissionDate >= monthStart && l.SubmissionDate < nextMonth);
            SuccessfulCount = await companyLogs.CountAsync(l => l.SubmissionDate >= monthStart && l.SubmissionDate < nextMonth && l.Success);
            FailedCount = await companyLogs.CountAsync(l => l.SubmissionDate >= recentStart && !l.Success);
            ErrorCount = FailedCount;

            var pendingSchedules = db.Schedules.Where(s =>
                s.Employee != null && s.Employee.CompanyId == companyId.Value && !s.SubmittedToErgani);
            var pendingOvertimes = db.Overtimes.Where(o =>
                o.Employee != null && o.Employee.CompanyId == companyId.Value && !o.SubmittedToErgani && !o.IsCancelled);
            var pendingLeaves = db.Leaves.Where(l =>
                l.Employee != null && l.Employee.CompanyId == companyId.Value && !l.SubmittedToErgani);
            PendingCount = await pendingSchedules.CountAsync()
                         + await pendingOvertimes.CountAsync()
                         + await pendingLeaves.CountAsync();

            QueuedEmailCount = await db.EmailLogs.CountAsync(e =>
                e.CompanyId == companyId.Value && e.Status == "Queued");
            FailedEmailCount = await db.EmailLogs.CountAsync(e =>
                e.CompanyId == companyId.Value && e.Status == "Failed");
            EmailHealthText = QueuedEmailCount == 0 && FailedEmailCount == 0
                ? "✓ " + Loc["Dashboard.Success"]
                : string.Format(Loc["Dashboard.EmailQueueSummary"], QueuedEmailCount, FailedEmailCount);

            Categories.Clear();
            var categoryCounts = await companyLogs
                .Where(l => l.SubmissionDate >= monthStart && l.SubmissionDate < nextMonth)
                .GroupBy(l => l.SubmissionType)
                .Select(g => new { Type = g.Key, Total = g.Count(), Successful = g.Count(l => l.Success) })
                .OrderByDescending(g => g.Total)
                .ToListAsync();

            var max = Math.Max(1, categoryCounts.Select(x => x.Total).DefaultIfEmpty(0).Max());
            foreach (var item in categoryCounts)
            {
                Categories.Add(new DashboardCategoryRow
                {
                    Name = FormatSubmissionType(item.Type),
                    Total = item.Total,
                    Successful = item.Successful,
                    Failed = item.Total - item.Successful,
                    MaxCount = max
                });
            }

            OnPropertyChanged(nameof(HasCategories));
            OnPropertyChanged(nameof(NoCategories));

            RecentActivity.Clear();
            var latest = await companyLogs
                .OrderByDescending(l => l.SubmissionDate)
                .Take(8)
                .Select(l => new
                {
                    l.SubmissionDate,
                    l.SubmissionType,
                    l.Success,
                    l.Protocol,
                    l.ErrorMessage,
                    l.HttpStatusCode,
                    l.EmployeeId,
                    l.EmployeeIdsJson,
                    l.WorkCardId,
                    l.ScheduleDate
                })
                .ToListAsync();

            // Resolve employee names for both single-employee and multi-employee submission batches.
            var employeeIds = new HashSet<int>();
            var workCardIds = latest.Where(x => x.WorkCardId.HasValue)
                .Select(x => x.WorkCardId!.Value).Distinct().ToList();

            foreach (var item in latest)
            {
                if (item.EmployeeId.HasValue)
                    employeeIds.Add(item.EmployeeId.Value);

                if (!string.IsNullOrWhiteSpace(item.EmployeeIdsJson))
                {
                    try
                    {
                        var ids = JsonSerializer.Deserialize<List<int>>(item.EmployeeIdsJson);
                        if (ids != null)
                            foreach (var id in ids)
                                employeeIds.Add(id);
                    }
                    catch (JsonException)
                    {
                        // Older or malformed historical records remain visible without employee names.
                    }
                }
            }

            var workCards = workCardIds.Count == 0
                ? new Dictionary<int, WorkCard>()
                : await db.WorkCards.AsNoTracking()
                    .Where(w => workCardIds.Contains(w.Id))
                    .ToDictionaryAsync(w => w.Id);

            foreach (var card in workCards.Values)
                employeeIds.Add(card.EmployeeId);

            var employeeNames = employeeIds.Count == 0
                ? new Dictionary<int, string>()
                : await db.Employees.AsNoTracking()
                    .Where(e => e.CompanyId == companyId.Value && employeeIds.Contains(e.Id))
                    .ToDictionaryAsync(e => e.Id, e => e.FullName);

            foreach (var item in latest)
            {
                var names = new List<string>();
                if (item.EmployeeId.HasValue && employeeNames.TryGetValue(item.EmployeeId.Value, out var singleName))
                    names.Add(singleName);

                if (!string.IsNullOrWhiteSpace(item.EmployeeIdsJson))
                {
                    try
                    {
                        var ids = JsonSerializer.Deserialize<List<int>>(item.EmployeeIdsJson);
                        if (ids != null)
                        {
                            foreach (var id in ids)
                            {
                                if (employeeNames.TryGetValue(id, out var name) && !names.Contains(name))
                                    names.Add(name);
                            }
                        }
                    }
                    catch (JsonException)
                    {
                        // Keep showing the rest of the submission information for legacy rows.
                    }
                }

                WorkCard? workCard = null;
                if (item.WorkCardId.HasValue)
                    workCards.TryGetValue(item.WorkCardId.Value, out workCard);

                if (workCard != null && employeeNames.TryGetValue(workCard.EmployeeId, out var cardEmployeeName)
                    && !names.Contains(cardEmployeeName))
                    names.Add(cardEmployeeName);

                var subjectParts = new List<string>();
                if (names.Count > 0)
                    subjectParts.Add(string.Format(Loc["Dashboard.SubjectEmployees"], string.Join(", ", names)));
                else
                    subjectParts.Add(Loc["Dashboard.SubjectEmployeeUnknown"]);

                if (item.ScheduleDate.HasValue)
                    subjectParts.Add(string.Format(Loc["Dashboard.SubjectDate"], item.ScheduleDate.Value.ToString("dd/MM/yyyy")));

                if (workCard != null)
                {
                    var movement = workCard.MovementType == MovementType.Arrival
                        ? Loc["Dashboard.WorkCard.Arrival"]
                        : Loc["Dashboard.WorkCard.Departure"];
                    subjectParts.Add(string.Format(Loc["Dashboard.WorkCardEvent"], movement,
                        workCard.MovementDateTime.ToLocalTime().ToString("dd/MM/yyyy HH:mm")));
                }

                RecentActivity.Add(new DashboardActivityRow
                {
                    DateText = item.SubmissionDate.ToLocalTime().ToString("dd/MM/yyyy HH:mm"),
                    TypeText = FormatSubmissionType(item.SubmissionType),
                    SubjectText = string.Join(" · ", subjectParts),
                    StatusText = item.Success ? "✓ " + Loc["Dashboard.Success"] : "✕ " + Loc["Dashboard.Failed"],
                    DetailText = item.Success
                        ? (!string.IsNullOrWhiteSpace(item.Protocol)
                            ? string.Format(Loc["Dashboard.Protocol"], item.Protocol)
                            : Loc["Dashboard.SubmissionCompleted"])
                        : (item.ErrorMessage ?? (item.HttpStatusCode.HasValue
                            ? string.Format(Loc["Dashboard.HttpError"], item.HttpStatusCode.Value)
                            : Loc["Dashboard.NoErrorDetails"])),
                    IsSuccess = item.Success
                });
            }

            OnPropertyChanged(nameof(HasRecentActivity));
            OnPropertyChanged(nameof(NoRecentActivity));
            try
            {
                var internetOnline = await _internetCheck.IsOnlineAsync();
                InternetHealthText = internetOnline
                    ? "✓ " + Loc["Dashboard.Success"]
                    : "✕ " + Loc["Dashboard.Failed"];
            }
            catch (Exception ex)
            {
                InternetHealthText = "✕ " + Loc["Dashboard.Failed"];
                Serilog.Log.Warning(ex, "Dashboard internet health check failed.");
            }

            if (company == null || string.IsNullOrWhiteSpace(company.ErganiUsername)
                || string.IsNullOrWhiteSpace(company.ErganiPasswordEncrypted)
                || string.IsNullOrWhiteSpace(company.ErganiBaseUrl))
            {
                ErganiHealthText = Loc["Dashboard.NotConfigured"];
            }
            else
            {
                try
                {
                    var credentials = new ErganiCredentials
                    {
                        Username = company.ErganiUsername,
                        Password = _credentialProtector.Unprotect(company.ErganiPasswordEncrypted),
                        BaseUrl = company.ErganiBaseUrl
                    };
                    var erganiStatus = await _erganiHealth.CheckAsync(credentials);
                    ErganiHealthText = erganiStatus == ErganiServiceStatus.Online
                        ? "✓ " + Loc["Dashboard.Success"]
                        : "✕ " + Loc["Dashboard.Failed"];
                }
                catch (Exception ex)
                {
                    ErganiHealthText = "✕ " + Loc["Dashboard.Failed"];
                    Serilog.Log.Warning(ex, "Dashboard Ergani health check failed.");
                }
            }

            HealthLastCheckedText = string.Format(
                Loc["Dashboard.HealthLastChecked"], DateTime.Now.ToString("dd/MM/yyyy HH:mm:ss"));
            LastUpdatedText = string.Format(Loc["Dashboard.LastUpdated"], DateTime.Now.ToString("dd/MM/yyyy HH:mm:ss"));
            if (Categories.Count == 0)
                StatusMessage = Loc["Dashboard.NoSubmissionsInPeriod"];
        }
        catch (Exception ex)
        {
            DatabaseHealthText = "✕ " + Loc["Dashboard.Failed"];
            ErganiHealthText = EmailHealthText = Loc["Dashboard.NotChecked"];
            try
            {
                var internetOnline = await _internetCheck.IsOnlineAsync();
                InternetHealthText = internetOnline
                    ? "✓ " + Loc["Dashboard.Success"]
                    : "✕ " + Loc["Dashboard.Failed"];
            }
            catch
            {
                InternetHealthText = Loc["Dashboard.NotChecked"];
            }
            HealthLastCheckedText = string.Format(
                Loc["Dashboard.HealthLastChecked"], DateTime.Now.ToString("dd/MM/yyyy HH:mm:ss"));
            StatusMessage = string.Format(Loc["Dashboard.LoadFailed"], ex.Message);
            Serilog.Log.Error(ex, "Failed to load company dashboard for company {CompanyId}", _session?.CompanyId);
        }
        finally
        {
            IsLoading = false;
        }
    }

    private string FormatSubmissionType(string? type)
    {
        var key = (type ?? string.Empty).Trim().ToLowerInvariant();
        return key switch
        {
            "dailyschedule" or "schedule" or "schedules" or "wtos" => Loc["Dashboard.Type.Schedules"],
            "overtime" or "wto" or "wtoov" => Loc["Dashboard.Type.Overtime"],
            "workcard" or "workcards" or "wrkcardse" or "workcardse" => Loc["Dashboard.Type.WorkCards"],
            "leave" or "holiday" or "holidays" or "wtoleave" => Loc["Dashboard.Type.Leaves"],
            "" => Loc["Dashboard.Type.Other"],
            _ => type!
        };
    }
}

public sealed class DashboardCategoryRow
{
    public string Name { get; set; } = string.Empty;
    public int Total { get; set; }
    public int Successful { get; set; }
    public string SuccessfulText => $"✓ {Successful}";
    public int Failed { get; set; }
    public bool HasFailures => Failed > 0;
    public string FailedText => $"✕ {Failed}";
    public int MaxCount { get; set; }
}

public sealed class DashboardActivityRow
{
    public string DateText { get; set; } = string.Empty;
    public string TypeText { get; set; } = string.Empty;
    public string SubjectText { get; set; } = string.Empty;
    public string StatusText { get; set; } = string.Empty;
    public string DetailText { get; set; } = string.Empty;
    public bool IsSuccess { get; set; }
    /// <summary>Loads the same branch-configured Open-Meteo summary used by the terminal.</summary>
    private async Task RefreshDashboardWeatherAsync(int companyId, int? branchId)
    {
        try
        {
            await using var db = new AppDbContext(_connectionState.GetDbOptions());
            var branch = branchId.HasValue
                ? await db.Branches.AsNoTracking().FirstOrDefaultAsync(b => b.Id == branchId.Value && b.CompanyId == companyId)
                : null;

            bool HasLocation(BusinessBranch b) =>
                (b.WeatherLatitude.HasValue && b.WeatherLongitude.HasValue) ||
                !string.IsNullOrWhiteSpace(b.WeatherManualAddress) ||
                !string.IsNullOrWhiteSpace(b.Address);

            if (branch == null || !HasLocation(branch))
            {
                var branches = await db.Branches.AsNoTracking()
                    .Where(b => b.CompanyId == companyId && b.IsActive)
                    .OrderBy(b => b.BranchNumber).ToListAsync();
                branch = branches.FirstOrDefault(HasLocation);
            }
            if (branch == null) { WeatherText = string.Empty; return; }

            double? latitude = branch.WeatherLatitude;
            double? longitude = branch.WeatherLongitude;
            string? placeName = branch.WeatherShowLocation ? branch.Name : null;
            var address = !string.IsNullOrWhiteSpace(branch.WeatherManualAddress)
                ? branch.WeatherManualAddress!.Trim() : branch.Address?.Trim();

            if ((!latitude.HasValue || !longitude.HasValue) && !string.IsNullOrWhiteSpace(address))
            {
                var candidates = new[] { address }.Concat(address!.Split(new[] { ',', ';', '\n' },
                    StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Reverse());
                foreach (var candidate in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    var url = "https://geocoding-api.open-meteo.com/v1/search?name=" +
                        Uri.EscapeDataString(candidate) + "&count=5&language=en&format=json&countryCode=GR";
                    using var geoResponse = await DashboardWeatherHttpClient.GetAsync(url);
                    if (!geoResponse.IsSuccessStatusCode) continue;
                    using var geoJson = JsonDocument.Parse(await geoResponse.Content.ReadAsStringAsync());
                    if (!geoJson.RootElement.TryGetProperty("results", out var results) || results.GetArrayLength() == 0) continue;
                    var selected = results.EnumerateArray().FirstOrDefault(item =>
                        item.TryGetProperty("country_code", out var country) &&
                        string.Equals(country.GetString(), "GR", StringComparison.OrdinalIgnoreCase));
                    if (selected.ValueKind == JsonValueKind.Undefined) selected = results[0];
                    latitude = selected.GetProperty("latitude").GetDouble();
                    longitude = selected.GetProperty("longitude").GetDouble();
                    if (branch.WeatherShowLocation && selected.TryGetProperty("name", out var name))
                        placeName = name.GetString();
                    break;
                }
            }
            if (!latitude.HasValue || !longitude.HasValue) { WeatherText = string.Empty; return; }

            var weatherUrl = $"https://api.open-meteo.com/v1/forecast?latitude={latitude.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)}&longitude={longitude.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)}&current=temperature_2m,relative_humidity_2m,apparent_temperature,weather_code,wind_speed_10m&timezone=auto";
            using var response = await DashboardWeatherHttpClient.GetAsync(weatherUrl);
            response.EnsureSuccessStatusCode();
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var current = json.RootElement.GetProperty("current");
            var temp = current.GetProperty("temperature_2m").GetDouble();
            var feels = current.GetProperty("apparent_temperature").GetDouble();
            var humidity = current.GetProperty("relative_humidity_2m").GetInt32();
            var wind = current.GetProperty("wind_speed_10m").GetDouble();
            var code = current.GetProperty("weather_code").GetInt32();
            var condition = code switch
            {
                0 => "Clear", 1 or 2 => "Partly cloudy", 3 => "Overcast",
                45 or 48 => "Fog", 51 or 53 or 55 or 56 or 57 => "Drizzle",
                61 or 63 or 65 or 66 or 67 => "Rain", 71 or 73 or 75 or 77 => "Snow",
                80 or 81 or 82 => "Showers", 85 or 86 => "Snow showers",
                95 or 96 or 99 => "Thunderstorm", _ => "Weather"
            };
            var icon = code switch
            {
                0 => "☀", 1 or 2 => "🌤", 3 => "☁", 45 or 48 => "🌫",
                51 or 53 or 55 or 56 or 57 => "🌦",
                61 or 63 or 65 or 66 or 67 or 80 or 81 or 82 => "🌧",
                71 or 73 or 75 or 77 or 85 or 86 => "❄", _ => "⛈"
            };
            var parts = new List<string>();
            if (branch.WeatherShowLocation && !string.IsNullOrWhiteSpace(placeName ?? branch.Name))
                parts.Add($"📍 {placeName ?? branch.Name}");
            if (branch.WeatherShowTemperature) parts.Add($"🌡️ {temp:0}°C");
            if (branch.WeatherShowCondition) parts.Add($"{icon} {condition}");
            if (branch.WeatherShowFeelsLike) parts.Add($"🥵 {feels:0}°");
            if (branch.WeatherShowHumidity) parts.Add($"💧 {humidity}%");
            if (branch.WeatherShowWind) parts.Add($"💨 {wind:0} km/h");
            WeatherText = parts.Count == 0 ? string.Empty : string.Join("  ·  ", parts);
        }
        catch (Exception ex)
        {
            Serilog.Log.Warning(ex, "Could not load dashboard weather for company {CompanyId}", companyId);
        }
    }

}
