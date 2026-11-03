using System.Net;
using System.Net.Http.Json;

namespace ErganiManager.Android;

public sealed class MobileApiClient : IDisposable
{
    private readonly HttpClient _http;

    public MobileApiClient(string baseAddress)
    {
        if (!Uri.TryCreate(baseAddress.Trim().TrimEnd('/') + "/", UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            throw new InvalidOperationException("Enter a valid HTTPS address for the mobile API, for example https://192.168.1.61:5001.");

        var cookies = new CookieContainer();
        var handler = new HttpClientHandler { CookieContainer = cookies, UseCookies = true };
        _http = new HttpClient(handler) { BaseAddress = uri, Timeout = TimeSpan.FromSeconds(20) };
    }

    public async Task CheckHealthAsync(CancellationToken cancellationToken = default)
    {
        using var response = await _http.GetAsync("api/mobile/health", cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new HttpRequestException(
                $"The API server responded with HTTP {(int)response.StatusCode} ({response.ReasonPhrase}). {body}".Trim());
        }
    }

    public async Task<MobileLoginResponse> LoginAsync(string username, string password, CancellationToken cancellationToken = default)
    {
        using var response = await _http.PostAsJsonAsync("api/mobile/login", new { username, password }, cancellationToken);
        if (response.StatusCode == HttpStatusCode.Unauthorized) throw new InvalidOperationException("The API was reached, but the username or password was rejected.");
        if (response.StatusCode == HttpStatusCode.ServiceUnavailable) throw new InvalidOperationException("The API was reached, but the server database is unavailable. Mobile login requires an online connection.");
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new HttpRequestException(
                $"Login endpoint responded with HTTP {(int)response.StatusCode} ({response.ReasonPhrase}). {body}".Trim());
        }
        return await response.Content.ReadFromJsonAsync<MobileLoginResponse>(cancellationToken: cancellationToken)
            ?? throw new InvalidOperationException("The server returned an empty login response.");
    }

    public async Task<List<MobileEmployee>> GetEmployeesAsync(CancellationToken cancellationToken = default)
    {
        using var response = await _http.GetAsync("api/mobile/employees", cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<List<MobileEmployee>>(cancellationToken: cancellationToken) ?? [];
    }

    public async Task<List<MobileTodayEmployeeStatus>> GetTodayStatusAsync(DateOnly date, int graceMinutes, CancellationToken cancellationToken = default)
    {
        var url = "api/mobile/today?date=" + date.ToString("yyyy-MM-dd") + "&graceMinutes=" + graceMinutes;
        using var response = await _http.GetAsync(url, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new HttpRequestException("Daily Status returned HTTP " + (int)response.StatusCode + ": " + body);
        }
        return await response.Content.ReadFromJsonAsync<List<MobileTodayEmployeeStatus>>(cancellationToken: cancellationToken) ?? [];
    }

    public async Task LogoutAsync(CancellationToken cancellationToken = default)
    {
        using var response = await _http.PostAsync("api/mobile/logout", content: null, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public void Dispose() => _http.Dispose();
}

public sealed record MobileLoginResponse(string Username, string Role, int? CompanyId, string? CompanyName, int? BranchId, string? BranchName);
public sealed record MobileEmployee(int Id, int BranchId, string FirstName, string LastName, bool IsActive);


public sealed class MobileTodayEmployeeStatus
{
    public int EmployeeId { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string BranchName { get; set; } = string.Empty;
    public int ScheduleKind { get; set; }
    public TimeOnly? PlannedStart { get; set; }
    public TimeOnly? PlannedEnd { get; set; }
    public bool IsHome { get; set; }
    public string? LeaveName { get; set; }
    public TimeOnly? ArrivalTime { get; set; }
    public TimeOnly? DepartureTime { get; set; }
    public int Presence { get; set; }
    public int Level { get; set; }
    public bool IsOfficialHoliday { get; set; }
    public List<MobileTodayIssue> Issues { get; set; } = new();

    public string PresenceText => Presence switch
    {
        1 => "Working", 2 => "Left", 3 => "On leave", 4 => "Not arrived",
        5 => "Expected", 6 => "Not working", _ => "No status"
    };
    public string ScheduleText => ScheduleKind switch
    {
        1 => IsHome ? "Work · Home" : "Work", 2 => "Rest day", 3 => "Absent",
        4 => LeaveName ?? "Leave", _ => IsOfficialHoliday ? "Official holiday" : "No schedule"
    };
    public string PlannedHours => (PlannedStart?.ToString("HH:mm") ?? "—") + " – " + (PlannedEnd?.ToString("HH:mm") ?? "—");
    public string ClockingText => "In: " + (ArrivalTime?.ToString("HH:mm") ?? "—") + "   Out: " + (DepartureTime?.ToString("HH:mm") ?? "—");
    public string LevelText => Level switch { 2 => "Error", 1 => "Warning", _ => "OK" };
    public string IssueSummary => Issues.Count == 0 ? "No issues" : Issues.Count + " issue(s)";
}
public sealed class MobileTodayIssue
{
    public int Code { get; set; }
    public int Level { get; set; }
    public string? Detail { get; set; }
}
