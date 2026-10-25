using ErganiManager.Core.Models;
using ErganiManager.Data;
using Microsoft.EntityFrameworkCore;

namespace ErganiManager.Core.Interfaces;

public interface IConnectionStateService
{
    Task<AppConnectionState> EvaluateAsync();
    AppConnectionState CurrentState { get; }

    /// <summary>Why the last EvaluateAsync did not end in Normal (null when it did).</summary>
    string? LastFailureReason { get; }

    /// <summary>Set when schema changes (new table/column) could not be applied; null when the schema is up to date.</summary>
    string? UpgradeWarning { get; }
    DbConfig? LoadConfig();
    void SaveConfig(DbConfig config);
    bool ConfigExists();

    /// <summary>Returns true when at least one active user is available in the local offline cache.</summary>
    Task<bool> HasCachedUsersAsync();

    /// <summary>Returns cached DbContextOptions — built once and reused so every
    /// service call doesn't re-read config from disk and rebuild options.</summary>
    DbContextOptions<AppDbContext> GetDbOptions();
}
