using ErganiManager.Core.Interfaces;
using ErganiManager.ErganiApi.Services;
using ErganiManager.LocalCache;
using Microsoft.Extensions.DependencyInjection;

namespace ErganiManager.ErganiApi;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the Ergani API client and credential protection services,
    /// and supplies the IWorkCardSubmitter implementation that Core's
    /// CacheSyncService depends on for flushing the offline queue.
    /// </summary>
    public static IServiceCollection AddErganiApiClient(this IServiceCollection services)
    {
        services.AddHttpClient("ErganiApi", client =>
        {
            client.Timeout = ErganiEndpoints.RequestTimeout;
            client.DefaultRequestHeaders.Add("Accept", "application/json");
        });

        services.AddSingleton<ICredentialProtector>(_ => new CredentialProtector(AppPaths.GetAppDataFolder()));
        services.AddSingleton<IErganiClient, ErganiClient>();
        services.AddSingleton<IErganiHealthCheckService, ErganiHealthCheckService>();
        services.AddSingleton<ErganiRetryService>();
        services.AddTransient<IWorkCardSubmitter, ErganiApiService>();
        services.AddTransient<IScheduleSubmitter, ScheduleSubmitterService>();
        services.AddTransient<IEmailAlertService, EmailAlertService>();

        return services;
    }
}
