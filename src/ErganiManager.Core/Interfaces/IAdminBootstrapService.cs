namespace ErganiManager.Core.Interfaces;

public class CreateFirstAdminRequest
{
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}

public class BootstrapResult
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
}

/// <summary>
/// Handles the chicken-and-egg problem of a freshly created database having
/// zero users: nobody can log in to create the first Admin through the normal
/// Admin > Users screen, because nobody can log in yet. This service is only
/// used once, right after the DB Setup wizard creates the schema.
/// </summary>
public interface IAdminBootstrapService
{
    /// <summary>True if the Users table has zero rows — i.e. the wizard should
    /// show the "create first admin" step.</summary>
    Task<bool> NeedsFirstAdminAsync();

    /// <summary>Creates a super-admin user (CompanyId = null) with the given
    /// credentials. Fails if any user already exists, to prevent this from
    /// being used as a backdoor after initial setup.</summary>
    Task<BootstrapResult> CreateFirstAdminAsync(CreateFirstAdminRequest request);

    /// <summary>Creates the offline (local cache) database if needed and copies all active
    /// main-database users into it. Called after the database and the first administrator
    /// exist, so offline login works from the very first run.</summary>
    Task SeedOfflineCacheAsync();
}
