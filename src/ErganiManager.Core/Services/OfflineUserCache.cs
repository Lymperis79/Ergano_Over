using ErganiManager.Data.Entities;
using ErganiManager.LocalCache;
using ErganiManager.LocalCache.Entities;
using Microsoft.EntityFrameworkCore;

namespace ErganiManager.Core.Services;

/// <summary>
/// Writes copies of main-database users into the local (offline) cache so they can
/// still log in when the main database is not available.
/// </summary>
internal static class OfflineUserCache
{
    /// <summary>Adds or updates the cached copy of <paramref name="user"/> (does not save).</summary>
    public static async Task UpsertAsync(LocalCacheDbContext cache, AppUser user)
    {
        var existing = await cache.CachedUsers
            .FirstOrDefaultAsync(u => u.SourceUserId == user.Id);

        if (existing == null)
        {
            existing = new CachedUser { SourceUserId = user.Id };
            cache.CachedUsers.Add(existing);
        }

        existing.Username = user.Username;
        existing.PasswordHash = user.PasswordHash;
        existing.Role = user.Role.ToString();
        existing.CompanyId = user.CompanyId;
        existing.CompanyName = user.Company?.Name;
        existing.BranchId = user.BranchId;
        existing.BranchName = user.Branch?.Name;
        existing.IsActive = user.IsActive;
        existing.LastSyncAt = DateTime.UtcNow;
    }
}
