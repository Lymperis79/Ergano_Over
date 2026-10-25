using ErganiManager.Core.Interfaces;
using ErganiManager.Data;
using ErganiManager.Data.Entities;
using ErganiManager.ErganiApi.Models;
using Microsoft.EntityFrameworkCore;

namespace ErganiManager.ErganiApi.Services;

/// <summary>Submits holidays (leave) to Ergani — Documents/WTOLeave.</summary>
public sealed class LeaveSubmitterService : ILeaveSubmitter
{
    private readonly IErganiClient _client;
    private readonly IConnectionStateService _connectionState;
    private readonly ICredentialProtector _protector;

    public LeaveSubmitterService(IErganiClient client, IConnectionStateService connectionState, ICredentialProtector protector)
    { _client = client; _connectionState = connectionState; _protector = protector; }

    public async Task<LeaveBatchSubmissionResult> SubmitAsync(
        int companyId, IReadOnlyList<int> leaveIds, CancellationToken ct = default)
    {
        var outcome = new LeaveBatchSubmissionResult();
        if (leaveIds.Count == 0) { outcome.ErrorMessage = ErganiManager.Data.UiText.T("No holidays selected."); return outcome; }

        await using var db = new AppDbContext(_connectionState.GetDbOptions());

        var company = await db.Companies.FindAsync(new object[] { companyId }, ct);
        if (company == null) { outcome.ErrorMessage = ErganiManager.Data.UiText.T("Company not found."); return outcome; }

        var records = await db.Leaves
            .Include(l => l.Employee).Include(l => l.Branch)
            .Where(l => leaveIds.Contains(l.Id) && l.Employee != null
                     && l.Employee.CompanyId == companyId && !l.SubmittedToErgani)
            .ToListAsync(ct);
        if (records.Count == 0) { outcome.ErrorMessage = ErganiManager.Data.UiText.T("There are no unsent holidays to submit."); return outcome; }

        var credentials = new ErganiCredentials
        {
            Username = company.ErganiUsername,
            Password = _protector.Unprotect(company.ErganiPasswordEncrypted),
            Usertype = company.ErganiUsertype,
            BaseUrl  = company.ErganiBaseUrl
        };

        // ONE Ergani submission per branch (and WTO comment). The WTOLeave body carries many employees,
        // days and leave types under one branch header (type, hours, year and entitled days are per
        // employee and day), so every pending holiday of the branch gets the same protocol.
        var groups = records
            .GroupBy(l => new { l.BranchId, Comments = l.Comments ?? string.Empty })
            .OrderBy(g => g.Min(l => l.LeaveDate))
            .ToList();

        var stopped = false;   // Ergani unreachable — don't wait for a timeout on every remaining group

        foreach (var group in groups)
        {
            var items = group.OrderBy(l => l.LeaveDate).ThenBy(l => l.EmployeeId).ToList();

            if (stopped)
            {
                Fail(items, ErganiManager.Data.UiText.T("Not sent — Ergani was unreachable."));
                outcome.FailedCount += items.Count;
                outcome.ErrorMessage ??= ErganiManager.Data.UiText.T("Not sent — Ergani was unreachable.");
                await db.SaveChangesAsync(ct);
                continue;
            }

            var branch = items[0].Branch;
            if (branch == null)
            {
                Fail(items, ErganiManager.Data.UiText.T("Branch not found."));
                outcome.FailedCount += items.Count;
                outcome.ErrorMessage ??= ErganiManager.Data.UiText.T("Branch not found.");
                await db.SaveChangesAsync(ct);
                continue;
            }

            var submission = new CompanyLeaveSubmission
            {
                BusinessBranchNumber = branch.BranchNumber,
                Comments             = items[0].Comments ?? string.Empty,
                Entries = items.Select(l => new LeaveEntry
                {
                    EmployeeTaxIdentificationNumber = l.Employee!.TaxId,
                    EmployeeLastName                = l.Employee.LastName,
                    EmployeeFirstName               = l.Employee.FirstName,
                    Date                            = l.LeaveDate,
                    TypeCode                        = l.LeaveTypeCode,
                    From                            = l.StartTime,
                    To                              = l.EndTime,
                    ReferenceYear                   = l.ReferenceYear,
                    EntitledDays                    = l.EntitledDays
                }).ToList()
            };

            var result   = await _client.SubmitLeaveAsync(credentials, new List<CompanyLeaveSubmission> { submission }, ct);
            var response = result.Data?.FirstOrDefault();

            // A 200 answer with a description but no protocol is an Ergani business error.
            var businessError = result.IsBusinessError || response is { IsBusinessError: true };
            var success       = result.Success && !businessError;
            var error         = success
                ? null
                : (result.ErrorMessage ?? response?.Description ?? ErganiManager.Data.UiText.T("Ergani leave submission failed."));

            var submittedDate = DateOnly.FromDateTime(DateTime.Today);
            foreach (var l in items)
            {
                l.SubmittedToErgani  = success;
                l.Protocol           = response?.Protocol;
                l.SubmissionId       = response?.SubmissionId;
                l.ResponseRawJson    = result.ResponseRawJson;
                l.RequestPayloadJson = result.RequestPayloadJson;
                l.HttpStatusCode     = result.HttpStatusCode;
                l.SubmittedDate      = success ? submittedDate : null;
                l.LastError          = error;
            }

            var employeeIds = items.Select(l => l.EmployeeId).Distinct().ToList();
            db.ApiSubmissionLogs.Add(new ApiSubmissionLog
            {
                CompanyId          = companyId,
                EmployeeId         = employeeIds.Count == 1 ? employeeIds[0] : null,
                EmployeeIdsJson    = System.Text.Json.JsonSerializer.Serialize(employeeIds),
                SubmissionType     = "Leave",
                ScheduleDate       = items[0].LeaveDate,
                RequestPayloadJson = result.RequestPayloadJson,
                ResponseRawJson    = result.ResponseRawJson,
                SubmissionId       = response?.SubmissionId,
                Protocol           = response?.Protocol,
                SubmissionDate     = DateTime.UtcNow,
                HttpStatusCode     = result.HttpStatusCode,
                Success            = success,
                ErrorMessage       = error,
                DurationMs         = result.DurationMs
            });

            await db.SaveChangesAsync(ct);

            if (success)
            {
                outcome.SubmittedCount += items.Count;
                outcome.Protocol = response?.Protocol;
            }
            else
            {
                outcome.FailedCount += items.Count;
                outcome.ErrorMessage ??= error;
                if (result.IsServiceUnavailable) stopped = true;
            }
        }

        return outcome;
    }

    private static void Fail(IEnumerable<Leave> items, string message)
    {
        foreach (var l in items) l.LastError = message;
    }
}
