using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ErganiManager.ErganiApi.Models;
using Microsoft.Extensions.Logging;

namespace ErganiManager.ErganiApi.Services;

public enum ErganiServiceStatus { Unknown, Online, Offline }

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

    public ErganiHealthCheckService(IHttpClientFactory httpClientFactory, ILogger<ErganiHealthCheckService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task<ErganiServiceStatus> CheckAsync(ErganiCredentials credentials, CancellationToken ct = default)
    {
        ErganiServiceStatus newStatus;
        try
        {
            var client = _httpClientFactory.CreateClient("ErganiApi");
            client.BaseAddress = new Uri(credentials.BaseUrl.TrimEnd('/') + "/");
            client.Timeout = TimeSpan.FromSeconds(10);

            var raw = $"{credentials.Username}:{credentials.Password}";
            var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(raw));
            client.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", encoded);

            var response = await client.SendAsync(
                new HttpRequestMessage(HttpMethod.Head, (Uri?)null), ct);

            newStatus = (int)response.StatusCode >= 500
                ? ErganiServiceStatus.Offline
                : ErganiServiceStatus.Online;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            newStatus = ErganiServiceStatus.Offline;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Unexpected error during Ergani health check.");
            newStatus = ErganiServiceStatus.Offline;
        }

        if (newStatus != _currentStatus)
        {
            var previous = _currentStatus;
            _currentStatus = newStatus;
            _logger.LogInformation("Ergani service status: {Previous} → {Current}", previous, newStatus);
            StatusChanged?.Invoke(this, newStatus);
        }

        return _currentStatus;
    }
}
