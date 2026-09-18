using ErganiManager.Core.Interfaces;
using ErganiManager.Core.Models;

namespace ErganiManager.Core.Services;

public class CompanyContextService : ICompanyContext
{
    private UserSession? _session;
    private int? _activeCompanyId;
    private string? _activeCompanyName;

    public UserSession? CurrentSession => _session;
    public int? ActiveCompanyId => _activeCompanyId;
    public bool IsAuthenticated => _session != null;

    public event EventHandler? CompanyChanged;

    public void SetSession(UserSession session)
    {
        _session = session;
        _activeCompanyId = session.CompanyId;
        _activeCompanyName = session.CompanyName;
        CompanyChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SwitchCompany(int companyId, string companyName)
    {
        if (_session == null)
            throw new InvalidOperationException("Cannot switch company before a session is set.");

        if (!_session.IsSuperAdmin)
            throw new InvalidOperationException("Only a super-admin (no fixed CompanyId) may switch active company.");

        _activeCompanyId = companyId;
        _activeCompanyName = companyName;
        CompanyChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Clear()
    {
        _session = null;
        _activeCompanyId = null;
        _activeCompanyName = null;
        CompanyChanged?.Invoke(this, EventArgs.Empty);
    }
}
