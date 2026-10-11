using ErganiManager.Core.Interfaces;
using ErganiManager.Data;
using ErganiManager.Data.Entities;
using ErganiManager.LocalCache;
using Microsoft.EntityFrameworkCore;

namespace ErganiManager.Core.Services;

public class AdminBootstrapService : IAdminBootstrapService
{
    private readonly IConnectionStateService _connectionState;
    private readonly IPasswordHasher _passwordHasher;

    public AdminBootstrapService(IConnectionStateService connectionState, IPasswordHasher passwordHasher)
    {
        _connectionState = connectionState;
        _passwordHasher = passwordHasher;
    }

    public async Task<bool> NeedsFirstAdminAsync()
    {
        var config = _connectionState.LoadConfig();
        if (config == null)
            return true;

        var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();
        DbProviderFactory.Configure(optionsBuilder, config);

        await using var db = new AppDbContext(optionsBuilder.Options);
        return !await db.Users.AnyAsync();
    }

    public async Task<BootstrapResult> CreateFirstAdminAsync(CreateFirstAdminRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Password))
            return new BootstrapResult { Success = false, ErrorMessage = "Username and password are required." };

        if (request.Password.Length < 8)
            return new BootstrapResult { Success = false, ErrorMessage = "Password must be at least 8 characters." };

        var config = _connectionState.LoadConfig();
        if (config == null)
            return new BootstrapResult { Success = false, ErrorMessage = "Database is not configured." };

        var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();
        DbProviderFactory.Configure(optionsBuilder, config);

        await using var db = new AppDbContext(optionsBuilder.Options);

        // Guard against this ever being usable as a backdoor after go-live.
        if (await db.Users.AnyAsync())
            return new BootstrapResult { Success = false, ErrorMessage = "Setup has already been completed — users already exist." };

        var admin = new AppUser
        {
            Username = request.Username.Trim(),
            PasswordHash = _passwordHasher.Hash(request.Password),
            Role = UserRole.Admin,
            CompanyId = null, // super-admin — not locked to a single company
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        db.Users.Add(admin);
        await db.SaveChangesAsync();

        return new BootstrapResult { Success = true };
    }

    public async Task SeedOfflineCacheAsync()
    {
        var config = _connectionState.LoadConfig()
            ?? throw new InvalidOperationException("Database is not configured.");

        var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();
        DbProviderFactory.Configure(optionsBuilder, config);
        await using var db = new AppDbContext(optionsBuilder.Options);

        var users = await db.Users
            .Include(u => u.Company)
            .Include(u => u.Branch)
            .Where(u => u.IsActive)
            .ToListAsync();

        using var cache = LocalCacheDbContextFactory.Create();
        foreach (var user in users)
            await OfflineUserCache.UpsertAsync(cache, user);
        await cache.SaveChangesAsync();
    }
}
