using ErganiManager.Core.Models;
using ErganiManager.Data;
using Microsoft.EntityFrameworkCore;

namespace ErganiManager.Core.Interfaces;

public interface IConnectionStateService
{
    Task<AppConnectionState> EvaluateAsync();
    AppConnectionState CurrentState { get; }
    DbConfig? LoadConfig();
    void SaveConfig(DbConfig config);
    bool ConfigExists();

    /// <summary>Returns cached DbContextOptions — built once and reused so every
    /// service call doesn't re-read config from disk and rebuild options.</summary>
    DbContextOptions<AppDbContext> GetDbOptions();
}
