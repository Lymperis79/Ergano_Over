using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ErganiManager.Core.Interfaces;
using ErganiManager.Data;
using ErganiManager.Data.Entities;
using ErganiManager.ErganiApi.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ErganiManager.ErganiApi.Services;

public class ErganiImportResult
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public int BranchesImported { get; set; }
    public int EmployeesImported { get; set; }
    public string? CompanyName { get; set; }
}

public interface IErganiDataImportService
{
    /// <summary>Test Ergani credentials and return company name if valid.</summary>
    Task<(bool Valid, string? CompanyName, string? Error)> TestCredentialsAsync(
        ErganiCredentials credentials, CancellationToken ct = default);

    /// <summary>Import branches and employees for a company from Ergani.</summary>
    Task<ErganiImportResult> ImportCompanyDataAsync(
        int companyId, ErganiCredentials credentials, CancellationToken ct = default);
}

public class ErganiDataImportService : IErganiDataImportService
{
    private readonly IErganiClient _client;
    private readonly IConnectionStateService _connectionState;
    private readonly ILogger<ErganiDataImportService> _logger;

    public ErganiDataImportService(
        IErganiClient client,
        IConnectionStateService connectionState,
        ILogger<ErganiDataImportService> logger)
    {
        _client           = client;
        _connectionState  = connectionState;
        _logger           = logger;
    }

    private AppDbContext OpenDb() => new AppDbContext(_connectionState.GetDbOptions());

    public async Task<(bool Valid, string? CompanyName, string? Error)> TestCredentialsAsync(
        ErganiCredentials credentials, CancellationToken ct = default)
    {
        try
        {
            var auth = await _client.AuthenticateAsync(credentials, ct);
            if (auth == null || string.IsNullOrEmpty(auth.AccessToken))
                return (false, null, "Authentication failed. Check username and password.");

            // Try to get employer info via EX_BASE_01 service
            try
            {
                using var doc = await _client.ExecuteServiceAsync(
                    credentials, "EX_BASE_01", new List<ErganiServiceParameterValue>(), ct);

                var companyName = doc.RootElement
                    .TryGetProperty("EX_BASE_01", out var root) &&
                    root.TryGetProperty("Ergodotis", out var ergodotis) &&
                    ergodotis.TryGetProperty("Eponimia", out var name)
                        ? name.GetString()
                        : null;

                return (true, companyName, null);
            }
            catch
            {
                // Auth succeeded even if data service failed
                return (true, null, null);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Ergani credential test failed.");
            return (false, null, ex.Message);
        }
    }

    public async Task<ErganiImportResult> ImportCompanyDataAsync(
        int companyId, ErganiCredentials credentials, CancellationToken ct = default)
    {
        var result = new ErganiImportResult();
        await using var db = OpenDb();

        var company = await db.Companies.FindAsync(new object[] { companyId }, ct);
        if (company == null) return new ErganiImportResult { Success = false, ErrorMessage = "Company not found." };

        try
        {
            // ── 1. Import branches (EX_BASE_02) ──────────────────────────
            try
            {
                using var branchDoc = await _client.ExecuteServiceAsync(
                    credentials, "EX_BASE_02", new List<ErganiServiceParameterValue>(), ct);

                if (branchDoc.RootElement.TryGetProperty("EX_BASE_02", out var branchRoot) &&
                    branchRoot.TryGetProperty("Parartimata", out var parartimata) &&
                    parartimata.TryGetProperty("Parartima", out var parartArray) &&
                    parartArray.ValueKind == System.Text.Json.JsonValueKind.Array)
                {
                    foreach (var p in parartArray.EnumerateArray())
                    {
                        var aa   = p.TryGetProperty("aa", out var aaEl)   ? aaEl.GetInt32()    : 0;
                        var addr = p.TryGetProperty("address", out var addrEl) ? addrEl.GetString() ?? "" : "";
                        var sepe = p.TryGetProperty("sepe_code", out var sepeEl) ? sepeEl.GetString() ?? "" : "";
                        var kad  = p.TryGetProperty("kad", out var kadEl)  ? kadEl.GetString() ?? "" : "";

                        var existing = await db.Branches
                            .FirstOrDefaultAsync(b => b.CompanyId == companyId && b.BranchNumber == aa, ct);

                        if (existing == null)
                        {
                            db.Branches.Add(new BusinessBranch
                            {
                                CompanyId    = companyId,
                                BranchNumber = aa,
                                Name         = $"Branch {aa}",
                                Address      = addr,
                                SepeServiceCode = sepe,
                                ActivityCode = kad,
                                IsActive     = true,
                                CreatedAt    = DateTime.UtcNow
                            });
                            result.BranchesImported++;
                        }
                        else
                        {
                            existing.Address      = addr;
                            existing.SepeServiceCode = sepe;
                            existing.ActivityCode = kad;
                        }
                    }
                    await db.SaveChangesAsync(ct);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Branch import from Ergani failed — continuing with employee import.");
            }

            // ── 2. Import employees (EX_BASE_EMPLOYEES or similar) ────────
            var branches = await db.Branches
                .Where(b => b.CompanyId == companyId && b.IsActive).ToListAsync(ct);

            foreach (var branch in branches)
            {
                try
                {
                    using var empDoc = await _client.ExecuteServiceAsync(
                        credentials, "EX_BASE_EMPLOYEES",
                        new List<ErganiServiceParameterValue>
                        {
                            new() { ParameterName = "Aa", ParameterValue = branch.BranchNumber.ToString() }
                        }, ct);

                    if (empDoc.RootElement.TryGetProperty("EX_BASE_EMPLOYEES", out var empRoot) &&
                        empRoot.TryGetProperty("Employees", out var employees) &&
                        employees.TryGetProperty("Employee", out var empArray) &&
                        empArray.ValueKind == System.Text.Json.JsonValueKind.Array)
                    {
                        foreach (var emp in empArray.EnumerateArray())
                        {
                            var afm    = emp.TryGetProperty("afm", out var afmEl) ? afmEl.GetString() ?? "" : "";
                            if (string.IsNullOrEmpty(afm)) continue;

                            var lastName  = emp.TryGetProperty("eponymo", out var epEl) ? epEl.GetString() ?? "" : "";
                            var firstName = emp.TryGetProperty("onoma",   out var onEl) ? onEl.GetString() ?? "" : "";
                            var amka      = emp.TryGetProperty("amka",    out var amkaEl) ? amkaEl.GetString() ?? "" : "";

                            var existing = await db.Employees
                                .FirstOrDefaultAsync(e => e.CompanyId == companyId && e.TaxId == afm, ct);

                            if (existing == null)
                            {
                                db.Employees.Add(new Employee
                                {
                                    CompanyId           = companyId,
                                    BranchId            = branch.Id,
                                    FirstName           = firstName,
                                    LastName            = lastName,
                                    TaxId               = afm,
                                    SocialSecurityNumber = amka,
                                    WeeklyWorkdays      = 5,
                                    IsActive            = true,
                                    CreatedAt           = DateTime.UtcNow
                                });
                                result.EmployeesImported++;
                            }
                            else
                            {
                                existing.FirstName = firstName;
                                existing.LastName  = lastName;
                                if (!string.IsNullOrEmpty(amka))
                                    existing.SocialSecurityNumber = amka;
                            }
                        }
                        await db.SaveChangesAsync(ct);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex,
                        "Employee import from Ergani failed for branch {BranchId}.", branch.Id);
                }
            }

            result.Success = true;
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ergani data import failed for company {CompanyId}.", companyId);
            return new ErganiImportResult { Success = false, ErrorMessage = ex.Message };
        }
    }
}
