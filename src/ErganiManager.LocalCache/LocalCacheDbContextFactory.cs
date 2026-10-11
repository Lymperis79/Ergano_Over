using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace ErganiManager.LocalCache;

public static class LocalCacheDbContextFactory
{
    private static readonly object Gate = new();
    private static bool _schemaChecked;

    /// <summary>
    /// Builds a working LocalCacheDbContext pointed at the standard OS-specific
    /// local cache file. This always uses SQLite — the local cache is never
    /// configurable, by design, since it must work with zero setup.
    ///
    /// The cache is disposable: if its model changed since the file was created
    /// (detected through a schema signature stored next to the file) the old file is
    /// backed up and recreated, so a new release never runs against a stale layout.
    /// </summary>
    public static LocalCacheDbContext Create()
    {
        var dbPath = AppPaths.GetLocalCacheDbPath();
        EnsureSchemaCurrent(dbPath);

        var context = new LocalCacheDbContext(BuildOptions(dbPath));
        context.Database.EnsureCreated();
        return context;
    }

    private static DbContextOptions<LocalCacheDbContext> BuildOptions(string dbPath) =>
        new DbContextOptionsBuilder<LocalCacheDbContext>()
            .UseSqlite($"Data Source={dbPath}")
            .Options;

    // ── Schema signature ──────────────────────────────────────────────────────

    /// <summary>Hash of the cache model (entities, columns, nullability, indexes).</summary>
    public static string ComputeModelSignature()
    {
        using var ctx = new LocalCacheDbContext(BuildOptions(AppPaths.GetLocalCacheDbPath()));
        var sb = new StringBuilder();

        foreach (var entity in ctx.Model.GetEntityTypes().OrderBy(e => e.Name, StringComparer.Ordinal))
        {
            sb.Append(entity.Name).Append('{');
            foreach (var prop in entity.GetProperties().OrderBy(p => p.Name, StringComparer.Ordinal))
                sb.Append(prop.Name).Append(':').Append(prop.ClrType.FullName)
                  .Append(prop.IsNullable ? "?" : string.Empty).Append(';');
            foreach (var index in entity.GetIndexes()
                         .OrderBy(i => string.Join(",", i.Properties.Select(p => p.Name)), StringComparer.Ordinal))
                sb.Append("ix(").Append(string.Join(",", index.Properties.Select(p => p.Name)))
                  .Append(index.IsUnique ? ",unique" : string.Empty).Append(");");
            sb.Append('}');
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString())));
    }

    private static string SignaturePath(string dbPath) => dbPath + ".schema";

    private static void EnsureSchemaCurrent(string dbPath)
    {
        if (_schemaChecked) return;

        lock (Gate)
        {
            if (_schemaChecked) return;

            var signature = ComputeModelSignature();
            var sigPath   = SignaturePath(dbPath);
            var stored    = File.Exists(sigPath) ? File.ReadAllText(sigPath).Trim() : null;

            // stored == null: file from a release before signatures existed — assume it is
            // current and just record the signature. (Use Administration → Rebuild if it is not.)
            if (stored != null && stored != signature && File.Exists(dbPath))
                RebuildCore(dbPath, backup: true);

            File.WriteAllText(sigPath, signature);
            _schemaChecked = true;
        }
    }

    // ── Maintenance (also used by the Administration screen) ──────────────────

    /// <summary>
    /// Deletes the local cache database. With <paramref name="backup"/> the old file is first copied
    /// to a timestamped .bak (the 3 newest are kept) because it may hold scans that were not
    /// synced yet. It is recreated empty on the next <see cref="Create"/>.
    /// </summary>
    public static string? DeleteLocalCache(bool backup)
    {
        lock (Gate)
        {
            var dbPath = AppPaths.GetLocalCacheDbPath();
            var bak = RebuildCore(dbPath, backup);
            _schemaChecked = false; // next Create() recreates the file and writes the signature
            return bak;
        }
    }

    /// <summary>Number of scans not yet synced and failed submissions not yet resolved.</summary>
    public static (int Pending, int Failed) GetUnsyncedCounts()
    {
        if (!File.Exists(AppPaths.GetLocalCacheDbPath())) return (0, 0);
        using var cache = Create();
        return (cache.PendingSubmissions.Count(p => !p.Synced),
                cache.FailedSubmissions.Count(f => !f.Resolved));
    }

    private static string? RebuildCore(string dbPath, bool backup)
    {
        string? bakPath = null;

        // Release the SQLite file handles held by the connection pool
        SqliteConnection.ClearAllPools();

        if (backup && File.Exists(dbPath))
        {
            bakPath = $"{dbPath}.{DateTime.Now:yyyyMMdd-HHmmss}.bak";
            File.Copy(dbPath, bakPath, overwrite: true);

            var dir  = Path.GetDirectoryName(dbPath)!;
            var name = Path.GetFileName(dbPath);
            foreach (var old in Directory.GetFiles(dir, name + ".*.bak")
                         .OrderByDescending(f => f, StringComparer.Ordinal).Skip(3))
            {
                try { File.Delete(old); } catch { /* best effort */ }
            }
        }

        foreach (var f in new[] { dbPath, dbPath + "-wal", dbPath + "-shm", dbPath + "-journal", SignaturePath(dbPath) })
        {
            try { if (File.Exists(f)) File.Delete(f); } catch { /* best effort */ }
        }

        return bakPath;
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
