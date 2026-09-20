using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ErganiManager.ErganiApi.Models;
using Microsoft.Extensions.Logging;

namespace ErganiManager.ErganiApi.Services;

/// <summary>
/// HTTP client for the Ergani REST API.
/// Authentication: JWT Bearer token obtained from POST /Authentication.
/// Tokens are cached per (BaseUrl + Username) key and refreshed automatically.
/// </summary>
public class ErganiClient : IErganiClient
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<ErganiClient> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    private static readonly ConcurrentDictionary<string, ErganiTokenCache> _tokenCache = new();

    private const int MaxRetries = 3;
    private static readonly TimeSpan[] RetryDelays =
    {
        TimeSpan.FromSeconds(5),
        TimeSpan.FromSeconds(15),
        TimeSpan.FromSeconds(30)
    };

    public ErganiClient(IHttpClientFactory httpClientFactory, ILogger<ErganiClient> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    // ── Public submission methods ─────────────────────────────────────────

    public Task<ErganiCallResult<List<ErganiSubmissionResponse>>> SubmitWorkCardAsync(
        ErganiCredentials credentials, WorkCardSubmissionEnvelope envelope,
        CancellationToken ct = default)
        => PostAsync<WorkCardSubmissionEnvelope, List<ErganiSubmissionResponse>>(
            credentials, ErganiEndpoints.WorkCardSubmitPath, envelope, ct);

    public Task<ErganiCallResult<List<ErganiSubmissionResponse>>> SubmitDailyScheduleAsync(
        ErganiCredentials credentials, List<CompanyDailyScheduleSubmission> submissions,
        CancellationToken ct = default)
        => PostAsync<List<CompanyDailyScheduleSubmission>, List<ErganiSubmissionResponse>>(
            credentials, ErganiEndpoints.DailyScheduleSubmitPath, submissions, ct);

    public Task<ErganiCallResult<List<ErganiSubmissionResponse>>> SubmitWeeklyScheduleAsync(
        ErganiCredentials credentials, List<CompanyWeeklyScheduleSubmission> submissions,
        CancellationToken ct = default)
        => PostAsync<List<CompanyWeeklyScheduleSubmission>, List<ErganiSubmissionResponse>>(
            credentials, ErganiEndpoints.WeeklyScheduleSubmitPath, submissions, ct);

    public Task<ErganiCallResult<List<ErganiSubmissionResponse>>> SubmitOvertimeAsync(
        ErganiCredentials credentials, List<CompanyOvertimeSubmission> submissions,
        CancellationToken ct = default)
        => PostAsync<List<CompanyOvertimeSubmission>, List<ErganiSubmissionResponse>>(
            credentials, ErganiEndpoints.OvertimeSubmitPath, submissions, ct);

    // ── Authentication ────────────────────────────────────────────────────

    public async Task<ErganiAuthResponse?> AuthenticateAsync(
        ErganiCredentials credentials, CancellationToken ct = default)
    {
        var client = BuildClient(credentials.BaseUrl);
        var body   = JsonSerializer.Serialize(new ErganiAuthRequest
        {
            Username = credentials.Username,
            Password = credentials.Password,
            Usertype = ErganiEndpoints.UsertypeErgani
        }, JsonOptions);

        using var content = new StringContent(body, Encoding.UTF8, "application/json");
        var response = await client.PostAsync(ErganiEndpoints.AuthPath, content, ct);
        if (!response.IsSuccessStatusCode) return null;

        var json = await response.Content.ReadAsStringAsync(ct);
        return JsonSerializer.Deserialize<ErganiAuthResponse>(json, JsonOptions);
    }

    public async Task<bool> TestCredentialsAsync(
        ErganiCredentials credentials, CancellationToken ct = default)
    {
        try
        {
            var token = await AuthenticateAsync(credentials, ct);
            return token != null && !string.IsNullOrEmpty(token.AccessToken);
        }
        catch { return false; }
    }

    // ── Data retrieval (WebServices) ──────────────────────────────────────

    public async Task<List<ErganiServiceDefinition>> GetServicesListAsync(
        ErganiCredentials credentials, CancellationToken ct = default)
    {
        var token  = await GetOrRefreshTokenAsync(credentials, ct);
        var client = BuildAuthenticatedClient(credentials.BaseUrl, token);
        var resp   = await client.GetAsync(ErganiEndpoints.ServicesListPath, ct);
        resp.EnsureSuccessStatusCode();
        var json = await resp.Content.ReadAsStringAsync(ct);
        return JsonSerializer.Deserialize<List<ErganiServiceDefinition>>(json, JsonOptions) ?? new();
    }

    public async Task<JsonDocument> ExecuteServiceAsync(
        ErganiCredentials credentials, string serviceCode,
        List<ErganiServiceParameterValue>? parameters = null,
        CancellationToken ct = default)
    {
        var token  = await GetOrRefreshTokenAsync(credentials, ct);
        var client = BuildAuthenticatedClient(credentials.BaseUrl, token);

        var req = new ErganiExecuteServiceRequest
        {
            ServiceCode = serviceCode,
            Parameters  = parameters ?? new()
        };

        using var content = new StringContent(
            JsonSerializer.Serialize(req, JsonOptions), Encoding.UTF8, "application/json");

        var resp = await client.PostAsync(ErganiEndpoints.ExecuteServicePath, content, ct);
        resp.EnsureSuccessStatusCode();
        var json = await resp.Content.ReadAsStringAsync(ct);
        return JsonDocument.Parse(json);
    }

    public async Task<List<ErganiSubmissionType>> GetSubmissionsLookupAsync(
        ErganiCredentials credentials, CancellationToken ct = default)
    {
        var token  = await GetOrRefreshTokenAsync(credentials, ct);
        var client = BuildAuthenticatedClient(credentials.BaseUrl, token);
        var resp   = await client.GetAsync(ErganiEndpoints.SubmissionsLookupPath, ct);
        resp.EnsureSuccessStatusCode();
        var json = await resp.Content.ReadAsStringAsync(ct);
        return JsonSerializer.Deserialize<List<ErganiSubmissionType>>(json, JsonOptions) ?? new();
    }

    // ── Token management ──────────────────────────────────────────────────

    private async Task<string> GetOrRefreshTokenAsync(
        ErganiCredentials credentials, CancellationToken ct)
    {
        var key = $"{credentials.BaseUrl}|{credentials.Username}";

        if (_tokenCache.TryGetValue(key, out var cached) && cached.IsAccessTokenValid)
            return cached.AccessToken;

        if (cached != null && cached.IsRefreshTokenValid)
        {
            try
            {
                var client  = BuildClient(credentials.BaseUrl);
                var reqBody = JsonSerializer.Serialize(new ErganiRefreshRequest
                {
                    AccessToken  = cached.AccessToken,
                    RefreshToken = cached.RefreshToken
                }, JsonOptions);

                using var content = new StringContent(reqBody, Encoding.UTF8, "application/json");
                var resp = await client.PostAsync(ErganiEndpoints.AuthRefreshPath, content, ct);

                if (resp.IsSuccessStatusCode)
                {
                    var json   = await resp.Content.ReadAsStringAsync(ct);
                    var newAuth = JsonSerializer.Deserialize<ErganiAuthResponse>(json, JsonOptions);
                    if (newAuth != null)
                    {
                        var newCache = ToTokenCache(newAuth);
                        _tokenCache[key] = newCache;
                        return newCache.AccessToken;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Ergani token refresh failed — re-authenticating.");
            }
        }

        // Full re-authentication
        var auth = await AuthenticateAsync(credentials, ct)
            ?? throw new InvalidOperationException(
                "Ergani authentication failed. Check credentials in Company settings.");

        var tokenCache = ToTokenCache(auth);
        _tokenCache[key] = tokenCache;
        return tokenCache.AccessToken;
    }

    private static ErganiTokenCache ToTokenCache(ErganiAuthResponse auth) => new()
    {
        AccessToken       = auth.AccessToken,
        RefreshToken      = auth.RefreshToken,
        ExpiresAt         = DateTimeOffset.UtcNow.AddSeconds(auth.AccessTokenExpiredSeconds),
        RefreshExpiresAt  = auth.RefreshTokenExpired
    };

    // ── Core POST with retry ──────────────────────────────────────────────

    private async Task<ErganiCallResult<TResponse>> PostAsync<TPayload, TResponse>(
        ErganiCredentials credentials, string path,
        TPayload payload, CancellationToken ct)
    {
        var result     = new ErganiCallResult<TResponse>();
        var requestJson = JsonSerializer.Serialize(payload, JsonOptions);
        result.RequestPayloadJson = requestJson;
        var stopwatch  = Stopwatch.StartNew();

        for (int attempt = 1; attempt <= MaxRetries; attempt++)
        {
            try
            {
                var token  = await GetOrRefreshTokenAsync(credentials, ct);
                var client = BuildAuthenticatedClient(credentials.BaseUrl, token);

                using var content = new StringContent(requestJson, Encoding.UTF8, "application/json");
                using var response = await client.PostAsync(path, content, ct);

                var responseBody = await response.Content.ReadAsStringAsync(ct);
                result.ResponseRawJson = responseBody;
                result.HttpStatusCode  = (int)response.StatusCode;

                if (response.StatusCode == HttpStatusCode.Unauthorized)
                {
                    // Token expired mid-flight — invalidate and retry once
                    var key = $"{credentials.BaseUrl}|{credentials.Username}";
                    _tokenCache.TryRemove(key, out _);
                    if (attempt < MaxRetries) { await Task.Delay(RetryDelays[attempt - 1], ct); continue; }
                    result.Success = false;
                    result.ErrorMessage = "Ergani rejected the token (401). Credentials may have changed.";
                    break;
                }

                if ((int)response.StatusCode >= 500)
                {
                    result.IsServiceUnavailable = true;
                    result.ErrorMessage = $"Ergani service error HTTP {(int)response.StatusCode}. Attempt {attempt}/{MaxRetries}.";
                    if (attempt < MaxRetries) { await Task.Delay(RetryDelays[attempt - 1], ct); continue; }
                    result.Success = false;
                    break;
                }

                if (response.StatusCode == HttpStatusCode.BadRequest)
                {
                    // 400 = business error (wrong AFM etc.)
                    result.Success = false;
                    result.IsBusinessError = true;
                    try
                    {
                        using var doc = JsonDocument.Parse(responseBody);
                        result.BusinessErrorDescription =
                            doc.RootElement.TryGetProperty("message", out var msg)
                                ? msg.GetString() : responseBody;
                    }
                    catch { result.BusinessErrorDescription = responseBody; }
                    result.ErrorMessage = result.BusinessErrorDescription;
                    break;
                }

                if (!response.IsSuccessStatusCode)
                {
                    result.Success      = false;
                    result.ErrorMessage = $"Ergani returned HTTP {(int)response.StatusCode}.";
                    break;
                }

                try
                {
                    var data = JsonSerializer.Deserialize<TResponse>(responseBody, JsonOptions);
                    result.Data    = data;
                    result.Success = true;
                    _logger.LogInformation("Ergani {Path} success on attempt {A}/{M}.",
                        path, attempt, MaxRetries);
                    break;
                }
                catch (JsonException jex)
                {
                    result.Success      = false;
                    result.ErrorMessage = "Could not parse Ergani response.";
                    _logger.LogError(jex, "Ergani parse error at {Path}.", path);
                    break;
                }
            }
            catch (TaskCanceledException) when (ct.IsCancellationRequested)
            {
                result.Success = false; result.ErrorMessage = "Request cancelled."; break;
            }
            catch (TaskCanceledException)
            {
                result.IsServiceUnavailable = true;
                result.ErrorMessage = $"Timeout. Attempt {attempt}/{MaxRetries}.";
                if (attempt < MaxRetries) { await Task.Delay(RetryDelays[attempt - 1], ct); continue; }
                result.Success = false; break;
            }
            catch (HttpRequestException hEx)
            {
                result.IsServiceUnavailable = true;
                result.ErrorMessage = $"Network error: {hEx.Message}. Attempt {attempt}/{MaxRetries}.";
                if (attempt < MaxRetries) { await Task.Delay(RetryDelays[attempt - 1], ct); continue; }
                result.Success = false; break;
            }
        }

        stopwatch.Stop();
        result.DurationMs = stopwatch.ElapsedMilliseconds;
        return result;
    }

    // ── HTTP client factory helpers ───────────────────────────────────────

    private HttpClient BuildClient(string baseUrl)
    {
        var client = _httpClientFactory.CreateClient("ErganiApi");
        client.BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/");
        client.Timeout     = ErganiEndpoints.RequestTimeout;
        client.DefaultRequestHeaders.Accept.Clear();
        client.DefaultRequestHeaders.Accept.Add(
            new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
        return client;
    }

    private HttpClient BuildAuthenticatedClient(string baseUrl, string bearerToken)
    {
        var client = BuildClient(baseUrl);
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", bearerToken);
        return client;
    }
}
