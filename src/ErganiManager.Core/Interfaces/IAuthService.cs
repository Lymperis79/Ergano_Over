using ErganiManager.Core.Models;

namespace ErganiManager.Core.Interfaces;

public interface IAuthService
{
    /// <summary>
    /// Attempts login. If the main database is reachable, authenticates against
    /// it and refreshes the local cache. If not, falls back to the local cache
    /// (Degraded mode) and the resulting session is flagged IsOfflineSession.
    /// </summary>
    Task<LoginResult> LoginAsync(string username, string password);

    void Logout();
}
