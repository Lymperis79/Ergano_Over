using System.Text.Json;
using ErganiManager.LocalCache.Entities;
using Microsoft.EntityFrameworkCore;

namespace ErganiManager.LocalCache;

/// <summary>
/// Durable local write-ahead queue. Call EnqueueAsync only after the local
/// change has been committed; reuse the same changeId when retrying the enqueue.
/// Applied/conflicted records remain in the database for audit and recovery.
/// </summary>
public sealed class LocalSyncOutbox
{
    private readonly JsonSerializerOptions _jsonOptions;

    public LocalSyncOutbox(JsonSerializerOptions? jsonOptions = null)
    {
        _jsonOptions = jsonOptions ?? new JsonSerializerOptions(JsonSerializerDefaults.Web);
    }

    public async Task<LocalSyncChange> EnqueueAsync<T>(
        string changeId,
        string entityType,
        string operation,
        string entityKey,
        int companyId,
        string deviceId,
        T payload,
        string? baseServerVersion = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(changeId))
            throw new ArgumentException("A stable idempotency key is required.", nameof(changeId));
        if (string.IsNullOrWhiteSpace(entityType))
            throw new ArgumentException("Entity type is required.", nameof(entityType));
        if (operation is not ("Create" or "Update" or "Delete"))
            throw new ArgumentOutOfRangeException(nameof(operation), "Operation must be Create, Update or Delete.");
        if (string.IsNullOrWhiteSpace(entityKey))
            throw new ArgumentException("A stable entity key is required.", nameof(entityKey));
        if (string.IsNullOrWhiteSpace(deviceId))
            throw new ArgumentException("Device ID is required.", nameof(deviceId));

        await using var cache = LocalCacheDbContextFactory.Create();
        var existing = await cache.LocalSyncChanges
            .FirstOrDefaultAsync(x => x.ChangeId == changeId, cancellationToken);
        if (existing != null)
            return existing;

        var change = new LocalSyncChange
        {
            ChangeId = changeId,
            EntityType = entityType.Trim(),
            Operation = operation,
            EntityKey = entityKey.Trim(),
            CompanyId = companyId,
            DeviceId = deviceId.Trim(),
            PayloadVersion = 1,
            PayloadJson = JsonSerializer.Serialize(payload, _jsonOptions),
            BaseServerVersion = baseServerVersion,
            CreatedAtUtc = DateTime.UtcNow,
            Status = "Pending"
        };

        cache.LocalSyncChanges.Add(change);
        try
        {
            await cache.SaveChangesAsync(cancellationToken);
            return change;
        }
        catch (DbUpdateException)
        {
            // A second caller may have inserted the same idempotency key concurrently.
            cache.Entry(change).State = EntityState.Detached;
            var duplicate = await cache.LocalSyncChanges
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.ChangeId == changeId, cancellationToken);
            if (duplicate != null)
                return duplicate;
            throw;
        }
    }

    public async Task<IReadOnlyList<LocalSyncChange>> GetPendingAsync(
        int companyId,
        int take = 100,
        CancellationToken cancellationToken = default)
    {
        if (take < 1) throw new ArgumentOutOfRangeException(nameof(take));

        await using var cache = LocalCacheDbContextFactory.Create();
        return await cache.LocalSyncChanges
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.Status == "Pending")
            .OrderBy(x => x.CreatedAtUtc)
            .ThenBy(x => x.Id)
            .Take(Math.Min(take, 1000))
            .ToListAsync(cancellationToken);
    }

    public async Task<int> GetUnresolvedCountAsync(
        int? companyId = null,
        CancellationToken cancellationToken = default)
    {
        await using var cache = LocalCacheDbContextFactory.Create();
        var query = cache.LocalSyncChanges.AsNoTracking()
            .Where(x => x.Status == "Pending" || x.Status == "Processing" || x.Status == "Conflict" || x.Status == "Rejected");
        if (companyId.HasValue)
            query = query.Where(x => x.CompanyId == companyId.Value);
        return await query.CountAsync(cancellationToken);
    }
}
