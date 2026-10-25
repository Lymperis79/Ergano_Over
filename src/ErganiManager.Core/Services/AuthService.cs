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
            return LoginResult.Fail(ErganiManager.Data.UiText.T("Username and password are required."));

        username = username.Trim();

        // 1. Does the main database exist, is it reachable and is its schema there?
        var state = await _connectionState.EvaluateAsync();

        // 2. Yes -> log in against the main database with this account.
        string? onlineError = null;
        if (state == AppConnectionState.Normal)
        {
            try
            {
                // Wrong credentials against a reachable DB is a hard failure — we do NOT fall
                // back to the cache, since that could let someone in with a stale/revoked
                // cached password while the real DB is telling us the password is wrong.
                return await TryOnlineLoginAsync(username, password);
            }
            catch (Exception ex)
            {
                // The database disappeared between the check and the query: treat as unavailable.
                _logger.LogError(ex, "Online login failed unexpectedly; trying the offline user database.");
                onlineError = ex.GetBaseException().Message;
            }
        }

        // 3. No -> check the offline user database. It is never created here as a side effect.
        if (!await _connectionState.HasCachedUsersAsync())
        {
            _logger.LogWarning(
                "Login blocked for {Username}: main database state {State} ({Reason}) and no offline users exist.",
                username, state, _connectionState.LastFailureReason);

            var failure = string.IsNullOrWhiteSpace(_connectionState.LastFailureReason) ? onlineError : _connectionState.LastFailureReason;
            var reason = string.IsNullOrWhiteSpace(failure) ? string.Empty : $" ({failure})";

            return LoginResult.Fail(state == AppConnectionState.SchemaIncomplete
                ? ErganiManager.Data.UiText.F("The database schema is incomplete{0}. Restart the application to run database setup.", reason)
                : ErganiManager.Data.UiText.F("The database is not available and there is no offline user database yet{0}. Restart the application to run database setup.", reason));
        }

        _logger.LogWarning("Main database not available ({State}). Falling back to offline login for user {Username}.",
            state, username);
        var offline = await TryOfflineLoginAsync(username, password);

        // Do not hide a real database error behind "invalid username or password".
        if (!offline.Success && onlineError != null)
            return LoginResult.Fail(ErganiManager.Data.UiText.F("{0} Database error: {1}", offline.ErrorMessage, onlineError));

        return offline;
    }

    private async Task<LoginResult> TryOnlineLoginAsync(string username, string password)
    {
        var config = _connectionState.LoadConfig();
        if (config == null)
            return LoginResult.Fail(ErganiManager.Data.UiText.T("Database is not configured."));

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
            return LoginResult.Fail(ErganiManager.Data.UiText.T("Invalid username or password."));
        }

        user.LastLoginAt = DateTime.UtcNow;
        await db.SaveChangesAsync();

        var session = new UserSession
        {
            UserId = user.Id,
            Username = user.Username,
            Role = user.Role.ToAppRole(),
            CompanyId = user.CompanyId,
            CompanyName = user.Company?.Name,
            BranchId = user.BranchId,
            BranchName = user.Branch?.Name,
            AllowedMenus = MenuKeys.Parse(user.AllowedMenus),
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
        var cachePath = ErganiManager.LocalCache.AppPaths.GetLocalCacheDbPath();
        if (!File.Exists(cachePath))
            return LoginResult.Fail(ErganiManager.Data.UiText.T("Database setup is required. No local offline user database exists."));

        var options = new DbContextOptionsBuilder<LocalCacheDbContext>()
            .UseSqlite($"Data Source={cachePath}")
            .Options;
        using var cache = new LocalCacheDbContext(options);

        var cachedUser = await cache.CachedUsers
            .FirstOrDefaultAsync(u => u.Username == username && u.IsActive);

        if (cachedUser == null || !_passwordHasher.Verify(password, cachedUser.PasswordHash))
        {
            _logger.LogWarning("Offline login failed for user {Username}: invalid credentials or not cached.", username);
            return LoginResult.Fail(ErganiManager.Data.UiText.T("Invalid username or password. (Offline mode — only previously synced users can log in.)"));
        }

        var session = new UserSession
        {
            UserId = cachedUser.SourceUserId,
            Username = cachedUser.Username,
            Role = RoleMapping.FromCachedText(cachedUser.Role),
            CompanyId = cachedUser.CompanyId,
            CompanyName = cachedUser.CompanyName,
            BranchId = cachedUser.BranchId,
            BranchName = cachedUser.BranchName,
            AllowedMenus = MenuKeys.Parse(OfflineMenuStore.Get(cachedUser.SourceUserId)),
            IsOfflineSession = true
        };

        _companyContext.SetSession(session);
        return LoginResult.Ok(session);
    }

    private async Task RefreshCachedUserAsync(AppUser user)
    {
        // The offline copy is a convenience: failing to write it must never fail a login
        // that already succeeded against the main database.
        try
        {
            using var cache = LocalCacheDbContextFactory.Create();
            await OfflineUserCache.UpsertAsync(cache, user);
            await cache.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not refresh the offline copy of user {Username}.", user.Username);
        }
    }

    public void Logout()
    {
        _companyContext.Clear();
    }
}
