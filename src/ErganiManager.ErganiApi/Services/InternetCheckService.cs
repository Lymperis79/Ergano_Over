using System;
using System.Net.NetworkInformation;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace ErganiManager.ErganiApi.Services;

/// <summary>
/// Tells whether this computer has a working internet connection, so an Ergani outage
/// ("problem connecting to ERGANI") can be told apart from our own connection being down
/// ("power / telecommunications problem").
/// </summary>
public interface IInternetCheckService
{
    Task<bool> IsOnlineAsync(CancellationToken ct = default);
}

/// <summary>Pings well-known public addresses (8.8.8.8, then 1.1.1.1). One reply is enough.</summary>
public class InternetCheckService : IInternetCheckService
{
    private static readonly string[] Targets = { "8.8.8.8", "1.1.1.1" };
    private const int TimeoutMs = 2000;

    private readonly ILogger<InternetCheckService> _logger;

    public InternetCheckService(ILogger<InternetCheckService> logger) => _logger = logger;

    public async Task<bool> IsOnlineAsync(CancellationToken ct = default)
    {
        foreach (var target in Targets)
        {
            if (ct.IsCancellationRequested) break;
            try
            {
                using var ping = new Ping();
                var reply = await ping.SendPingAsync(target, TimeoutMs);
                if (reply.Status == IPStatus.Success)
                    return true;
            }
            catch (Exception ex)
            {
                // No network adapter / no route: treated as "no reply".
                _logger.LogDebug(ex, "Internet check ping to {Target} failed.", target);
            }
        }

        _logger.LogInformation("Internet check: no reply from {Targets}.", string.Join(", ", Targets));
        return false;
    }
}
