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
    private readonly ILogger<ScheduleSubmitterService> _logger;

    public ScheduleSubmitterService(
        IErganiClient erganiClient,
        IConnectionStateService connectionState,
        ICredentialProtector credentialProtector,
        ILogger<ScheduleSubmitterService> logger)
    {
        _erganiClient        = erganiClient;
        _connectionState     = connectionState;
        _credentialProtector = credentialProtector;
        _logger              = logger;
    }

    private AppDbContext OpenDb() => new AppDbContext(_connectionState.GetDbOptions());

    public async Task<(int Submitted, string? Error)> SubmitScheduleDaysAsync(
        int companyId, IReadOnlyList<ScheduleDayDto> days)
    {
        if (days.Count == 0) return (0, null);

        await using var db = OpenDb();
        var company = await db.Companies.FindAsync(companyId);
        if (company == null) return (0, "Company not found.");

        var credentials = BuildCredentials(company);
        var byBranch    = days.GroupBy(d => d.BranchId);
        int submitted   = 0;

        foreach (var branchGroup in byBranch)
        {
            var branch = await db.Branches.FindAsync(branchGroup.Key);
            if (branch == null) continue;

            var employeeIds = branchGroup.Select(d => d.EmployeeId).Distinct().ToList();
            var employees   = await db.Employees
                .Where(e => employeeIds.Contains(e.Id)).ToListAsync();

            // Group by date — one submission per date
            foreach (var dateGroup in branchGroup.GroupBy(d => d.ScheduleDate))
            {
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

                var submission = new CompanyDailyScheduleSubmission
                {
                    EmployerTaxIdentificationNumber = company.TaxId,
                    BusinessBranchNumber            = branch.BranchNumber,
                    SepeServiceCode                 = branch.SepeServiceCode,
                    BusinessPrimaryActivityCode     = branch.ActivityCode,
                    KallikratisMunicipalCode        = branch.KallikratisMunicipalCode,
                    EmployeeSchedules               = scheduleEntries
                };

                var result = await _erganiClient.SubmitDailyScheduleAsync(
                    credentials, new List<CompanyDailyScheduleSubmission> { submission });

                if (result.Success)
                {
                    var protocol = result.Data?.FirstOrDefault()?.Protocol;
                    foreach (var day in dateGroup)
                    {
                        var entity = await db.Schedules.FindAsync(day.Id);
                        if (entity != null)
                        {
                            entity.SubmittedToErgani = true;
                            entity.Protocol          = protocol;
                        }
                    }
                    await db.SaveChangesAsync();
                    submitted += dateGroup.Count();
                }
                else
                {
                    _logger.LogWarning("Schedule submit failed for branch {B} date {D}: {E}",
                        branch.Id, dateGroup.Key, result.ErrorMessage);
                    return (submitted, result.ErrorMessage);
                }
            }
        }

        return (submitted, null);
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
                EmployerTaxIdentificationNumber = company.TaxId,
                BusinessBranchNumber            = branch.BranchNumber,
                SepeServiceCode                 = branch.SepeServiceCode,
                BusinessPrimaryActivityCode     = branch.ActivityCode,
                KallikratisMunicipalCode        = branch.KallikratisMunicipalCode,
                OvertimeDetails = new List<OvertimeEntry>
                {
                    new()
                    {
                        EmployeeTaxIdentificationNumber = employee.TaxId,
                        EmployeeLastName                = employee.LastName,
                        EmployeeFirstName               = employee.FirstName,
                        OvertimeDate                    = day.ScheduleDate,
                        StartTime                       = TimeOnly.FromDateTime(overtimeStart),
                        EndTime                         = TimeOnly.FromDateTime(overtimeEnd),
                        Justification                   = ApiOvertimeJustification.ExceptionalWorkload,
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
        BaseUrl  = c.ErganiBaseUrl
    };

    private static List<WorkdayDetails> BuildWorkdayDetails(ScheduleDayDto day)
    {
        if (day.StartTime == null || day.EndTime == null) return new();
        return new()
        {
            new()
            {
                WorkDayType = day.WorkType switch
                {
                    AppWorkType.Home   => "WORK_FROM_HOME",
                    AppWorkType.Office => "WORK_FROM_OFFICE",
                    AppWorkType.Rest   => "REST",
                    AppWorkType.Absent => "ABSENT",
                    _                  => "WORK_FROM_OFFICE"
                },
                StartTime = day.StartTime.Value,
                EndTime   = day.EndTime.Value
            }
        };
    }
}
