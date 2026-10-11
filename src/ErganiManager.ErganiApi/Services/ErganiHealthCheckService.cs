using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ErganiManager.ErganiApi.Models;
using Microsoft.Extensions.Logging;

namespace ErganiManager.ErganiApi.Services;

public enum ErganiServiceStatus { Unknown, Online, Offline }

/// <summary>
/// Checks Ergani API availability by attempting a real JWT authentication call.
/// A HEAD/ping is not enough — the web server may be up while the API is down.
/// Only a successful 200 response with a valid accessToken counts as Online.
/// </summary>
public interface IErganiHealthCheckService
{
    ErganiServiceStatus CurrentStatus { get; }
    Task<ErganiServiceStatus> CheckAsync(ErganiCredentials credentials, CancellationToken ct = default);
    event EventHandler<ErganiServiceStatus>? StatusChanged;
}

public class ErganiHealthCheckService : IErganiHealthCheckService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<ErganiHealthCheckService> _logger;
    private ErganiServiceStatus _currentStatus = ErganiServiceStatus.Unknown;

    public ErganiServiceStatus CurrentStatus => _currentStatus;
    public event EventHandler<ErganiServiceStatus>? StatusChanged;

    public ErganiHealthCheckService(
        IHttpClientFactory httpClientFactory,
        ILogger<ErganiHealthCheckService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger            = logger;
    }

    /// <summary>
    /// Performs a real POST to /Authentication with the provided credentials.
    /// Online  = HTTP 200 AND response contains a non-empty accessToken.
    /// Offline = any other result (4xx, 5xx, network error, or missing token).
    ///
    /// This catches the case where the trial API is "down" but the web server
    /// returns a 200 HTML maintenance page — that page won't contain accessToken,
    /// so the check correctly returns Offline.
    /// </summary>
    public async Task<ErganiServiceStatus> CheckAsync(
        ErganiCredentials credentials, CancellationToken ct = default)
    {
        ErganiServiceStatus newStatus;
        string detail = string.Empty;

        try
        {
            var client = _httpClientFactory.CreateClient("ErganiApi");
            client.BaseAddress = new Uri(credentials.BaseUrl.TrimEnd('/') + "/");
            client.Timeout     = TimeSpan.FromSeconds(15);
            client.DefaultRequestHeaders.Accept.Clear();
            client.DefaultRequestHeaders.Accept.Add(
                new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));

            // Build the auth request exactly as the official spec requires
            var body = JsonSerializer.Serialize(new
            {
                Username = credentials.Username,
                Password = credentials.Password,
                Usertype = ErganiEndpoints.UsertypeExternal   // "01"
            });

            using var content = new StringContent(body, Encoding.UTF8, "application/json");
            using var response = await client
                .PostAsync(ErganiEndpoints.AuthPath, content, ct);  // POST /Authentication

            var responseBody = await response.Content.ReadAsStringAsync(ct);

            if (!response.IsSuccessStatusCode)
            {
                // 4xx = bad credentials or service down
                detail    = $"HTTP {(int)response.StatusCode}";
                newStatus = ErganiServiceStatus.Offline;
            }
            else
            {
                // 200 — but check the body actually contains an accessToken
                // (maintenance pages return 200 HTML without a token)
                try
                {
                    using var doc   = JsonDocument.Parse(responseBody);
                    var tokenExists = doc.RootElement.TryGetProperty("accessToken", out var token)
                                      && !string.IsNullOrWhiteSpace(token.GetString());

                    if (tokenExists)
                    {
                        newStatus = ErganiServiceStatus.Online;
                        detail    = "JWT obtained";
                    }
                    else
                    {
                        // 200 but no token — maintenance page or API error message
                        newStatus = ErganiServiceStatus.Offline;
                        detail    = "200 OK but no accessToken in response";
                    }
                }
                catch (JsonException)
                {
                    // Response is not JSON (HTML maintenance page etc.)
                    newStatus = ErganiServiceStatus.Offline;
                    detail    = "200 OK but response is not valid JSON";
                }
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            newStatus = ErganiServiceStatus.Offline;
            detail    = ex.Message;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Unexpected error during Ergani health check.");
            newStatus = ErganiServiceStatus.Offline;
            detail    = ex.Message;
        }

        _logger.LogInformation(
            "Ergani status check: {Status} ({Detail})", newStatus, detail);

        if (newStatus != _currentStatus)
        {
            _currentStatus = newStatus;
            StatusChanged?.Invoke(this, newStatus);
        }

        return _currentStatus;
    }
}
