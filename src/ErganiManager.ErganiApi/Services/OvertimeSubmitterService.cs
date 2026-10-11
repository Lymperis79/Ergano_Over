using ErganiManager.Core.Interfaces;
using ErganiManager.Data;
using ErganiManager.Data.Entities;
using ErganiManager.ErganiApi.Models;
using Microsoft.EntityFrameworkCore;

namespace ErganiManager.ErganiApi.Services;

public sealed class OvertimeSubmitterService : IOvertimeSubmitter
{
    private readonly IErganiClient _client;
    private readonly IConnectionStateService _connectionState;
    private readonly ICredentialProtector _protector;

    public OvertimeSubmitterService(IErganiClient client, IConnectionStateService connectionState, ICredentialProtector protector)
    { _client = client; _connectionState = connectionState; _protector = protector; }

    public async Task<OvertimeBatchSubmissionResult> SubmitAsync(int companyId, IReadOnlyList<int> overtimeIds, CancellationToken ct = default)
    {
        if (overtimeIds.Count == 0) return new() { ErrorMessage = "No overtime records selected." };
        await using var db = new AppDbContext(_connectionState.GetDbOptions());
        var company = await db.Companies.FindAsync(new object[] { companyId }, ct);
        if (company == null) return new() { ErrorMessage = "Company not found." };
        var records = await db.Overtimes.Include(x => x.Employee).Include(x => x.Branch)
            .Where(x => overtimeIds.Contains(x.Id) && x.Employee != null && x.Employee.CompanyId == companyId && !x.IsCancelled && !x.SubmittedToErgani)
            .ToListAsync(ct);
        if (records.Count == 0) return new() { ErrorMessage = "No eligible unsent overtime records were selected." };

        // Records past the 2-hour cutoff are skipped (and reported); the rest are still sent.
        var errors = new Dictionary<int, string>();
        var eligible = new List<Overtime>();
        foreach (var r in records)
        {
            var cutoff = r.OvertimeDate.ToDateTime(r.EndTime).AddHours(-2);
            if (DateTime.Now >= cutoff)
                errors[r.Id] = $"Overtime for {r.Employee?.FullName ?? "employee"} ending at {r.EndTime:HH:mm} cannot be submitted now. The 2-hour submission cutoff was {cutoff:dd/MM/yyyy HH:mm}.";
            else eligible.Add(r);
        }
        if (eligible.Count == 0)
            return new() { ErrorMessage = string.Join(" ", errors.Values), Errors = errors };

        var credentials = new ErganiCredentials { Username = company.ErganiUsername, Password = _protector.Unprotect(company.ErganiPasswordEncrypted), Usertype = company.ErganiUsertype, BaseUrl = company.ErganiBaseUrl };
        var submittedIds = new List<int>(); string? lastProtocol = null; string? lastSubmissionId = null; string? lastResponse = null;

        // ONE request per branch containing every selected employee/day (the Ergani WTOOv body carries
        // many employees and dates under one branch header; the justification is not part of it).
        foreach (var group in eligible.GroupBy(x => x.BranchId))
        {
            var branch = group.First().Branch;
            if (branch == null)
            {
                foreach (var g in group) errors[g.Id] = "Branch not found.";
                continue;
            }
            var submission = new CompanyOvertimeSubmission
            {
                BusinessBranchNumber = branch.BranchNumber, SepeServiceCode = branch.SepeServiceCode,
                BusinessPrimaryActivityCode = branch.ActivityCode, BusinessBranchActivityCode = branch.ActivityCode,
                KallikratisMunicipalCode = branch.KallikratisMunicipalCode,
                LegalRepresentativeTaxIdentificationNumber = company.TaxId,
                EmployeeOvertimes = group.OrderBy(x => x.OvertimeDate).ThenBy(x => x.StartTime).Select(x => new OvertimeEntry
                {
                    EmployeeTaxIdentificationNumber = x.Employee!.TaxId, EmployeeSocialSecurityNumber = x.Employee.SocialSecurityNumber, EmployeeProfessionCode = x.Employee.ProfessionCode, EmployeeLastName = x.Employee.LastName, EmployeeFirstName = x.Employee.FirstName,
                    OvertimeDate = x.OvertimeDate, StartTime = x.StartTime, EndTime = x.EndTime, Justification = MapJustification(x.Justification),
                    WeeklyWorkdaysNumber = x.WeeklyWorkdaysNumber, AseeApproval = x.AseeApproval
                }).ToList()
            };
            var result = await _client.SubmitOvertimeAsync(credentials, new List<CompanyOvertimeSubmission> { submission }, ct);
            var response = result.Data?.FirstOrDefault();
            var success = result.Success && response?.IsBusinessError != true && !string.IsNullOrWhiteSpace(response?.Protocol);
            var protocol = response?.Protocol; var submissionId = response?.SubmissionId;
            var error = success ? null : (result.ErrorMessage ?? response?.Description ?? "Ergani overtime submission failed.");
            var submittedDate = DateOnly.FromDateTime(DateTime.Today);
            foreach (var entity in group)
            {
                entity.SubmittedToErgani = success; entity.Protocol = protocol; entity.SubmissionId = submissionId;
                entity.ResponseRawJson = result.ResponseRawJson; entity.RequestPayloadJson = result.RequestPayloadJson; entity.SubmittedDate = success ? submittedDate : null; entity.HttpStatusCode = result.HttpStatusCode;
                if (success) submittedIds.Add(entity.Id); else errors[entity.Id] = error!;
            }
            var employeeIds = group.Select(x => x.EmployeeId).Distinct().ToList();
            db.ApiSubmissionLogs.Add(new ApiSubmissionLog
            { CompanyId = companyId, EmployeeId = employeeIds.Count == 1 ? employeeIds[0] : null, SubmissionType = "Overtime", RequestPayloadJson = result.RequestPayloadJson, ResponseRawJson = result.ResponseRawJson, SubmissionId = submissionId, Protocol = protocol, SubmissionDate = DateTime.UtcNow, HttpStatusCode = result.HttpStatusCode, Success = success, ErrorMessage = error, DurationMs = result.DurationMs });
            await db.SaveChangesAsync(ct);
            lastProtocol = protocol; lastSubmissionId = submissionId; lastResponse = result.ResponseRawJson;
        }

        return new()
        {
            Success = errors.Count == 0 && submittedIds.Count > 0,
            SubmittedCount = submittedIds.Count, SubmittedIds = submittedIds, Errors = errors,
            Protocol = lastProtocol, SubmissionId = lastSubmissionId, ResponseRawJson = lastResponse,
            ErrorMessage = errors.Count == 0 ? null : string.Join(" | ", errors.Values.Distinct())
        };
    }

    private static ApiOvertimeJustification MapJustification(OvertimeJustification value) => value switch
    {
        OvertimeJustification.AccidentPreventionOrDamageRestoration => ApiOvertimeJustification.ACCIDENT_PREVENTION_OR_DAMAGE_RESTORATION,
        OvertimeJustification.UrgentSeasonalTasks => ApiOvertimeJustification.URGENT_SEASONAL_TASKS,
        OvertimeJustification.ExceptionalWorkload => ApiOvertimeJustification.EXCEPTIONAL_WORKLOAD,
        OvertimeJustification.SupplementaryTasks => ApiOvertimeJustification.SUPPLEMENTARY_TASKS,
        OvertimeJustification.LostHoursSuddenCauses => ApiOvertimeJustification.LOST_HOURS_SUDDEN_CAUSES,
        OvertimeJustification.LostHoursOfficialHolidays => ApiOvertimeJustification.LOST_HOURS_OFFICIAL_HOLIDAYS,
        OvertimeJustification.LostHoursWeatherConditions => ApiOvertimeJustification.LOST_HOURS_WEATHER_CONDITIONS,
        OvertimeJustification.EmergencyClosureDay => ApiOvertimeJustification.EMERGENCY_CLOSURE_DAY,
        OvertimeJustification.NonWorkdayTasks => ApiOvertimeJustification.NON_WORKDAY_TASKS,
        _ => ApiOvertimeJustification.EXCEPTIONAL_WORKLOAD
    };
}
