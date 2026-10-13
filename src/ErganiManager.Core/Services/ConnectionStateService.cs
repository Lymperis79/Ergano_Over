using System.Text.Json;
using ErganiManager.Core.Interfaces;
using ErganiManager.Core.Models;
using ErganiManager.Data;
using ErganiManager.LocalCache;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ErganiManager.Core.Services;

public class ConnectionStateService : IConnectionStateService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly ILogger<ConnectionStateService> _logger;

    // Serialises evaluations: the retry service, the login and the setup wizard may all call
    // EvaluateAsync at about the same time.
    private readonly SemaphoreSlim _evalLock = new(1, 1);

    // The config for which the schema upgrade (migrations / column patches) already succeeded
    // in this process. Evaluation itself is a cheap, read-only probe.
    private string? _upgradedKey;

    public ConnectionStateService(ILogger<ConnectionStateService> logger)
    {
        _logger = logger;
    }

    public AppConnectionState CurrentState { get; private set; } = AppConnectionState.FirstRun;

    /// <summary>Why the last evaluation did not end in Normal (null when it did).</summary>
    public string? LastFailureReason { get; private set; }

    // Cached options — built once after a successful EvaluateAsync or SaveConfig.
    // Avoids re-reading connection.json and rebuilding DbContextOptions on every DB call.
    private DbContextOptions<AppDbContext>? _cachedOptions;

    public DbContextOptions<AppDbContext> GetDbOptions()
    {
        if (_cachedOptions != null) return _cachedOptions;
        var config = LoadConfig()
            ?? throw new InvalidOperationException(ErganiManager.Data.UiText.T("Database is not configured."));
        var builder = new DbContextOptionsBuilder<AppDbContext>();
        DbProviderFactory.Configure(builder, config);
        _cachedOptions = builder.Options;
        return _cachedOptions;
    }

    public bool ConfigExists() => File.Exists(AppPaths.GetConnectionConfigPath());

    public DbConfig? LoadConfig()
    {
        var path = AppPaths.GetConnectionConfigPath();
        if (!File.Exists(path))
            return null;

        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<DbConfig>(json, JsonOptions);
        }
        catch (Exception)
        {
            // Corrupted config file — treat as if it doesn't exist, so the
            // setup wizard can run again rather than the app crashing on launch.
            return null;
        }
    }

    public async Task<bool> HasCachedUsersAsync()
    {
        // A missing/empty local cache means there is no legitimate offline
        // authentication path. In that situation a configured-but-unreachable
        // main database must not send the user to the offline login screen.
        var path = AppPaths.GetLocalCacheDbPath();
        if (!File.Exists(path))
            return false;

        try
        {
            // Do not call Create() here: startup state detection must never create
            // the offline database. It is created only after the main database and
            // first administrator have been successfully initialized.
            using var cache = new LocalCacheDbContext(
                new DbContextOptionsBuilder<LocalCacheDbContext>()
                    .UseSqlite($"Data Source={path}")
                    .Options);
            return await cache.CachedUsers.AnyAsync(u => u.IsActive);
        }
        catch
        {
            // A corrupt/missing cache is not a valid offline-login source.
            return false;
        }
    }

    public void SaveConfig(DbConfig config)
    {
        var path = AppPaths.GetConnectionConfigPath();
        var json = JsonSerializer.Serialize(config, JsonOptions);
        File.WriteAllText(path, json);
        _cachedOptions = null; // Force rebuild on next GetDbOptions call
    }

    public async Task<AppConnectionState> EvaluateAsync()
    {
        await _evalLock.WaitAsync();
        try
        {
            CurrentState = await EvaluateCoreAsync();
            return CurrentState;
        }
        finally
        {
            _evalLock.Release();
        }
    }

    private async Task<AppConnectionState> EvaluateCoreAsync()
    {
        LastFailureReason = null;

        if (!ConfigExists())
            return AppConnectionState.FirstRun;

        var config = LoadConfig();
        if (config == null)
        {
            LastFailureReason = ErganiManager.Data.UiText.T("connection.json could not be read.");
            _logger.LogWarning("connection.json exists but could not be read; treating as first run.");
            return AppConnectionState.FirstRun;
        }

        // Step 1: does the database exist and can we open it?
        var (canConnect, connectError) = await DbProviderFactory.TestConnectionAsync(config);
        if (!canConnect)
        {
            LastFailureReason = connectError;
            _logger.LogWarning("Main database not available: {Error}", connectError);

            // Offline mode is only meaningful when previously synced users exist.
            // Otherwise the user must go through database setup.
            return await HasCachedUsersAsync()
                ? AppConnectionState.Degraded
                : AppConnectionState.FirstRun;
        }

        // Step 2: are the tables we need to log in there? (read-only probe)
        try
        {
            await using var db = new AppDbContext(BuildOptions(config));
            _ = await db.Companies.AnyAsync();
            _ = await db.Users.AnyAsync();
        }
        catch (Exception ex)
        {
            LastFailureReason = ex.Message;
            _logger.LogError(ex, "Database is reachable but the schema is incomplete.");
            return AppConnectionState.SchemaIncomplete;
        }

        // Step 3: bring an older schema up to date, once per process. A failure here is logged
        // loudly but does NOT lock the user out of the login screen.
        await UpgradeSchemaOnceAsync(config);

        return AppConnectionState.Normal;
    }

    private static DbContextOptions<AppDbContext> BuildOptions(DbConfig config)
    {
        var builder = new DbContextOptionsBuilder<AppDbContext>();
        DbProviderFactory.Configure(builder, config);
        return builder.Options;
    }

    private DateTime _lastFailedUpgradeUtc = DateTime.MinValue;

    /// <summary>A short description of schema changes that could not be applied (null when all is fine).</summary>
    public string? UpgradeWarning { get; private set; }

    private async Task UpgradeSchemaOnceAsync(DbConfig config)
    {
        var key = $"{config.DatabaseProvider}|{config.SqlitePath}|{config.ConnectionString}";
        if (key == _upgradedKey) return;

        // After a failure do not retry (and log) on every evaluation — at most every 10 minutes.
        if (UpgradeWarning != null && DateTime.UtcNow - _lastFailedUpgradeUtc < TimeSpan.FromMinutes(10))
            return;

        try
        {
            var outcome = await DatabaseMigrator.ApplyAsync(config);

            IReadOnlyList<string> changes = Array.Empty<string>();
            if (!outcome.UsedMigrations)
                changes = await DbSchemaUpdater.EnsureUpToDateAsync(config);
            else if (outcome.SchemaChanged)
                // Big schema change: the temporary local cache was built from the old layout.
                // It is backed up, deleted and recreated (refilled by Sync Cache).
                LocalCacheDbContextFactory.DeleteLocalCache(backup: true);

            var failures = changes
                .Where(c => c.StartsWith(ModelSchemaSync.FailedPrefix, StringComparison.Ordinal))
                .Select(c => c[ModelSchemaSync.FailedPrefix.Length..])
                .ToList();

            foreach (var change in changes.Where(c => !c.StartsWith(ModelSchemaSync.FailedPrefix, StringComparison.Ordinal)))
                _logger.LogInformation("Schema upgrade: {Change}", change);
            foreach (var failure in failures)
                _logger.LogError("Schema upgrade FAILED: {Failure}", failure);

            if (failures.Count == 0)
            {
                _upgradedKey = key;
                UpgradeWarning = null;
            }
            else
            {
                _lastFailedUpgradeUtc = DateTime.UtcNow;
                UpgradeWarning = ErganiManager.Data.UiText.F("The database could not be fully updated: {0}", string.Join("; ", failures.Take(3))) +
                                 (failures.Count > 3 ? ErganiManager.Data.UiText.F(" (+{0} more, see the log)", failures.Count - 3) : string.Empty);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Schema upgrade failed; continuing with the existing schema.");
            _lastFailedUpgradeUtc = DateTime.UtcNow;
            UpgradeWarning = ErganiManager.Data.UiText.F("The database could not be updated: {0}", ex.Message);
        }
    }
}
