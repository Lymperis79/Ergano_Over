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
        int companyId, IReadOnlyList<ScheduleDayDto> days, string overtimeJustification = "ExceptionalWorkload")
    {
        var outcome = new ScheduleSubmitResult();
        if (days.Count == 0) return outcome;

        await using var db = OpenDb();
        var company = await db.Companies.FindAsync(companyId);
        if (company == null)
        {
            outcome.Error = ErganiManager.Data.UiText.T("Company not found.");
            return outcome;
        }

        var credentials = BuildCredentials(company);
        var stopped     = false;   // Ergani unreachable — no point trying the remaining dates

        foreach (var branchGroup in days.GroupBy(d => d.BranchId))
        {
            var branch = await db.Branches.FindAsync(branchGroup.Key);
            if (branch == null)
            {
                foreach (var d in branchGroup)
                    AddDayResult(outcome, d, false, null, ErganiManager.Data.UiText.T("Branch not found."));
                continue;
            }

            var employeeIds = branchGroup.Select(d => d.EmployeeId).Distinct().ToList();
            var employees   = await db.Employees
                .Where(e => employeeIds.Contains(e.Id)).ToListAsync();

            // One Ergani submission per branch. WTODaily supports multiple employees and dates
            // in the same WTO item, with each employee/date carrying its own analytics.
            var allDays = branchGroup.OrderBy(d => d.ScheduleDate).ThenBy(d => d.EmployeeId).ToList();
            if (stopped)
            {
                foreach (var d in allDays)
                    AddDayResult(outcome, d, false, null, ErganiManager.Data.UiText.T("Not sent — Ergani was unreachable."));
                continue;
            }

            var scheduleEntries = allDays
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
                foreach (var d in allDays)
                    AddDayResult(outcome, d, false, null, ErganiManager.Data.UiText.T("Employee not found."));
                continue;
            }

            var submission = new CompanyDailyScheduleSubmission
            {
                EmployerTaxIdentificationNumber = company.TaxId,
                BusinessBranchNumber            = branch.BranchNumber,
                SepeServiceCode                 = branch.SepeServiceCode,
                BusinessPrimaryActivityCode     = branch.ActivityCode,
                BusinessBranchActivityCode      = branch.ActivityCode,
                KallikratisMunicipalCode        = branch.KallikratisMunicipalCode,
                EmployeeSchedules               = scheduleEntries
            };

            var result   = await _erganiClient.SubmitDailyScheduleAsync(
                credentials, new List<CompanyDailyScheduleSubmission> { submission });
            var response = result.Data?.FirstOrDefault();

            // A 200 response that carries a description but no protocol is an Ergani business error.
            var businessError = result.IsBusinessError || response is { IsBusinessError: true };
            var success       = result.Success && !businessError
                                && !string.IsNullOrWhiteSpace(response?.Protocol);
            var error         = success
                ? null
                : (result.ErrorMessage ?? response?.Description ?? ErganiManager.Data.UiText.T("Ergani schedule submission failed."));

            // Audit one branch-level request. EmployeeId remains null for a multi-employee batch.
            var distinctEmployees = allDays.Select(d => d.EmployeeId).Distinct().ToList();
            db.ApiSubmissionLogs.Add(new ApiSubmissionLog
            {
                CompanyId          = companyId,
                EmployeeId         = distinctEmployees.Count == 1 ? distinctEmployees[0] : null,
                EmployeeIdsJson    = System.Text.Json.JsonSerializer.Serialize(distinctEmployees),
                SubmissionType     = "DailySchedule",
                ScheduleDate       = allDays.Min(d => d.ScheduleDate),
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
                foreach (var day in allDays)
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

            foreach (var day in allDays)
            {
                var dayResult = AddDayResult(outcome, day, success, response?.Protocol, error);

                // Potential extra hours are reviewed explicitly; schedule submission never classifies them as overtime.
            }

            if (!success)
            {
                _logger.LogWarning("Schedule batch submit failed for branch {B}: {E}", branch.Id, error);
                if (result.IsServiceUnavailable) stopped = true;
            }
        }

        // Overtime is left pending and submitted explicitly from the Overtime page.
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
        AppDbContext db, int companyId, ScheduleDayDto day,
        (TimeOnly From, TimeOnly To) range, ScheduleSubmitDayResult dayResult, ScheduleSubmitResult outcome,
        string overtimeJustification)
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

            var justification = Enum.TryParse<OvertimeJustification>(overtimeJustification, out var selectedJustification)
                ? selectedJustification
                : OvertimeJustification.ExceptionalWorkload;

            if (existing != null)
            {
                // Preserve a reason selected in the schedule day popup.
                var generatedBySchedule = existing.RequestPayloadJson?.Contains("\"source\":\"schedule\"") == true;
                if (!generatedBySchedule) existing.Justification = justification;
                await db.SaveChangesAsync();
            }
            else
            {
                var employee = await db.Employees.FindAsync(day.EmployeeId);
                if (employee == null)
                {
                    dayResult.OvertimeError = ErganiManager.Data.UiText.T("Employee not found.");
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
                    Justification        = justification,
                    WeeklyWorkdaysNumber = employee.WeeklyWorkdays,
                    RequestPayloadJson   = System.Text.Json.JsonSerializer.Serialize(new { source = "schedule", scheduleId = day.Id }),
                    CreatedAt            = DateTime.UtcNow
                };
                db.Overtimes.Add(existing);
                await db.SaveChangesAsync();
            }

            var marker = "\"overtimeId\":" + existing.Id;
            var pendingExists = await db.ApiSubmissionLogs.AnyAsync(l =>
                l.CompanyId == companyId && l.SubmissionType == "Overtime" &&
                l.ErrorMessage == "PENDING_OVERTIME" && l.RequestPayloadJson != null &&
                l.RequestPayloadJson.Contains(marker));
            if (!pendingExists)
            {
                db.ApiSubmissionLogs.Add(new ApiSubmissionLog
                {
                    CompanyId = companyId, EmployeeId = day.EmployeeId,
                    EmployeeIdsJson = System.Text.Json.JsonSerializer.Serialize(new[] { day.EmployeeId }),
                    SubmissionType = "Overtime", ScheduleDate = day.ScheduleDate,
                    RequestPayloadJson = System.Text.Json.JsonSerializer.Serialize(new { overtimeId = existing.Id, source = "schedule" }),
                    SubmissionDate = DateTime.UtcNow, Success = false, ErrorMessage = "PENDING_OVERTIME"
                });
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
                        : (ot.ErrorMessage ?? ErganiManager.Data.UiText.T("Overtime submission failed."));
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
        int companyId, IReadOnlyList<ScheduleDayDto> days, string overtimeJustification = "ExceptionalWorkload")
    {
        var outcome = new ScheduleSubmitResult();
        if (days.Count == 0) return outcome;

        await using var db = OpenDb();
        var queue = new List<(int Id, ScheduleSubmitDayResult Day)>();

        foreach (var day in days.Where(d => d.BranchId > 0).OrderBy(d => d.ScheduleDate))
        {
            var scheduledPeriods = day.WorkingPeriods?.OrderBy(p => p.PeriodNumber)
                .Select(p => (p.StartTime, p.EndTime)).ToList() ?? new List<(TimeOnly StartTime, TimeOnly EndTime)>();
            if (scheduledPeriods.Count == 0 && day.StartTime is TimeOnly legacyStart && day.EndTime is TimeOnly legacyEnd)
                scheduledPeriods.Add((legacyStart, legacyEnd));
            var scheduled = day.WorkType is AppWorkType.Office or AppWorkType.Home
                ? ScheduleHours.GetOvertimeRange(scheduledPeriods.Select(p => (p.StartTime, p.EndTime)))
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

            if (await PrepareOvertimeAsync(db, companyId, day, r, dayResult, outcome, overtimeJustification) is int otId)
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
        if (company == null) return (0, ErganiManager.Data.UiText.T("Company not found."));

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
        var periods = day.WorkingPeriods?.OrderBy(p => p.PeriodNumber)
            .Select(p => (Start: (TimeOnly?)p.StartTime, End: (TimeOnly?)p.EndTime)).ToList()
            ?? new List<(TimeOnly? Start, TimeOnly? End)>();

        // Compatibility for callers and older records that still supply only the original pair.
        if (periods.Count == 0 && day.StartTime is TimeOnly start && day.EndTime is TimeOnly end)
            periods.Add((start, end));

        var workDayType = day.WorkType switch
        {
            AppWorkType.Home => "ΤΗΛ",
            AppWorkType.Office => "ΕΡΓ",
            AppWorkType.Rest => "ΑΝ",
            AppWorkType.Absent => "ΜΕ",
            _ => "ΕΡΓ"
        };

        return periods
            .Where(p => p.Start.HasValue && p.End.HasValue && p.End.Value > p.Start.Value)
            .Select(p => new WorkdayDetails
            {
                WorkDayType = workDayType,
                StartTime = p.Start!.Value,
                EndTime = p.End!.Value
            })
            .ToList();
    }
}
