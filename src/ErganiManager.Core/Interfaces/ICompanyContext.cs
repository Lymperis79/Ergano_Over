using ErganiManager.Core.Models;

namespace ErganiManager.Core.Interfaces;

/// <summary>
/// Holds the active session and active company for the current app run.
/// All tenant-scoped queries should read CompanyId from here rather than
/// from a parameter the caller might forget to pass — this is what
/// guarantees no data leaks between companies.
/// </summary>
public interface ICompanyContext
{
    UserSession? CurrentSession { get; }

    /// <summary>The company ID currently active. For a super-admin this can
    /// change via SwitchCompany; for a normal Admin/Operator it is fixed to
    /// their assigned CompanyId for the whole session.</summary>
    int? ActiveCompanyId { get; }

    bool IsAuthenticated { get; }

    void SetSession(UserSession session);

    /// <summary>Only allowed for super-admins (CompanyId == null on their user record).</summary>
    void SwitchCompany(int companyId, string companyName);

    void Clear();

    event EventHandler? CompanyChanged;
}
