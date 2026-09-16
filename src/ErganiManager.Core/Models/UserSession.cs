namespace ErganiManager.Core.Models;

public enum AppConnectionState
{
    /// <summary>No connection.json found yet — must run the DB setup wizard.</summary>
    FirstRun,

    /// <summary>connection.json exists, DB is reachable, and schema is healthy.</summary>
    Normal,

    /// <summary>connection.json exists but the database could not be reached.
    /// Login falls back to the local cache; scans are queued.</summary>
    Degraded,

    /// <summary>connection.json exists and DB is reachable, but the schema is
    /// missing or incomplete (e.g. EnsureCreated failed on a previous run).
    /// The setup wizard should be shown again so the user can fix and retry.</summary>
    SchemaIncomplete
}

public enum AppUserRole
{
    Admin,
    Operator
}

/// <summary>
/// Represents the currently logged-in user and active company context for
/// this session. Built either from the main database (Normal mode) or from
/// the local cache (Degraded mode) — the rest of the app does not need to
/// care which.
/// </summary>
public class UserSession
{
    public int UserId { get; set; }
    public string Username { get; set; } = string.Empty;
    public AppUserRole Role { get; set; }

    public int? CompanyId { get; set; }
    public string? CompanyName { get; set; }

    public int? BranchId { get; set; }
    public string? BranchName { get; set; }

    /// <summary>True if this session was authenticated against the local
    /// cache because the main database was unreachable.</summary>
    public bool IsOfflineSession { get; set; }

    public bool IsSuperAdmin => Role == AppUserRole.Admin && CompanyId == null;
}

public class LoginResult
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public UserSession? Session { get; set; }

    public static LoginResult Fail(string message) => new() { Success = false, ErrorMessage = message };
    public static LoginResult Ok(UserSession session) => new() { Success = true, Session = session };
}
