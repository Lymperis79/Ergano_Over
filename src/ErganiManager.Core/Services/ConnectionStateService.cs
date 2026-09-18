using System.Text.Json;
using ErganiManager.Core.Interfaces;
using ErganiManager.Core.Models;
using ErganiManager.Data;
using ErganiManager.LocalCache;
using Microsoft.EntityFrameworkCore;

namespace ErganiManager.Core.Services;

public class ConnectionStateService : IConnectionStateService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public AppConnectionState CurrentState { get; private set; } = AppConnectionState.FirstRun;

    // Cached options — built once after a successful EvaluateAsync or SaveConfig.
    // Avoids re-reading connection.json and rebuilding DbContextOptions on every DB call.
    private DbContextOptions<AppDbContext>? _cachedOptions;

    public DbContextOptions<AppDbContext> GetDbOptions()
    {
        if (_cachedOptions != null) return _cachedOptions;
        var config = LoadConfig()
            ?? throw new InvalidOperationException("Database is not configured.");
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

    public void SaveConfig(DbConfig config)
    {
        var path = AppPaths.GetConnectionConfigPath();
        var json = JsonSerializer.Serialize(config, JsonOptions);
        File.WriteAllText(path, json);
        _cachedOptions = null; // Force rebuild on next GetDbOptions call
    }

    public async Task<AppConnectionState> EvaluateAsync()
    {
        if (!ConfigExists())
        {
            CurrentState = AppConnectionState.FirstRun;
            return CurrentState;
        }

        var config = LoadConfig();
        if (config == null)
        {
            CurrentState = AppConnectionState.FirstRun;
            return CurrentState;
        }

        // Step 1: Can we open a connection at all?
        var (canConnect, _) = await DbProviderFactory.TestConnectionAsync(config);
        if (!canConnect)
        {
            CurrentState = AppConnectionState.Degraded;
            return CurrentState;
        }

        // Step 2: Is the schema actually there?
        // We probe for the Companies table — if it doesn't exist, EnsureCreated
        // either never ran or failed partway through. Route back to the setup
        // wizard so the user can fix the connection and retry.
        var schemaOk = await IsSchemaHealthyAsync(config);
        if (!schemaOk)
        {
            CurrentState = AppConnectionState.SchemaIncomplete;
            return CurrentState;
        }

        CurrentState = AppConnectionState.Normal;
        return CurrentState;
    }

    private static async Task<bool> IsSchemaHealthyAsync(DbConfig config)
    {
        try
        {
            var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();
            DbProviderFactory.Configure(optionsBuilder, config);
            await using var db = new AppDbContext(optionsBuilder.Options);

            // EF Core's way to check if a specific table exists without
            // executing raw SQL: try to query it and catch if it doesn't exist.
            // AnyAsync() on an empty table returns false — that's fine and healthy.
            // A SqlException/MySqlException about the table not existing tells us
            // the schema is broken.
            _ = await db.Companies.AnyAsync();
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
