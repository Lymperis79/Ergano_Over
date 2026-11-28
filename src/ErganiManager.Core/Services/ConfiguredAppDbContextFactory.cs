using ErganiManager.Core.Interfaces;
using ErganiManager.Data;
using Microsoft.EntityFrameworkCore;

namespace ErganiManager.Core.Services;

/// <summary>
/// Creates short-lived application database contexts using the current connection
/// configuration. The connection state service remains responsible for loading and
/// caching provider-specific options, including changes made by the setup wizard.
/// </summary>
public sealed class ConfiguredAppDbContextFactory : IDbContextFactory<AppDbContext>
{
    private readonly IConnectionStateService _connectionState;

    public ConfiguredAppDbContextFactory(IConnectionStateService connectionState)
    {
        _connectionState = connectionState;
    }

    public AppDbContext CreateDbContext()
        => new(_connectionState.GetDbOptions());
}
