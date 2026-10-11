using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ErganiManager.Core.Interfaces;
using ErganiManager.Core.Models;
using ErganiManager.Data;
using ErganiManager.Data.Entities;
using ErganiManager.ErganiApi.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ErganiManager.ErganiApi.Services;

public class ScheduleSubmitterService : IScheduleSubmitter
{
    private readonly IErganiClient           _erganiClient;
    private readonly IConnectionStateService _connectionState;
    private readonly ICredentialProtector    _credentialProtector;
    private readonly IOvertimeSubmitter      _overtimeSubmitter;
    private readonly ILogger<ScheduleSubmitterService> _logger;

    public ScheduleSubmitterService(
        IErganiClient erganiClient,
        IConnectionStateService connectionState,
        ICredentialProtector credentialProtector,
        IOvertimeSubmitter overtimeSubmitter,
        ILogger<ScheduleSubmitterService> logger)
    {
        _overtimeSubmitter   = overtimeSubmitter;
        _erganiClient        = erganiClient;
        _connectionState     = connectionState;
        _credentialProtector = credentialProtector;
        _logger              = logger;
    }

    private AppDbContext OpenDb() => new AppDbContext(_connectionState.GetDbOptions());

    public async Task<ScheduleSubmitResult> SubmitScheduleDaysAsync(
        int companyId, IReadOnlyList<ScheduleDayDto> days)
    {
        var outcome = new ScheduleSubmitResult();
        if (days.Count == 0) return outcome;

        await using var db = OpenDb();
        var company = await db.Companies.FindAsync(companyId);
        if (company == null)
        {
            outcome.Error = "Company not found.";
            return outcome;
        }

        var credentials = BuildCredentials(company);
        var stopped     = false;   // Ergani unreachable — no point trying the remaining dates
        var overtimeQueue = new List<(int Id, ScheduleSubmitDayResult Day)>();

        foreach (var branchGroup in days.GroupBy(d => d.BranchId))
        {
            var branch = await db.Branches.FindAsync(branchGroup.Key);
            if (branch == null)
            {
                foreach (var d in branchGroup)
                    AddDayResult(outcome, d, false, null, "Branch not found.");
                continue;
            }

            var employeeIds = branchGroup.Select(d => d.EmployeeId).Distinct().ToList();
            var employees   = await db.Employees
                .Where(e => employeeIds.Contains(e.Id)).ToListAsync();

            // One Ergani submission per date.
            foreach (var dateGroup in branchGroup.GroupBy(d => d.ScheduleDate).OrderBy(g => g.Key))
            {
                if (stopped)
                {
                    foreach (var d in dateGroup)
                        AddDayResult(outcome, d, false, null, "Not sent — Ergani was unreachable.");
                    continue;
                }

                var scheduleEntries = dateGroup
                    .Join(employees, d => d.EmployeeId, e => e.Id,
                        (d, e) => new EmployeeDailySchedule
                        {
                            EmployeeTaxIdentificationNumber = e.TaxId,
                            EmployeeLastName                = e.LastName,
                            EmployeeFirstName               = e.FirstName,
                            ScheduleDate                    = d.ScheduleDate,
                            WorkdayDetails                  = BuildWorkdayDetails(d)
                        })
                    .ToList();

                if (scheduleEntries.Count == 0)
                {
                    foreach (var d in dateGroup)
                        AddDayResult(outcome, d, false, null, "Employee not found.");
                    continue;
                }

                var submission = new CompanyDailyScheduleSubmission
                {
                    EmployerTaxIdentificationNumber = company.TaxId,
                    BusinessBranchNumber            = branch.BranchNumber,
                    SepeServiceCode                 = branch.SepeServiceCode,
                    BusinessPrimaryActivityCode     = branch.ActivityCode,
                    KallikratisMunicipalCode        = branch.KallikratisMunicipalCode,
                    EmployeeSchedules               = scheduleEntries
                };

                var result   = await _erganiClient.SubmitDailyScheduleAsync(
                    credentials, new List<CompanyDailyScheduleSubmission> { submission });
                var response = result.Data?.FirstOrDefault();

                // A 200 response that carries a description but no protocol is an Ergani business error.
                var businessError = result.IsBusinessError || response is { IsBusinessError: true };
                var success       = result.Success && !businessError;
                var error         = success
                    ? null
                    : (result.ErrorMessage ?? response?.Description ?? "Ergani schedule submission failed.");

                // Audit log — one row per attempt, successful or not (same table the work cards use).
                var distinctEmployees = dateGroup.Select(d => d.EmployeeId).Distinct().ToList();
                db.ApiSubmissionLogs.Add(new ApiSubmissionLog
                {
                    CompanyId          = companyId,
                    EmployeeId         = distinctEmployees.Count == 1 ? distinctEmployees[0] : null,
                    SubmissionType     = "DailySchedule",
                    ScheduleDate       = dateGroup.Key,
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

                if (success)
                {
                    foreach (var day in dateGroup)
                    {
                        var entity = await db.Schedules.FindAsync(day.Id);
                        if (entity != null)
                        {
                            entity.SubmittedToErgani = true;
                            entity.SubmissionId      = response?.SubmissionId;
                            entity.Protocol          = response?.Protocol;
                        }
                    }
                }

                await db.SaveChangesAsync();

                foreach (var day in dateGroup)
                {
                    var dayResult = AddDayResult(outcome, day, success, response?.Protocol, error);

                    // Hours over 8: the schedule above carried only the first 8h — send the rest as overtime.
                    if (success && ScheduleHours.GetOvertimeRange(day.StartTime, day.EndTime) is { } range
                        && day.WorkType is AppWorkType.Office or AppWorkType.Home)
                    {
                        if (await PrepareOvertimeAsync(db, day, range, dayResult, outcome) is int otId)
                            overtimeQueue.Add((otId, dayResult));
                    }
                }

                if (!success)
                {
                    _logger.LogWarning("Schedule submit failed for branch {B} date {D}: {E}",
                        branch.Id, dateGroup.Key, error);

                    if (result.IsServiceUnavailable) stopped = true;
                }
            }
        }

        // Hours over 8 of all the days above: ONE overtime request per branch.
        await SubmitOvertimeBatchAsync(companyId, overtimeQueue, outcome);

        return outcome;
    }

    private static ScheduleSubmitDayResult AddDayResult(
        ScheduleSubmitResult outcome, ScheduleDayDto day, bool success, string? protocol, string? error)
    {
        var result = new ScheduleSubmitDayResult
        {
            EmployeeId = day.EmployeeId,
            Date       = day.ScheduleDate,
            Success    = success,
            Protocol   = protocol,
            Error      = error
        };
        outcome.Days.Add(result);
        if (success) outcome.Submitted++; else outcome.Failed++;
        return result;
    }

    /// <summary>Creates (or reuses) the Overtime record for the hours over 8. Returns its id when it
    /// still has to be sent to Ergani, or null when it was already sent / could not be prepared
    /// (the day result and counters are updated in that case).</summary>
    private async Task<int?> PrepareOvertimeAsync(
        AppDbContext db, ScheduleDayDto day,
        (TimeOnly From, TimeOnly To) range, ScheduleSubmitDayResult dayResult, ScheduleSubmitResult outcome)
    {
        dayResult.OvertimeFrom = range.From;
        dayResult.OvertimeTo   = range.To;

        try
        {
            var existing = await db.Overtimes.FirstOrDefaultAsync(o =>
                o.EmployeeId == day.EmployeeId && o.OvertimeDate == day.ScheduleDate &&
                o.StartTime == range.From && o.EndTime == range.To && !o.IsCancelled);

            if (existing is { SubmittedToErgani: true })
            {
                dayResult.OvertimeSubmitted = true;
                dayResult.OvertimeProtocol  = existing.Protocol;
                outcome.OvertimeSubmitted++;
                return null;
            }

            if (existing == null)
            {
                var employee = await db.Employees.FindAsync(day.EmployeeId);
                if (employee == null)
                {
                    dayResult.OvertimeError = "Employee not found.";
                    outcome.OvertimeFailed++;
                    return null;
                }

                existing = new Overtime
                {
                    EmployeeId           = day.EmployeeId,
                    BranchId             = day.BranchId,
                    OvertimeDate         = day.ScheduleDate,
                    StartTime            = range.From,
                    EndTime              = range.To,
                    Justification        = OvertimeJustification.ExceptionalWorkload,
                    WeeklyWorkdaysNumber = employee.WeeklyWorkdays,
                    CreatedAt            = DateTime.UtcNow
                };
                db.Overtimes.Add(existing);
                await db.SaveChangesAsync();
            }

            return existing.Id;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Overtime part of schedule {D} failed for employee {E}", day.ScheduleDate, day.EmployeeId);
            dayResult.OvertimeError = ex.Message;
            outcome.OvertimeFailed++;
            return null;
        }
    }

    /// <summary>Sends all queued overtime records through the normal overtime path in one request per
    /// branch (so it is logged and, if Ergani refuses it, can be retried from the Overtime page).</summary>
    private async Task SubmitOvertimeBatchAsync(
        int companyId, List<(int Id, ScheduleSubmitDayResult Day)> queue, ScheduleSubmitResult outcome)
    {
        if (queue.Count == 0) return;

        try
        {
            var ot = await _overtimeSubmitter.SubmitAsync(companyId, queue.Select(q => q.Id).ToList());
            var accepted = ot.SubmittedIds.ToHashSet();

            foreach (var (id, day) in queue)
            {
                if (accepted.Contains(id))
                {
                    day.OvertimeSubmitted = true;
                    day.OvertimeProtocol  = ot.Protocol;
                    outcome.OvertimeSubmitted++;
                }
                else
                {
                    day.OvertimeError = ot.Errors.TryGetValue(id, out var reason)
                        ? reason
                        : (ot.ErrorMessage ?? "Overtime submission failed.");
                    outcome.OvertimeFailed++;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Batch overtime submission failed");
            foreach (var (_, day) in queue) { day.OvertimeError = ex.Message; outcome.OvertimeFailed++; }
        }
    }

    public async Task<ScheduleSubmitResult> SubmitOvertimeFromScheduleAsync(
        int companyId, IReadOnlyList<ScheduleDayDto> days)
    {
        var outcome = new ScheduleSubmitResult();
        if (days.Count == 0) return outcome;

        await using var db = OpenDb();
        var queue = new List<(int Id, ScheduleSubmitDayResult Day)>();

        foreach (var day in days.Where(d => d.BranchId > 0).OrderBy(d => d.ScheduleDate))
        {
            var scheduled = day.WorkType is AppWorkType.Office or AppWorkType.Home
                ? ScheduleHours.GetOvertimeRange(day.StartTime, day.EndTime)
                : null;
            var range = scheduled ?? GetClockedOvertimeRange(day);
            if (range is not { } r) continue;

            var dayResult = new ScheduleSubmitDayResult
            {
                EmployeeId = day.EmployeeId,
                Date       = day.ScheduleDate,
                Success    = true
            };
            outcome.Days.Add(dayResult);

            if (await PrepareOvertimeAsync(db, day, r, dayResult, outcome) is int otId)
                queue.Add((otId, dayResult));
        }

        // One request per branch with every selected employee/day
        await SubmitOvertimeBatchAsync(companyId, queue, outcome);

        return outcome;
    }

    /// <summary>Fallback when the schedule has no hours over 8: use the clocked-in times.</summary>
    private static (TimeOnly From, TimeOnly To)? GetClockedOvertimeRange(ScheduleDayDto day)
    {
        if (!day.ActualArrival.HasValue || !day.ActualDeparture.HasValue) return null;
        if ((day.ActualDeparture.Value - day.ActualArrival.Value).TotalHours <= ScheduleHours.NormalDailyHours) return null;

        var from = TimeOnly.FromDateTime(day.ActualArrival.Value.AddHours(ScheduleHours.NormalDailyHours));
        var to   = TimeOnly.FromDateTime(day.ActualDeparture.Value);
        return from < to ? (from, to) : null;
    }

    public async Task<(int Submitted, string? Error)> SubmitOvertimeDaysAsync(
        int companyId, IReadOnlyList<ScheduleDayDto> overtimeDays)
    {
        if (overtimeDays.Count == 0) return (0, null);

        await using var db = OpenDb();
        var company = await db.Companies.FindAsync(companyId);
        if (company == null) return (0, "Company not found.");

        var credentials = BuildCredentials(company);
        int submitted   = 0;

        foreach (var day in overtimeDays)
        {
            if (!day.ActualArrival.HasValue || !day.ActualDeparture.HasValue) continue;

            var employee = await db.Employees.FindAsync(day.EmployeeId);
            var branch   = await db.Branches.FindAsync(day.BranchId);
            if (employee == null || branch == null) continue;

            var overtimeStart = day.ActualArrival.Value.AddHours(8);
            var overtimeEnd   = day.ActualDeparture.Value;

            var submission = new CompanyOvertimeSubmission
            {
                BusinessBranchNumber        = branch.BranchNumber,
                SepeServiceCode             = branch.SepeServiceCode,
                BusinessPrimaryActivityCode = branch.ActivityCode,
                BusinessBranchActivityCode  = branch.ActivityCode,
                KallikratisMunicipalCode    = branch.KallikratisMunicipalCode,
                LegalRepresentativeTaxIdentificationNumber = company.TaxId,
                EmployeeOvertimes = new List<OvertimeEntry>
                {
                    new()
                    {
                        EmployeeTaxIdentificationNumber = employee.TaxId,
                        EmployeeSocialSecurityNumber    = employee.SocialSecurityNumber,
                        EmployeeProfessionCode          = employee.ProfessionCode,
                        EmployeeLastName                = employee.LastName,
                        EmployeeFirstName               = employee.FirstName,
                        OvertimeDate                    = day.ScheduleDate,
                        StartTime                       = TimeOnly.FromDateTime(overtimeStart),
                        EndTime                         = TimeOnly.FromDateTime(overtimeEnd),
                        Justification                   = ApiOvertimeJustification.EXCEPTIONAL_WORKLOAD,
                        WeeklyWorkdaysNumber            = employee.WeeklyWorkdays
                    }
                }
            };

            var result = await _erganiClient.SubmitOvertimeAsync(
                credentials, new List<CompanyOvertimeSubmission> { submission });

            if (result.Success)
            {
                var protocol = result.Data?.FirstOrDefault()?.Protocol;
                db.Overtimes.Add(new Overtime
                {
                    EmployeeId           = employee.Id,
                    BranchId             = branch.Id,
                    OvertimeDate         = day.ScheduleDate,
                    StartTime            = TimeOnly.FromDateTime(overtimeStart),
                    EndTime              = TimeOnly.FromDateTime(overtimeEnd),
                    Justification        = OvertimeJustification.ExceptionalWorkload,
                    WeeklyWorkdaysNumber = employee.WeeklyWorkdays,
                    SubmittedToErgani    = true,
                    Protocol             = protocol,
                    SubmissionId         = result.Data?.FirstOrDefault()?.SubmissionId,
                    CreatedAt            = DateTime.UtcNow
                });
                await db.SaveChangesAsync();
                submitted++;
            }
            else
            {
                _logger.LogWarning("Overtime submit failed for employee {E} on {D}: {Err}",
                    employee.Id, day.ScheduleDate, result.ErrorMessage);
                return (submitted, result.ErrorMessage);
            }
        }

        return (submitted, null);
    }

    private ErganiCredentials BuildCredentials(Data.Entities.Company c) => new()
    {
        Username = c.ErganiUsername,
        Password = _credentialProtector.Unprotect(c.ErganiPasswordEncrypted),
        Usertype = c.ErganiUsertype,
    BaseUrl  = c.ErganiBaseUrl
    };

    private static List<WorkdayDetails> BuildWorkdayDetails(ScheduleDayDto day)
    {
        if (day.StartTime == null || day.EndTime == null) return new();

        // Over 8 hours: the schedule carries the first 8h only, the rest goes in as overtime.
        var endTime = ScheduleHours.GetOvertimeRange(day.StartTime, day.EndTime)?.From ?? day.EndTime.Value;

        return new()
        {
            new()
            {
                WorkDayType = day.WorkType switch
                {
                    AppWorkType.Home   => "ΤΗΛ",
                    AppWorkType.Office => "ΕΡΓ",
                    AppWorkType.Rest   => "ΑΝ",
                    AppWorkType.Absent => "ΜΕ",
                    _                  => "ΕΡΓ"
                },
                StartTime = day.StartTime.Value,
                EndTime   = endTime
            }
        };
    }
}
