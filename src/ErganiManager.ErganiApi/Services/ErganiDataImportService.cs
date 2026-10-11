using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
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

    public string? CompanyName { get; set; }

    public string? CompanyTaxId { get; set; }

    public int CompanyId { get; set; }

    public bool CompanyCreated { get; set; }

    public bool CompanyUpdated { get; set; }

    public int BranchesImported { get; set; }

    public int BranchesUpdated { get; set; }

    public int EmployeesImported { get; set; }

    public int EmployeesUpdated { get; set; }

    public List<string> Warnings { get; } = new();

    public int TotalBranches =>
        BranchesImported + BranchesUpdated;

    public int TotalEmployees =>
        EmployeesImported + EmployeesUpdated;
}

public interface IErganiDataImportService
{
    Task<(bool Valid, string? CompanyName, string? Error)> TestCredentialsAsync(
        ErganiCredentials credentials,
        CancellationToken ct = default);

    Task<ErganiImportResult> ImportFromErganiAsync(
        ErganiCredentials credentials,
        CancellationToken ct = default);

    Task<ErganiImportResult> ImportCompanyDataAsync(
        int companyId,
        ErganiCredentials credentials,
        CancellationToken ct = default);
}

public class ErganiDataImportService : IErganiDataImportService
{
    private readonly IErganiClient _client;
    private readonly IConnectionStateService _connectionState;
    private readonly ICompanyService _companyService;
    private readonly ILogger<ErganiDataImportService> _logger;

    // =====================================================================
    // DEFAULT VALUES
    // =====================================================================

    /*
     * These values are used when Ergani does not provide a value but
     * our local database requires a non-null/non-empty value.
     */

    private const string DefaultBranchAddress = "Unknown";

    private const string DefaultSepeServiceCode = "UNKNOWN";

    private const string DefaultActivityCode = "UNKNOWN";

    private const string DefaultKallikratisCode = "UNKNOWN";

    private const string DefaultProfessionCode = "UNKNOWN";

    /*
     * Ergani does not provide the barcode used by our local scanner.
     *
     * For a NEW employee we initially use the AFM.
     *
     * Existing manually configured BarcodeId values are never overwritten.
     */
    private const bool UseAfmAsInitialBarcode = true;

    // =====================================================================
    // CONSTRUCTOR
    // =====================================================================

    public ErganiDataImportService(
        IErganiClient client,
        IConnectionStateService connectionState,
        ICompanyService companyService,
        ILogger<ErganiDataImportService> logger)
    {
        _client = client;
        _connectionState = connectionState;
        _companyService = companyService;
        _logger = logger;
    }

    private AppDbContext OpenDb()
        => new AppDbContext(_connectionState.GetDbOptions());

    // =====================================================================
    // TEST CREDENTIALS
    // =====================================================================

    public async Task<(bool Valid, string? CompanyName, string? Error)>
        TestCredentialsAsync(
            ErganiCredentials credentials,
            CancellationToken ct = default)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(credentials.Username))
            {
                return (
                    false,
                    null,
                    "Ergani username is required.");
            }

            if (string.IsNullOrWhiteSpace(credentials.Password))
            {
                return (
                    false,
                    null,
                    "Ergani password is required.");
            }

            var auth = await _client.AuthenticateAsync(
                credentials,
                ct);

            if (auth == null ||
                string.IsNullOrWhiteSpace(auth.AccessToken))
            {
                return (
                    false,
                    null,
                    "Authentication failed. Check the Ergani username, password and Base URL.");
            }

            /*
             * Authentication succeeded.
             *
             * Retrieve employer information.
             */
            try
            {
                using var doc = await _client.ExecuteServiceAsync(
                    credentials,
                    "EX_BASE_01",
                    new List<ErganiServiceParameterValue>(),
                    ct);

                var employer =
                    ExtractEmployer(doc.RootElement);

                return (
                    true,
                    employer.Name,
                    null);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Ergani authentication succeeded but EX_BASE_01 failed.");

                /*
                 * Credentials themselves were valid.
                 */
                return (
                    true,
                    null,
                    null);
            }
        }
        catch (InvalidOperationException ex)
        {
            return (
                false,
                null,
                ex.Message);
        }
        catch (HttpRequestException ex)
        {
            return (
                false,
                null,
                $"Cannot reach Ergani: {ex.Message}");
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Ergani credential test failed.");

            return (
                false,
                null,
                ex.Message);
        }
    }

    // =====================================================================
    // COMPLETE IMPORT
    // =====================================================================

    public async Task<ErganiImportResult> ImportFromErganiAsync(
        ErganiCredentials credentials,
        CancellationToken ct = default)
    {
        var result = new ErganiImportResult();

        try
        {
            if (string.IsNullOrWhiteSpace(credentials.Username))
            {
                result.ErrorMessage =
                    "Ergani username is required.";

                return result;
            }

            if (string.IsNullOrWhiteSpace(credentials.Password))
            {
                result.ErrorMessage =
                    "Ergani password is required.";

                return result;
            }

            _logger.LogInformation(
                "==========================================================");

            _logger.LogInformation(
                "Starting complete Ergani import for username {Username}.",
                credentials.Username);

            // =============================================================
            // STEP 1 - AUTHENTICATION
            // =============================================================

            _logger.LogInformation(
                "STEP 1: Authenticating with Ergani...");

            var auth = await _client.AuthenticateAsync(
                credentials,
                ct);

            if (auth == null ||
                string.IsNullOrWhiteSpace(auth.AccessToken))
            {
                result.ErrorMessage =
                    "Ergani authentication failed. Check username, password and Base URL.";

                return result;
            }

            _logger.LogInformation(
                "STEP 1 SUCCESS: Ergani authentication successful.");

            // =============================================================
            // STEP 2 - COMPANY / EX_BASE_01
            // =============================================================

            _logger.LogInformation(
                "STEP 2: Requesting company information using EX_BASE_01...");

            using var employerDoc =
                await _client.ExecuteServiceAsync(
                    credentials,
                    "EX_BASE_01",
                    new List<ErganiServiceParameterValue>(),
                    ct);

            var employerJson =
                employerDoc.RootElement.GetRawText();

            _logger.LogInformation(
                "EX_BASE_01 response received: {Json}",
                employerJson);

            var employer =
                ExtractEmployer(
                    employerDoc.RootElement);

            if (string.IsNullOrWhiteSpace(employer.TaxId))
            {
                result.ErrorMessage =
                    "Ergani EX_BASE_01 returned company information, but no AFM (Afm) was found.";

                return result;
            }

            result.CompanyTaxId =
                employer.TaxId.Trim();

            result.CompanyName =
                employer.Name;

            if (string.IsNullOrWhiteSpace(result.CompanyName))
            {
                result.CompanyName =
                    $"Company {result.CompanyTaxId}";
            }

            _logger.LogInformation(
                "STEP 2 SUCCESS: Company received. AFM={TaxId}, Name={Name}.",
                result.CompanyTaxId,
                result.CompanyName);

            // =============================================================
            // STEP 3 - CREATE / UPDATE COMPANY
            // =============================================================

            _logger.LogInformation(
                "STEP 3: Creating/updating local company...");

            var companies =
                await _companyService.GetAllAsync();

            var existingCompany =
                companies.FirstOrDefault(
                    c =>
                        string.Equals(
                            NormalizeTaxId(c.TaxId),
                            NormalizeTaxId(result.CompanyTaxId),
                            StringComparison.OrdinalIgnoreCase));

            var companyDto =
                new CompanyDto
                {
                    Id =
                        existingCompany?.Id ?? 0,

                    Name =
                        result.CompanyName,

                    TaxId =
                        result.CompanyTaxId,

                    ErganiUsername =
                        credentials.Username,

                    /*
                     * CompanyService is responsible for encrypting
                     * this password.
                     */
                    ErganiPasswordPlainText =
                        credentials.Password,

                    ErganiUsertype =
                        string.IsNullOrWhiteSpace(credentials.Usertype)
                            ? "01"
                            : credentials.Usertype,

                    ErganiBaseUrl =
                        string.IsNullOrWhiteSpace(credentials.BaseUrl)
                            ? ErganiEndpoints.TrialBaseUrl
                            : credentials.BaseUrl,

                    IsActive =
                        existingCompany?.IsActive ?? true,

                    EarlyClockInBlockMinutes =
                        existingCompany?.EarlyClockInBlockMinutes ?? 15,

                    EarlyDepartureAlertMinutes =
                        existingCompany?.EarlyDepartureAlertMinutes ?? 10,

                    BlockClockInWithoutSchedule =
                        existingCompany?.BlockClockInWithoutSchedule ?? true,

                    AlertEmailEnabled =
                        existingCompany?.AlertEmailEnabled ?? false,

                    AutoRetryFailedSubmissions =
                        existingCompany?.AutoRetryFailedSubmissions ?? true,

                    AlertEmailRecipients =
                        existingCompany?.AlertEmailRecipients,

                    SmtpHost =
                        existingCompany?.SmtpHost,

                    SmtpPort =
                        existingCompany?.SmtpPort ?? 587,

                    SmtpUser =
                        existingCompany?.SmtpUser,

                    SmtpUseTls =
                        existingCompany?.SmtpUseTls ?? true
                };

            if (existingCompany == null)
            {
                var newCompanyId =
                    await _companyService.CreateAsync(
                        companyDto);

                result.CompanyId =
                    newCompanyId;

                result.CompanyCreated =
                    true;

                _logger.LogInformation(
                    "STEP 3 SUCCESS: Created company Id={CompanyId}, AFM={TaxId}.",
                    newCompanyId,
                    result.CompanyTaxId);
            }
            else
            {
                await _companyService.UpdateAsync(
                    companyDto);

                result.CompanyId =
                    existingCompany.Id;

                result.CompanyUpdated =
                    true;

                _logger.LogInformation(
                    "STEP 3 SUCCESS: Updated company Id={CompanyId}, AFM={TaxId}.",
                    existingCompany.Id,
                    result.CompanyTaxId);
            }

            // =============================================================
            // STEP 4 - BRANCHES / EX_BASE_02
            // =============================================================

            _logger.LogInformation(
                "STEP 4: Requesting branches using EX_BASE_02...");

            await ImportBranchesAsync(
                result.CompanyId,
                credentials,
                result,
                ct);

            _logger.LogInformation(
                "STEP 4 FINISHED: Branches added={Added}, updated={Updated}.",
                result.BranchesImported,
                result.BranchesUpdated);

            // =============================================================
            // STEP 5 - EMPLOYEES / EX_BASE_05
            // =============================================================

            _logger.LogInformation(
                "STEP 5: Requesting employees using EX_BASE_05...");

            await ImportEmployeesAsync(
                result.CompanyId,
                credentials,
                result,
                ct);

            _logger.LogInformation(
                "STEP 5 FINISHED: Employees added={Added}, updated={Updated}.",
                result.EmployeesImported,
                result.EmployeesUpdated);

            // =============================================================
            // COMPLETE
            // =============================================================

            result.Success = true;

            if (result.Warnings.Count > 0)
            {
                result.ErrorMessage =
                    string.Join(
                        Environment.NewLine,
                        result.Warnings);
            }

            _logger.LogInformation(
                "==========================================================");

            _logger.LogInformation(
                "COMPLETE ERGANI IMPORT FINISHED.");

            _logger.LogInformation(
                "CompanyId={CompanyId}",
                result.CompanyId);

            _logger.LogInformation(
                "CompanyCreated={Created}, CompanyUpdated={Updated}",
                result.CompanyCreated,
                result.CompanyUpdated);

            _logger.LogInformation(
                "Branches Added={BranchesAdded}, Updated={BranchesUpdated}",
                result.BranchesImported,
                result.BranchesUpdated);

            _logger.LogInformation(
                "Employees Added={EmployeesAdded}, Updated={EmployeesUpdated}",
                result.EmployeesImported,
                result.EmployeesUpdated);

            _logger.LogInformation(
                "Warnings={WarningCount}",
                result.Warnings.Count);

            _logger.LogInformation(
                "==========================================================");

            return result;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            var message =
                GetFullExceptionMessage(ex);

            _logger.LogError(
                ex,
                "Complete Ergani import failed.");

            result.Success = false;

            result.ErrorMessage =
                message;

            return result;
        }
    }

    // =====================================================================
    // IMPORT BRANCHES - EX_BASE_02
    // =====================================================================

    private async Task ImportBranchesAsync(
        int companyId,
        ErganiCredentials credentials,
        ErganiImportResult result,
        CancellationToken ct)
    {
        try
        {
            _logger.LogInformation(
                "Calling Ergani EX_BASE_02 for CompanyId={CompanyId}.",
                companyId);

            using var branchDoc =
                await _client.ExecuteServiceAsync(
                    credentials,
                    "EX_BASE_02",
                    new List<ErganiServiceParameterValue>(),
                    ct);

            var branchJson =
                branchDoc.RootElement.GetRawText();

            _logger.LogInformation(
                "EX_BASE_02 response: {Json}",
                branchJson);

            var branchRecords =
                ParseBranches(
                    branchDoc.RootElement);

            _logger.LogInformation(
                "EX_BASE_02 parsed {Count} branch record(s).",
                branchRecords.Count);

            if (branchRecords.Count == 0)
            {
                result.Warnings.Add(
                    "EX_BASE_02 returned data, but no branches were found.");

                return;
            }

            await using var db =
                OpenDb();

            foreach (var branchData in branchRecords)
            {
                ct.ThrowIfCancellationRequested();

                /*
                 * IMPORTANT:
                 *
                 * Ergani can legitimately return:
                 *
                 *     "Aa": "0"
                 *
                 * Therefore 0 IS a valid branch number.
                 *
                 * We only reject the branch if Aa is completely missing
                 * or could not be converted to an integer.
                 */
                if (!branchData.BranchNumber.HasValue)
                {
                    result.Warnings.Add(
                        "A branch without a valid Aa/branch number was skipped.");

                    _logger.LogWarning(
                        "Skipping branch because Aa was missing or invalid. Address={Address}",
                        branchData.Address);

                    continue;
                }

                var branchNumber =
                    branchData.BranchNumber.Value;

                _logger.LogInformation(
                    "Processing branch AA={BranchNumber}, Address={Address}.",
                    branchNumber,
                    branchData.Address);

                var existing =
                    await db.Branches.FirstOrDefaultAsync(
                        b =>
                            b.CompanyId == companyId &&
                            b.BranchNumber == branchNumber,
                        ct);

                if (existing == null)
                {
                    var branch =
                        new BusinessBranch
                        {
                            CompanyId =
                                companyId,

                            /*
                             * AA = 0 is valid and is stored as 0.
                             */
                            BranchNumber =
                                branchNumber,

                            Name =
                                $"Branch {branchNumber}",

                            Address =
                                DefaultIfEmpty(
                                    branchData.Address,
                                    DefaultBranchAddress),

                            SepeServiceCode =
                                DefaultIfEmpty(
                                    branchData.SepeServiceCode,
                                    DefaultSepeServiceCode),

                            OaedServiceCode =
                                EmptyToNull(
                                    branchData.OaedServiceCode),

                            ActivityCode =
                                DefaultIfEmpty(
                                    branchData.ActivityCode,
                                    DefaultActivityCode),

                            KallikratisMunicipalCode =
                                DefaultIfEmpty(
                                    branchData.KallikratisMunicipalCode,
                                    DefaultKallikratisCode),

                            IsActive =
                                true
                        };

                    db.Branches.Add(branch);

                    result.BranchesImported++;

                    _logger.LogInformation(
                        "Adding new branch AA={BranchNumber}.",
                        branch.BranchNumber);
                }
                else
                {
                    /*
                     * Update values supplied by Ergani.
                     *
                     * If Ergani doesn't supply a required value,
                     * preserve the existing value or use a default.
                     */

                    if (!string.IsNullOrWhiteSpace(
                            branchData.Address))
                    {
                        existing.Address =
                            branchData.Address.Trim();
                    }
                    else if (string.IsNullOrWhiteSpace(
                                 existing.Address))
                    {
                        existing.Address =
                            DefaultBranchAddress;
                    }

                    if (!string.IsNullOrWhiteSpace(
                            branchData.SepeServiceCode))
                    {
                        existing.SepeServiceCode =
                            branchData.SepeServiceCode.Trim();
                    }
                    else if (string.IsNullOrWhiteSpace(
                                 existing.SepeServiceCode))
                    {
                        existing.SepeServiceCode =
                            DefaultSepeServiceCode;
                    }

                    if (!string.IsNullOrWhiteSpace(
                            branchData.OaedServiceCode))
                    {
                        existing.OaedServiceCode =
                            branchData.OaedServiceCode.Trim();
                    }

                    if (!string.IsNullOrWhiteSpace(
                            branchData.ActivityCode))
                    {
                        existing.ActivityCode =
                            branchData.ActivityCode.Trim();
                    }
                    else if (string.IsNullOrWhiteSpace(
                                 existing.ActivityCode))
                    {
                        existing.ActivityCode =
                            DefaultActivityCode;
                    }

                    if (!string.IsNullOrWhiteSpace(
                            branchData.KallikratisMunicipalCode))
                    {
                        existing.KallikratisMunicipalCode =
                            branchData.KallikratisMunicipalCode.Trim();
                    }
                    else if (string.IsNullOrWhiteSpace(
                                 existing.KallikratisMunicipalCode))
                    {
                        existing.KallikratisMunicipalCode =
                            DefaultKallikratisCode;
                    }

                    existing.IsActive =
                        true;

                    result.BranchesUpdated++;

                    _logger.LogInformation(
                        "Updating existing branch AA={BranchNumber}.",
                        existing.BranchNumber);
                }
            }

            await db.SaveChangesAsync(ct);

            _logger.LogInformation(
                "EX_BASE_02 database save completed. Added={Added}, Updated={Updated}.",
                result.BranchesImported,
                result.BranchesUpdated);
        }
        catch (Exception ex)
        {
            var message =
                GetFullExceptionMessage(ex);

            _logger.LogError(
                ex,
                "Branch import failed for CompanyId={CompanyId}.",
                companyId);

            result.Warnings.Add(
                $"Branch import failed: {message}");
        }
    }

    // =====================================================================
    // IMPORT EMPLOYEES - EX_BASE_05
    // =====================================================================

    private async Task ImportEmployeesAsync(
        int companyId,
        ErganiCredentials credentials,
        ErganiImportResult result,
        CancellationToken ct)
    {
        try
        {
            _logger.LogInformation(
                "Calling Ergani EX_BASE_05 for CompanyId={CompanyId}.",
                companyId);

            using var employeeDoc =
                await _client.ExecuteServiceAsync(
                    credentials,
                    "EX_BASE_05",
                    new List<ErganiServiceParameterValue>(),
                    ct);

            var employeeJson =
                employeeDoc.RootElement.GetRawText();

            _logger.LogInformation(
                "EX_BASE_05 response: {Json}",
                employeeJson);

            var employees =
                ParseEmployees(
                    employeeDoc.RootElement);

            _logger.LogInformation(
                "EX_BASE_05 parsed {Count} employee record(s).",
                employees.Count);

            if (employees.Count == 0)
            {
                result.Warnings.Add(
                    "EX_BASE_05 returned data, but no employees were found.");

                return;
            }

            await using var db =
                OpenDb();

            var branches =
                await db.Branches
                    .Where(
                        b =>
                            b.CompanyId == companyId &&
                            b.IsActive)
                    .ToListAsync(ct);

            _logger.LogInformation(
                "Found {Count} active branch(es) in local database for employee linking.",
                branches.Count);

            if (branches.Count == 0)
            {
                result.Warnings.Add(
                    "No branches exist in the database. Employees cannot be linked.");

                return;
            }

            foreach (var employeeData in employees)
            {
                ct.ThrowIfCancellationRequested();

                if (string.IsNullOrWhiteSpace(
                        employeeData.TaxId))
                {
                    result.Warnings.Add(
                        "An employee without AFM was returned by Ergani and was skipped.");

                    continue;
                }

                var employeeTaxId =
                    employeeData.TaxId.Trim();

                BusinessBranch? branch = null;

                /*
                 * PararthmaAa can also legitimately be 0.
                 *
                 * Therefore HasValue is used rather than:
                 *
                 *     BranchNumber > 0
                 */
                if (employeeData.BranchNumber.HasValue)
                {
                    var employeeBranchNumber =
                        employeeData.BranchNumber.Value;

                    branch =
                        branches.FirstOrDefault(
                            b =>
                                b.BranchNumber ==
                                employeeBranchNumber);

                    if (branch == null)
                    {
                        result.Warnings.Add(
                            $"Employee AFM {employeeTaxId} could not be linked to branch " +
                            $"{employeeBranchNumber}.");

                        continue;
                    }
                }
                else
                {
                    /*
                     * If Ergani doesn't provide the branch and there is
                     * exactly one local branch, associate the employee
                     * with that branch.
                     */
                    if (branches.Count == 1)
                    {
                        branch =
                            branches[0];
                    }
                    else
                    {
                        result.Warnings.Add(
                            $"Employee AFM {employeeTaxId} could not be linked to a branch because " +
                            "Ergani did not provide PararthmaAa and multiple branches exist.");

                        continue;
                    }
                }

                var existing =
                    await db.Employees.FirstOrDefaultAsync(
                        e =>
                            e.CompanyId == companyId &&
                            e.TaxId == employeeTaxId,
                        ct);

                var firstName =
                    DefaultIfEmpty(
                        employeeData.FirstName,
                        "Unknown");

                var lastName =
                    DefaultIfEmpty(
                        employeeData.LastName,
                        "Unknown");

                var socialSecurityNumber =
                    DefaultIfEmpty(
                        employeeData.Amka,
                        "00000000000");

                var professionCode =
                    DefaultIfEmpty(
                        employeeData.ProfessionCode,
                        DefaultProfessionCode);

                var weeklyWorkdays =
                    employeeData.WeeklyWorkdays.HasValue &&
                    employeeData.WeeklyWorkdays.Value >= 1 &&
                    employeeData.WeeklyWorkdays.Value <= 7
                        ? employeeData.WeeklyWorkdays.Value
                        : 5;

                /*
                 * Ergani does not provide our local scanner barcode.
                 *
                 * Use AFM as the initial barcode.
                 */
                var initialBarcode =
                    UseAfmAsInitialBarcode
                        ? employeeTaxId
                        : $"ERGANI-{employeeTaxId}";

                if (existing == null)
                {
                    var employee =
                        new Employee
                        {
                            CompanyId =
                                companyId,

                            BranchId =
                                branch.Id,

                            FirstName =
                                firstName,

                            LastName =
                                lastName,

                            TaxId =
                                employeeTaxId,

                            SocialSecurityNumber =
                                socialSecurityNumber,

                            BarcodeId =
                                initialBarcode,

                            ProfessionCode =
                                professionCode,

                            WeeklyWorkdays =
                                weeklyWorkdays,

                            IsActive =
                                true,

                            CreatedAt =
                                DateTime.UtcNow
                        };

                    db.Employees.Add(employee);

                    result.EmployeesImported++;

                    _logger.LogInformation(
                        "Adding employee AFM={Afm}, Branch={BranchNumber}.",
                        employee.TaxId,
                        branch.BranchNumber);
                }
                else
                {
                    existing.BranchId =
                        branch.Id;

                    existing.FirstName =
                        firstName;

                    existing.LastName =
                        lastName;

                    if (!string.IsNullOrWhiteSpace(
                            employeeData.Amka))
                    {
                        existing.SocialSecurityNumber =
                            employeeData.Amka.Trim();
                    }
                    else if (string.IsNullOrWhiteSpace(
                                 existing.SocialSecurityNumber))
                    {
                        existing.SocialSecurityNumber =
                            socialSecurityNumber;
                    }

                    if (!string.IsNullOrWhiteSpace(
                            employeeData.ProfessionCode))
                    {
                        existing.ProfessionCode =
                            employeeData.ProfessionCode.Trim();
                    }
                    else if (string.IsNullOrWhiteSpace(
                                 existing.ProfessionCode))
                    {
                        existing.ProfessionCode =
                            DefaultProfessionCode;
                    }

                    /*
                     * Never overwrite a manually configured barcode.
                     */
                    if (string.IsNullOrWhiteSpace(
                            existing.BarcodeId))
                    {
                        existing.BarcodeId =
                            initialBarcode;
                    }

                    existing.WeeklyWorkdays =
                        weeklyWorkdays;

                    existing.IsActive =
                        true;

                    result.EmployeesUpdated++;

                    _logger.LogInformation(
                        "Updating employee AFM={Afm}, Branch={BranchNumber}.",
                        existing.TaxId,
                        branch.BranchNumber);
                }
            }

            await db.SaveChangesAsync(ct);

            _logger.LogInformation(
                "EX_BASE_05 database save completed. Added={Added}, Updated={Updated}.",
                result.EmployeesImported,
                result.EmployeesUpdated);
        }
        catch (Exception ex)
        {
            var message =
                GetFullExceptionMessage(ex);

            _logger.LogError(
                ex,
                "Employee import failed for CompanyId={CompanyId}.",
                companyId);

            result.Warnings.Add(
                $"Employee import failed: {message}");
        }
    }

    // =====================================================================
    // EXISTING COMPANY IMPORT
    // =====================================================================

    public async Task<ErganiImportResult> ImportCompanyDataAsync(
        int companyId,
        ErganiCredentials credentials,
        CancellationToken ct = default)
    {
        var result =
            new ErganiImportResult
            {
                CompanyId =
                    companyId
            };

        try
        {
            var company =
                await _companyService.GetByIdAsync(
                    companyId);

            if (company == null)
            {
                result.Success =
                    false;

                result.ErrorMessage =
                    $"Company {companyId} was not found.";

                return result;
            }

            result.CompanyId =
                company.Id;

            result.CompanyName =
                company.Name;

            result.CompanyTaxId =
                company.TaxId;

            _logger.LogInformation(
                "Starting branch and employee import for existing CompanyId={CompanyId}.",
                companyId);

            // EX_BASE_02
            await ImportBranchesAsync(
                companyId,
                credentials,
                result,
                ct);

            // EX_BASE_05
            await ImportEmployeesAsync(
                companyId,
                credentials,
                result,
                ct);

            result.Success =
                true;

            if (result.Warnings.Count > 0)
            {
                result.ErrorMessage =
                    string.Join(
                        Environment.NewLine,
                        result.Warnings);
            }

            return result;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            result.Success =
                false;

            result.ErrorMessage =
                GetFullExceptionMessage(ex);

            return result;
        }
    }

    // =====================================================================
    // EX_BASE_01 - COMPANY
    // =====================================================================

    private static EmployerImportRecord ExtractEmployer(
        JsonElement root)
    {
        var result =
            new EmployerImportRecord();

        if (!TryGetPropertyIgnoreCase(
                root,
                "EX_BASE_01",
                out var base01))
        {
            return result;
        }

        JsonElement employer =
            base01;

        if (TryGetPropertyIgnoreCase(
                base01,
                "Ergodotis",
                out var ergodotis))
        {
            employer =
                ergodotis;
        }

        result.TaxId =
            GetString(
                employer,
                "Afm",
                "AFM",
                "afm");

        result.Name =
            GetString(
                employer,
                "Eponimia",
                "eponimia",
                "Name");

        result.TradeName =
            GetString(
                employer,
                "DiakritikosTitlos");

        result.EmployerNumber =
            GetString(
                employer,
                "Ame");

        return result;
    }

    // =====================================================================
    // EX_BASE_02 - BRANCHES
    // =====================================================================

    private static List<BranchImportRecord> ParseBranches(
        JsonElement root)
    {
        var result =
            new List<BranchImportRecord>();

        if (!TryGetPropertyIgnoreCase(
                root,
                "EX_BASE_02",
                out var base02))
        {
            return result;
        }

        /*
         * Actual response observed:
         *
         * {
         *   "EX_BASE_02": {
         *     "Pararthma": {
         *       "Aa": "0",
         *       "Address": "..."
         *     }
         *   }
         * }
         *
         * Pararthma can therefore be either:
         *
         *     Object
         *
         * or:
         *
         *     Array
         */

        if (TryGetPropertyIgnoreCase(
                base02,
                "Pararthma",
                out var pararthma))
        {
            AddBranchElements(
                pararthma,
                result);

            return result;
        }

        /*
         * Fallback for alternative wrappers.
         */
        AddBranchElements(
            base02,
            result);

        return result;
    }

    private static void AddBranchElements(
        JsonElement element,
        List<BranchImportRecord> result)
    {
        if (element.ValueKind ==
            JsonValueKind.Array)
        {
            foreach (var item in
                     element.EnumerateArray())
            {
                if (item.ValueKind ==
                    JsonValueKind.Object)
                {
                    if (LooksLikeBranch(item))
                    {
                        result.Add(
                            ParseBranch(item));
                    }
                    else
                    {
                        AddBranchElements(
                            item,
                            result);
                    }
                }
            }

            return;
        }

        if (element.ValueKind !=
            JsonValueKind.Object)
        {
            return;
        }

        if (LooksLikeBranch(element))
        {
            result.Add(
                ParseBranch(element));

            return;
        }

        foreach (var property in
                 element.EnumerateObject())
        {
            if (property.Value.ValueKind ==
                    JsonValueKind.Object ||
                property.Value.ValueKind ==
                    JsonValueKind.Array)
            {
                AddBranchElements(
                    property.Value,
                    result);
            }
        }
    }

    private static BranchImportRecord ParseBranch(
        JsonElement element)
    {
        return new BranchImportRecord
        {
            /*
             * IMPORTANT:
             *
             * This is nullable so that:
             *
             *     "Aa": "0"
             *
             * becomes:
             *
             *     BranchNumber = 0
             *
             * while a completely missing Aa becomes:
             *
             *     BranchNumber = null
             */
            BranchNumber =
                GetNullableInt(
                    element,
                    "Aa",
                    "aa",
                    "F_Aa",
                    "f_aa"),

            Address =
                GetString(
                    element,
                    "Address",
                    "address"),

            SepeServiceCode =
                GetString(
                    element,
                    "YpiresiaSepe",
                    "sepe_code",
                    "SepeServiceCode"),

            OaedServiceCode =
                GetString(
                    element,
                    "YpiresiaOaed",
                    "oaed_code",
                    "OaedServiceCode"),

            ActivityCode =
                GetString(
                    element,
                    "Kad",
                    "kad",
                    "ActivityCode"),

            KallikratisMunicipalCode =
                GetString(
                    element,
                    "Kallikratis",
                    "KallikratisMunicipalCode")
        };
    }

    // =====================================================================
    // EX_BASE_05 - EMPLOYEES
    // =====================================================================

    private static List<EmployeeImportRecord> ParseEmployees(
        JsonElement root)
    {
        var result =
            new List<EmployeeImportRecord>();

        if (!TryGetPropertyIgnoreCase(
                root,
                "EX_BASE_05",
                out var base05))
        {
            return result;
        }

        /*
         * Expected:
         *
         * EX_BASE_05
         *     Cur
         *         [...]
         *
         * Cur can also be a single object.
         */

        if (TryGetPropertyIgnoreCase(
                base05,
                "Cur",
                out var cur))
        {
            AddEmployeeElements(
                cur,
                result);

            return result;
        }

        AddEmployeeElements(
            base05,
            result);

        return result;
    }

    private static void AddEmployeeElements(
        JsonElement element,
        List<EmployeeImportRecord> result)
    {
        if (element.ValueKind ==
            JsonValueKind.Array)
        {
            foreach (var item in
                     element.EnumerateArray())
            {
                if (item.ValueKind ==
                    JsonValueKind.Object)
                {
                    if (LooksLikeEmployee(item))
                    {
                        result.Add(
                            ParseEmployee(item));
                    }
                    else
                    {
                        AddEmployeeElements(
                            item,
                            result);
                    }
                }
            }

            return;
        }

        if (element.ValueKind !=
            JsonValueKind.Object)
        {
            return;
        }

        if (LooksLikeEmployee(element))
        {
            result.Add(
                ParseEmployee(element));

            return;
        }

        foreach (var property in
                 element.EnumerateObject())
        {
            if (property.Value.ValueKind ==
                    JsonValueKind.Object ||
                property.Value.ValueKind ==
                    JsonValueKind.Array)
            {
                AddEmployeeElements(
                    property.Value,
                    result);
            }
        }
    }

    private static EmployeeImportRecord ParseEmployee(
        JsonElement element)
    {
        return new EmployeeImportRecord
        {
            TaxId =
                GetString(
                    element,
                    "afm",
                    "AFM",
                    "Afm"),

            LastName =
                GetString(
                    element,
                    "Eponimo",
                    "eponymo",
                    "Eponymo"),

            FirstName =
                GetString(
                    element,
                    "Onoma",
                    "onoma"),

            Amka =
                GetString(
                    element,
                    "Amka",
                    "amka"),

            /*
             * This is nullable for the same reason as branch AA.
             *
             * Branch 0 is valid.
             */
            BranchNumber =
                GetNullableInt(
                    element,
                    "PararthmaAa",
                    "ParartimaAa",
                    "BranchNumber"),

            ProfessionCode =
                GetString(
                    element,
                    "Step",
                    "step",
                    "ProfessionCode"),

            WeeklyWorkdays =
                GetNullableInt(
                    element,
                    "WeekDays",
                    "WeeklyWorkdays")
        };
    }

    // =====================================================================
    // JSON DETECTION
    // =====================================================================

    private static bool LooksLikeBranch(
        JsonElement element)
    {
        return HasAnyProperty(
            element,
            "Aa",
            "aa",
            "F_Aa",
            "f_aa",
            "Address",
            "address",
            "YpiresiaSepe",
            "Kad",
            "Kallikratis");
    }

    private static bool LooksLikeEmployee(
        JsonElement element)
    {
        return HasAnyProperty(
            element,
            "afm",
            "AFM",
            "Afm",
            "Eponimo",
            "eponymo",
            "Eponymo",
            "Onoma",
            "Amka",
            "PararthmaAa",
            "ParartimaAa");
    }

    private static bool HasAnyProperty(
        JsonElement element,
        params string[] names)
    {
        if (element.ValueKind !=
            JsonValueKind.Object)
        {
            return false;
        }

        foreach (var name in names)
        {
            if (TryGetPropertyIgnoreCase(
                    element,
                    name,
                    out _))
            {
                return true;
            }
        }

        return false;
    }

    // =====================================================================
    // JSON PROPERTY HELPERS
    // =====================================================================

    private static bool TryGetPropertyIgnoreCase(
        JsonElement element,
        string propertyName,
        out JsonElement value)
    {
        if (element.ValueKind ==
            JsonValueKind.Object)
        {
            foreach (var property in
                     element.EnumerateObject())
            {
                if (string.Equals(
                        property.Name,
                        propertyName,
                        StringComparison.OrdinalIgnoreCase))
                {
                    value =
                        property.Value;

                    return true;
                }
            }
        }

        value =
            default;

        return false;
    }

    private static string? GetString(
        JsonElement element,
        params string[] propertyNames)
    {
        foreach (var propertyName in
                 propertyNames)
        {
            if (!TryGetPropertyIgnoreCase(
                    element,
                    propertyName,
                    out var value))
            {
                continue;
            }

            if (value.ValueKind ==
                JsonValueKind.String)
            {
                return value.GetString();
            }

            if (value.ValueKind ==
                    JsonValueKind.Number ||
                value.ValueKind ==
                    JsonValueKind.True ||
                value.ValueKind ==
                    JsonValueKind.False)
            {
                return value.ToString();
            }
        }

        return null;
    }

    private static int? GetNullableInt(
        JsonElement element,
        params string[] propertyNames)
    {
        foreach (var propertyName in
                 propertyNames)
        {
            if (!TryGetPropertyIgnoreCase(
                    element,
                    propertyName,
                    out var value))
            {
                continue;
            }

            if (value.ValueKind ==
                    JsonValueKind.Number &&
                value.TryGetInt32(
                    out var number))
            {
                return number;
            }

            if (value.ValueKind ==
                JsonValueKind.String)
            {
                var text =
                    value.GetString();

                if (int.TryParse(
                        text,
                        out var parsed))
                {
                    return parsed;
                }
            }

            /*
             * Handle a numeric value represented as something such as:
             *
             * "0.0"
             */
            if (value.ValueKind ==
                JsonValueKind.String)
            {
                var text =
                    value.GetString();

                if (decimal.TryParse(
                        text,
                        out var decimalValue))
                {
                    return (int)decimalValue;
                }
            }
        }

        return null;
    }

    // =====================================================================
    // VALUE HELPERS
    // =====================================================================

    private static string DefaultIfEmpty(
        string? value,
        string defaultValue)
    {
        return string.IsNullOrWhiteSpace(value)
            ? defaultValue
            : value.Trim();
    }

    private static string? EmptyToNull(
        string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim();
    }

    private static string NormalizeTaxId(
        string? taxId)
    {
        if (string.IsNullOrWhiteSpace(taxId))
        {
            return string.Empty;
        }

        return new string(
            taxId.Where(char.IsDigit).ToArray());
    }

    private static string GetFullExceptionMessage(
        Exception exception)
    {
        var messages =
            new List<string>();

        Exception? current =
            exception;

        while (current != null)
        {
            if (!string.IsNullOrWhiteSpace(
                    current.Message))
            {
                messages.Add(
                    current.Message);
            }

            current =
                current.InnerException;
        }

        return string.Join(
            " -> ",
            messages.Distinct());
    }

    // =====================================================================
    // INTERNAL MODELS
    // =====================================================================

    private sealed class EmployerImportRecord
    {
        public string? TaxId { get; set; }

        public string? Name { get; set; }

        public string? TradeName { get; set; }

        public string? EmployerNumber { get; set; }
    }

    private sealed class BranchImportRecord
    {
        /*
         * IMPORTANT:
         *
         * Nullable because we must distinguish:
         *
         *     AA = 0      -> VALID
         *
         * from:
         *
         *     AA missing  -> INVALID
         */
        public int? BranchNumber { get; set; }

        public string? Address { get; set; }

        public string? SepeServiceCode { get; set; }

        public string? OaedServiceCode { get; set; }

        public string? ActivityCode { get; set; }

        public string? KallikratisMunicipalCode { get; set; }
    }

    private sealed class EmployeeImportRecord
    {
        public string? TaxId { get; set; }

        public string? LastName { get; set; }

        public string? FirstName { get; set; }

        public string? Amka { get; set; }

        public int? BranchNumber { get; set; }

        public string? ProfessionCode { get; set; }

        public int? WeeklyWorkdays { get; set; }
    }
}