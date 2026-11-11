using ErganiManager.Core.Interfaces;
using ErganiManager.Data;
using Microsoft.EntityFrameworkCore;

namespace ErganiManager.Core.Services;

/// <summary>
/// Opens the complete application data model against the device-local SQLite replica.
/// The replica is intentionally a different file from LocalCacheDbContext: the latter
/// owns the durable attendance outbox and must never be rebuilt as part of replica schema
/// maintenance.
/// </summary>
public static class LocalReplicaDbContextFactory
{
    public static async Task<AppDbContext> CreateAsync(
        IConnectionStateService connectionState,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connectionState);

        var context = new AppDbContext(connectionState.GetLocalReplicaDbOptions());
        try
        {
            await context.Database.EnsureCreatedAsync(cancellationToken);
            await context.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;", cancellationToken);
            await context.Database.ExecuteSqlRawAsync("PRAGMA foreign_keys=ON;", cancellationToken);
            return context;
        }
        catch
        {
            await context.DisposeAsync();
            throw;
        }
    }
}
