using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ErganiManager.Core.Interfaces;
using ErganiManager.Core.Models;
using ErganiManager.Data;
using ErganiManager.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ErganiManager.UI.Services;

/// <summary>
/// Monitors the active company while the desktop application is running and submits
/// a departure only when a scheduled employee has an arrival but no departure.
/// </summary>
public sealed class AutoClockOutMonitor
{
    private readonly IConnectionStateService _connectionState;
    private readonly ICompanyContext _companyContext;
    private readonly IPortalSettingsService _settings;
    private readonly IWorkCardSubmitter _submitter;
    private readonly ILogger<AutoClockOutMonitor> _logger;
    private CancellationTokenSource? _cts;
    private Task? _loop;

    public AutoClockOutMonitor(
        IConnectionStateService connectionState,
        ICompanyContext companyContext,
        IPortalSettingsService settings,
        IWorkCardSubmitter submitter,
        ILogger<AutoClockOutMonitor> logger)
    {
        _connectionState = connectionState;
        _companyContext = companyContext;
        _settings = settings;
        _submitter = submitter;
        _logger = logger;
    }

    public void Start()
    {
        if (_loop != null) return;
        _cts = new CancellationTokenSource();
        _loop = RunAsync(_cts.Token);
    }

    public async Task StopAsync()
    {
        if (_cts == null) return;
        _cts.Cancel();
        try { if (_loop != null) await _loop.ConfigureAwait(false); }
        catch (OperationCanceledException) { }
        _cts.Dispose();
        _cts = null;
        _loop = null;
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await CheckAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Automatic clock-out monitoring failed; it will try again.");
            }

            await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task CheckAsync(CancellationToken cancellationToken)
    {
        var settings = _settings.Current;
        if (!settings.AutoClockOutEnabled ||
            !_companyContext.IsAuthenticated ||
            !_connectionState.ConfigExists() ||
            _companyContext.ActiveCompanyId is not int companyId)
            return;

        var now = DateTime.Now;
        var today = DateOnly.FromDateTime(now);
        var firstDate = today.AddDays(-1);

        await using var db = new AppDbContext(_connectionState.GetDbOptions());
        var schedules = await db.Schedules
            .Include(s => s.Employee)
            .Where(s => s.ScheduleDate >= firstDate &&
                        s.ScheduleDate <= today &&
                        s.Employee != null &&
                        s.Employee.CompanyId == companyId &&
                        s.Employee.IsActive &&
                        (s.WorkType == WorkType.Office || s.WorkType == WorkType.Home) &&
                        s.StartTime != null && s.EndTime != null)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        foreach (var schedule in schedules)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var employee = schedule.Employee;
            if (employee == null) continue;

            var start = schedule.ScheduleDate.ToDateTime(schedule.StartTime!.Value);
            var end = schedule.ScheduleDate.ToDateTime(schedule.EndTime!.Value);
            if (schedule.EndTime.Value <= schedule.StartTime.Value)
                end = end.AddDays(1);

            var graceMinutes = Math.Clamp(settings.AutoClockOutMinutes, 0, 1440);
            var departureTime = end.AddMinutes(graceMinutes);
            if (now < departureTime) continue;

            // Include early arrivals and an overnight shift's next-day movements.
            var cardFrom = start.AddHours(-6);
            var cardTo = end.AddHours(18);
            var cards = await db.WorkCards
                .Where(w => w.EmployeeId == employee.Id &&
                            w.MovementDateTime >= cardFrom &&
                            w.MovementDateTime <= cardTo)
                .OrderBy(w => w.MovementDateTime)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            var arrival = cards
                .Where(w => w.MovementType == MovementType.Arrival &&
                            w.MovementDateTime >= start.AddHours(-6) &&
                            w.MovementDateTime <= end)
                .OrderByDescending(w => w.MovementDateTime)
                .FirstOrDefault();
            if (arrival == null || departureTime <= arrival.MovementDateTime)
                continue;

            // Any departure after the clock-in means the shift already has a clock-out.
            // This also prevents duplicate submissions after a failed automatic submission:
            // the standard submitter persists a WorkCard placeholder and the retry service owns retries.
            var hasDeparture = cards.Any(w =>
                w.MovementType == MovementType.Departure &&
                w.MovementDateTime > arrival.MovementDateTime);
            if (hasDeparture) continue;

            _logger.LogInformation(
                "Automatic clock-out: submitting departure for employee {EmployeeId}, scheduled end {ScheduledEnd}, departure time {DepartureTime}.",
                employee.Id, end, departureTime);

            var result = await _submitter.SubmitAsync(new WorkCardSubmissionRequest
            {
                EmployeeId = employee.Id,
                CompanyId = companyId,
                BranchId = schedule.BranchId,
                MovementType = "Departure",
                MovementDateTime = departureTime
            }).ConfigureAwait(false);

            if (result.Success)
                _logger.LogInformation("Automatic clock-out submitted for employee {EmployeeId}; protocol {Protocol}.",
                    employee.Id, result.Protocol);
            else
                _logger.LogWarning("Automatic clock-out submission failed for employee {EmployeeId}: {Error}",
                    employee.Id, result.ErrorMessage);
        }
    }
}
