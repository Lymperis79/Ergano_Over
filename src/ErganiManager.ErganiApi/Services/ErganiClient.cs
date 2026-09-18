using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;
using ErganiManager.ErganiApi.Models;
using Microsoft.Extensions.Logging;

namespace ErganiManager.ErganiApi.Services;

/// <summary>
/// HTTP client for the Ergani Web Services API. Uses Basic Auth on every
/// request (no separate login/token call). Every call returns a full
/// ErganiCallResult capturing the request payload and raw response, success
/// or failure, so the caller can persist it to ApiSubmissionLog for audit
/// purposes regardless of outcome.
///
/// NOTE ON ENDPOINT PATHS: see ErganiEndpoints.cs in this project — that is
/// the single file to edit if Ergani's API paths or base URL ever change.
/// Nothing in this class hardcodes a path string.
/// </summary>
public class ErganiClient : IErganiClient
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<ErganiClient> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = null, // we control names explicitly via [JsonPropertyName]
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    public ErganiClient(IHttpClientFactory httpClientFactory, ILogger<ErganiClient> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public Task<ErganiCallResult<List<ErganiSubmissionResponse>>> SubmitWorkCardAsync(
        ErganiCredentials credentials, List<CompanyWorkCardSubmission> submissions, CancellationToken ct = default)
        => PostAsync(credentials, ErganiEndpoints.WorkCardSubmitPath, submissions, ct);

    public Task<ErganiCallResult<List<ErganiSubmissionResponse>>> SubmitDailyScheduleAsync(
        ErganiCredentials credentials, List<CompanyDailyScheduleSubmission> submissions, CancellationToken ct = default)
        => PostAsync(credentials, ErganiEndpoints.DailyScheduleSubmitPath, submissions, ct);

    public Task<ErganiCallResult<List<ErganiSubmissionResponse>>> SubmitWeeklyScheduleAsync(
        ErganiCredentials credentials, List<CompanyWeeklyScheduleSubmission> submissions, CancellationToken ct = default)
        => PostAsync(credentials, ErganiEndpoints.WeeklyScheduleSubmitPath, submissions, ct);

    public Task<ErganiCallResult<List<ErganiSubmissionResponse>>> SubmitOvertimeAsync(
        ErganiCredentials credentials, List<CompanyOvertimeSubmission> submissions, CancellationToken ct = default)
        => PostAsync(credentials, ErganiEndpoints.OvertimeSubmitPath, submissions, ct);

    private const int MaxRetries = 3;
    private static readonly TimeSpan[] RetryDelays =
    {
        TimeSpan.FromSeconds(5),
        TimeSpan.FromSeconds(15),
        TimeSpan.FromSeconds(30)
    };

    private async Task<ErganiCallResult<List<ErganiSubmissionResponse>>> PostAsync<TPayload>(
        ErganiCredentials credentials,
        string relativePath,
        TPayload payload,
        CancellationToken ct)
    {
        var result = new ErganiCallResult<List<ErganiSubmissionResponse>>();
        var requestJson = JsonSerializer.Serialize(payload, JsonOptions);
        result.RequestPayloadJson = requestJson;

        var stopwatch = Stopwatch.StartNew();

        for (int attempt = 1; attempt <= MaxRetries; attempt++)
        {
            try
            {
                var client = BuildAuthenticatedClient(credentials);
                using var content = new StringContent(requestJson, Encoding.UTF8, "application/json");
                using var response = await client.PostAsync(relativePath, content, ct);

                var responseBody = await response.Content.ReadAsStringAsync(ct);
                result.ResponseRawJson = responseBody;
                result.HttpStatusCode = (int)response.StatusCode;

                if (response.StatusCode == HttpStatusCode.Unauthorized)
                {
                    result.Success = false;
                    result.ErrorMessage = "Ergani rejected the credentials (401). Check username/password in Settings.";
                    _logger.LogWarning("Ergani 401 for {Path}, user {U}.", relativePath, credentials.Username);
                    break;
                }

                if ((int)response.StatusCode >= 500 || response.StatusCode == HttpStatusCode.ServiceUnavailable)
                {
                    result.IsServiceUnavailable = true;
                    result.ErrorMessage = $"Ergani service error: HTTP {(int)response.StatusCode}. Attempt {attempt}/{MaxRetries}.";
                    _logger.LogWarning("Ergani 5xx on attempt {A}/{M}: {S}", attempt, MaxRetries, response.StatusCode);
                    if (attempt < MaxRetries) { await Task.Delay(RetryDelays[attempt - 1], ct); continue; }
                    result.Success = false;
                    break;
                }

                if (!response.IsSuccessStatusCode)
                {
                    result.Success = false;
                    result.ErrorMessage = $"Ergani returned {(int)response.StatusCode}.";
                    break;
                }

                try
                {
                    var responses = JsonSerializer.Deserialize<List<ErganiSubmissionResponse>>(responseBody, JsonOptions)
                                    ?? new List<ErganiSubmissionResponse>();
                    var first = responses.FirstOrDefault();

                    if (first != null && first.IsBusinessError)
                    {
                        result.Success = false;
                        result.IsBusinessError = true;
                        result.BusinessErrorDescription = first.Description;
                        result.Data = responses;
                        result.ErrorMessage = $"Ergani business error: {first.Description}";
                        _logger.LogWarning("Ergani business error on attempt {A}/{M}: {D}", attempt, MaxRetries, first.Description);
                        if (attempt < MaxRetries) { await Task.Delay(RetryDelays[attempt - 1], ct); continue; }
                        result.IsServiceUnavailable = true;
                        break;
                    }

                    result.Data = responses;
                    result.Success = true;
                    _logger.LogInformation("Ergani success on attempt {A}/{M}. Protocol: {P}",
                        attempt, MaxRetries, first?.Protocol);
                    break;
                }
                catch (JsonException jex)
                {
                    result.Success = false;
                    result.ErrorMessage = "Could not parse Ergani response body.";
                    _logger.LogError(jex, "Parse error for {Path}: {B}", relativePath, responseBody);
                    break;
                }
            }
            catch (TaskCanceledException) when (ct.IsCancellationRequested)
            {
                result.Success = false;
                result.ErrorMessage = "Request cancelled.";
                break;
            }
            catch (TaskCanceledException)
            {
                result.IsServiceUnavailable = true;
                result.ErrorMessage = $"Timeout. Attempt {attempt}/{MaxRetries}.";
                if (attempt < MaxRetries) { await Task.Delay(RetryDelays[attempt - 1], ct); continue; }
                result.Success = false;
                break;
            }
            catch (HttpRequestException hEx)
            {
                result.IsServiceUnavailable = true;
                result.ErrorMessage = $"Network error: {hEx.Message}. Attempt {attempt}/{MaxRetries}.";
                _logger.LogWarning(hEx, "Network error {Path} attempt {A}/{M}.", relativePath, attempt, MaxRetries);
                if (attempt < MaxRetries) { await Task.Delay(RetryDelays[attempt - 1], ct); continue; }
                result.Success = false;
                break;
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.ErrorMessage = $"Unexpected error: {ex.Message}";
                _logger.LogError(ex, "Unexpected error at {Path}.", relativePath);
                break;
            }
        }

        stopwatch.Stop();
        result.DurationMs = stopwatch.ElapsedMilliseconds;
        return result;
    }

        private HttpClient BuildAuthenticatedClient(ErganiCredentials credentials)
    {
        var client = _httpClientFactory.CreateClient("ErganiApi");
        client.BaseAddress = new Uri(credentials.BaseUrl.TrimEnd('/') + "/");

        var raw = $"{credentials.Username}:{credentials.Password}";
        var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(raw));
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", encoded);

        return client;
    }
}
