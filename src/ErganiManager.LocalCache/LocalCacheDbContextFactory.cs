using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace ErganiManager.LocalCache;

public static class LocalCacheDbContextFactory
{
    /// <summary>
    /// Builds a working LocalCacheDbContext pointed at the standard OS-specific
    /// local cache file. This always uses SQLite — the local cache is never
    /// configurable, by design, since it must work with zero setup.
    /// </summary>
    public static LocalCacheDbContext Create()
    {
        var dbPath = AppPaths.GetLocalCacheDbPath();
        var options = new DbContextOptionsBuilder<LocalCacheDbContext>()
            .UseSqlite($"Data Source={dbPath}")
            .Options;

        var context = new LocalCacheDbContext(options);
        context.Database.EnsureCreated(); // local cache has no migration ceremony needed
        return context;
    }
}

/// <summary>
/// Used by `dotnet ef migrations add` design-time tooling only.
/// </summary>
public class LocalCacheDbContextDesignTimeFactory : IDesignTimeDbContextFactory<LocalCacheDbContext>
{
    public LocalCacheDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<LocalCacheDbContext>()
            .UseSqlite("Data Source=design_time_local_cache.db")
            .Options;

        return new LocalCacheDbContext(options);
    }
}
