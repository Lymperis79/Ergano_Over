using ErganiManager.Core.Interfaces;
using ErganiManager.Core.Models;
using ErganiManager.Data;
using ErganiManager.Data.Entities;
using ErganiManager.LocalCache;
using ErganiManager.LocalCache.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ErganiManager.Core.Services;

public class AuthService : IAuthService
{
    private readonly IConnectionStateService _connectionState;
    private readonly ICompanyContext _companyContext;
    private readonly IPasswordHasher _passwordHasher;
    private readonly ILogger<AuthService> _logger;

    public AuthService(
        IConnectionStateService connectionState,
        ICompanyContext companyContext,
        IPasswordHasher passwordHasher,
        ILogger<AuthService> logger)
    {
        _connectionState = connectionState;
        _companyContext = companyContext;
        _passwordHasher = passwordHasher;
        _logger = logger;
    }

    public async Task<LoginResult> LoginAsync(string username, string password)
    {
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
            return LoginResult.Fail("Username and password are required.");

        var state = await _connectionState.EvaluateAsync();

        if (state == AppConnectionState.Normal)
        {
            var onlineResult = await TryOnlineLoginAsync(username, password);
            if (onlineResult.Success)
                return onlineResult;

            // Wrong credentials against a reachable DB is a hard failure —
            // we do NOT fall back to cache here, since that could let someone
            // log in with a stale/revoked cached password while the real DB
            // is sitting right there telling us the password is wrong.
            return onlineResult;
        }

        // FirstRun should never reach here (UI should route to setup wizard first),
        // but guard anyway.
        if (state == AppConnectionState.FirstRun)
            return LoginResult.Fail("Database is not configured yet. Please run setup first.");

        // Degraded: main DB unreachable, fall back to local cache.
        _logger.LogWarning("Main database unreachable. Falling back to offline login for user {Username}.", username);
        return await TryOfflineLoginAsync(username, password);
    }

    private async Task<LoginResult> TryOnlineLoginAsync(string username, string password)
    {
        var config = _connectionState.LoadConfig();
        if (config == null)
            return LoginResult.Fail("Database is not configured.");

        var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();
        DbProviderFactory.Configure(optionsBuilder, config);

        await using var db = new AppDbContext(optionsBuilder.Options);

        var user = await db.Users
            .Include(u => u.Company)
            .Include(u => u.Branch)
            .FirstOrDefaultAsync(u => u.Username == username && u.IsActive);

        if (user == null || !_passwordHasher.Verify(password, user.PasswordHash))
        {
            _logger.LogWarning("Login failed for user {Username}: invalid credentials.", username);
            return LoginResult.Fail("Invalid username or password.");
        }

        user.LastLoginAt = DateTime.UtcNow;
        await db.SaveChangesAsync();

        var session = new UserSession
        {
            UserId = user.Id,
            Username = user.Username,
            Role = user.Role == UserRole.Admin ? AppUserRole.Admin : AppUserRole.Operator,
            CompanyId = user.CompanyId,
            CompanyName = user.Company?.Name,
            BranchId = user.BranchId,
            BranchName = user.Branch?.Name,
            IsOfflineSession = false
        };

        _companyContext.SetSession(session);

        // Refresh the local cache copy of this user so they could still log
        // in offline next time, with whatever password they just used.
        await RefreshCachedUserAsync(user);

        return LoginResult.Ok(session);
    }

    private async Task<LoginResult> TryOfflineLoginAsync(string username, string password)
    {
        using var cache = LocalCacheDbContextFactory.Create();

        var cachedUser = await cache.CachedUsers
            .FirstOrDefaultAsync(u => u.Username == username && u.IsActive);

        if (cachedUser == null || !_passwordHasher.Verify(password, cachedUser.PasswordHash))
        {
            _logger.LogWarning("Offline login failed for user {Username}: invalid credentials or not cached.", username);
            return LoginResult.Fail("Invalid username or password. (Offline mode — only previously synced users can log in.)");
        }

        var session = new UserSession
        {
            UserId = cachedUser.SourceUserId,
            Username = cachedUser.Username,
            Role = cachedUser.Role == "Admin" ? AppUserRole.Admin : AppUserRole.Operator,
            CompanyId = cachedUser.CompanyId,
            CompanyName = cachedUser.CompanyName,
            BranchId = cachedUser.BranchId,
            BranchName = cachedUser.BranchName,
            IsOfflineSession = true
        };

        _companyContext.SetSession(session);
        return LoginResult.Ok(session);
    }

    private static async Task RefreshCachedUserAsync(AppUser user)
    {
        using var cache = LocalCacheDbContextFactory.Create();

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

        await cache.SaveChangesAsync();
    }

    public void Logout()
    {
        _companyContext.Clear();
    }
}
