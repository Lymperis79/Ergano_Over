using ErganiManager.Core.Interfaces;
using ErganiManager.Core.Models;
using ErganiManager.Data;
using Microsoft.EntityFrameworkCore;
using Entities = ErganiManager.Data.Entities;

namespace ErganiManager.Core.Services;

public class TodayStatusService : ITodayStatusService
{
    private readonly IConnectionStateService _connectionState;
    private readonly IWorkCardHistoryService _history;

    public TodayStatusService(IConnectionStateService connectionState, IWorkCardHistoryService history)
    {
        _connectionState = connectionState;
        _history = history;
    }

    public async Task<List<TodayEmployeeStatusDto>> GetAsync(int companyId, DateOnly date, int graceMinutes)
    {
        await using var db = new AppDbContext(_connectionState.GetDbOptions());

        var employees = await db.Employees
            .Include(e => e.Branch)
            .Where(e => e.CompanyId == companyId && e.IsActive)
            .OrderBy(e => e.LastName).ThenBy(e => e.FirstName)
            .ToListAsync();
        var ids = employees.Select(e => e.Id).ToList();

        var schedules = (await db.Schedules
                .Where(s => s.ScheduleDate == date && ids.Contains(s.EmployeeId))
                .ToListAsync())
            .GroupBy(s => s.EmployeeId).ToDictionary(g => g.Key, g => g.First());

        var leaves = (await db.Leaves
                .Where(l => l.LeaveDate == date && ids.Contains(l.EmployeeId))
                .ToListAsync())
            .GroupBy(l => l.EmployeeId).ToDictionary(g => g.Key, g => g.ToList());

        // Includes scans that are still waiting in the local queue or failed to be sent.
        var cards = (await _history.GetAsync(companyId, new WorkCardHistoryFilter { FromDate = date, ToDate = date }))
            .GroupBy(c => c.EmployeeId).ToDictionary(g => g.Key, g => g.OrderBy(c => c.MovementDateTime).ToList());

        var now = DateTime.Now;
        var today = DateOnly.FromDateTime(now);
        // Today: the real time. A past day is over; a future day has not started.
        var nowTime = date == today ? TimeOnly.FromDateTime(now)
                    : date < today ? TimeOnly.MaxValue
                    : TimeOnly.MinValue;
        var isPastDay = date < today;
        var grace = TimeSpan.FromMinutes(Math.Max(0, graceMinutes));

        var rows = new List<TodayEmployeeStatusDto>();
        foreach (var emp in employees)
        {
            schedules.TryGetValue(emp.Id, out var sched);
            leaves.TryGetValue(emp.Id, out var empLeaves);
            cards.TryGetValue(emp.Id, out var empCards);
            rows.Add(Build(emp, sched, empLeaves ?? new(), empCards ?? new(), nowTime, isPastDay, grace));
        }

        return rows
            .OrderByDescending(r => (int)r.Level)
            .ThenBy(r => r.FullName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    private static TodayEmployeeStatusDto Build(
        Entities.Employee emp, Entities.EmployeeSchedule? sched, List<Entities.Leave> leaves,
        List<WorkCardHistoryDto> cards, TimeOnly nowTime, bool isPastDay, TimeSpan grace)
    {
        var dto = new TodayEmployeeStatusDto
        {
            EmployeeId = emp.Id,
            FullName   = emp.FullName,
            BranchName = emp.Branch?.Name ?? emp.Branch?.Address ?? string.Empty
        };

        // "Later than <t> + grace" without wrapping past midnight.
        bool After(TimeOnly t) => nowTime.ToTimeSpan() > t.ToTimeSpan() + grace;

        var issues = new List<TodayIssueDto>();
        void Add(TodayIssueDto issue) => issues.Add(issue);

        var wholeDayLeave = leaves.FirstOrDefault(l => l.StartTime == null);
        var leaveType = wholeDayLeave == null ? null : LeaveTypes.Find(wholeDayLeave.LeaveTypeCode);
        var leaveName = wholeDayLeave == null ? null
            : (leaveType?.LocalizedDescription ?? wholeDayLeave.LeaveTypeCode) + (leaveType?.IsPaid == false ? $" ({ErganiManager.Data.UiText.T("Unpaid")})" : string.Empty);

        var isWorkDay = wholeDayLeave == null && sched != null
            && (sched.WorkType == Entities.WorkType.Office || sched.WorkType == Entities.WorkType.Home)
            && sched.StartTime.HasValue && sched.EndTime.HasValue;
        TimeOnly? plannedStart = sched?.StartTime ?? wholeDayLeave?.DeclaredStartTime;
        TimeOnly? plannedEnd   = sched?.EndTime ?? wholeDayLeave?.DeclaredEndTime;

        // ── plan ─────────────────────────────────────────────
        dto.PlannedStart = wholeDayLeave != null ? plannedStart : isWorkDay ? plannedStart : null;
        dto.PlannedEnd   = wholeDayLeave != null ? plannedEnd : isWorkDay ? plannedEnd : null;
        dto.IsHome       = sched?.WorkType == Entities.WorkType.Home;
        dto.LeaveName    = leaveName;
        dto.ScheduleKind = wholeDayLeave != null ? TodayScheduleKind.Leave
                         : sched == null ? TodayScheduleKind.None
                         : isWorkDay ? TodayScheduleKind.Work
                         : sched.WorkType == Entities.WorkType.Absent ? TodayScheduleKind.Absent
                         : TodayScheduleKind.Rest;
        dto.IsExpectedToday = isWorkDay;

        // ── clockings ────────────────────────────────────────
        var arrivals   = cards.Where(c => c.MovementType == "Arrival").ToList();
        var departures = cards.Where(c => c.MovementType == "Departure").ToList();

        TodaySendState SendStateOf(WorkCardHistoryDto c)
            => c.IsLocalFailed ? TodaySendState.Failed
             : c.IsLocalPending ? (c.RetryAttempts > 0 ? TodaySendState.Retrying : TodaySendState.Waiting)
             : c.SubmittedToErgani ? TodaySendState.Sent
             : TodaySendState.NotSubmitted;

        StatusLevel LevelOf(TodaySendState st) => st switch
        {
            TodaySendState.Sent => StatusLevel.Ok,
            TodaySendState.Waiting or TodaySendState.Retrying => StatusLevel.Warning,
            _ => StatusLevel.Error
        };

        dto.Movements = cards.Select(c => new TodayMovementDto
        {
            IsArrival = c.MovementType == "Arrival",
            Time      = TimeOnly.FromDateTime(c.MovementDateTime),
            SendState = SendStateOf(c),
            Protocol  = c.Protocol,
            Attempts  = c.RetryAttempts,
            Detail    = c.RetryError
        }).ToList();

        // The status shown for a side is the worst one of that side, so one failed card is never hidden.
        void Side(List<WorkCardHistoryDto> list, bool first,
            out TimeOnly? time, out int extra, out TodaySendState send, out string? protocol, out int attempts)
        {
            time = null; extra = 0; send = TodaySendState.None; protocol = null; attempts = 0;
            if (list.Count == 0) return;
            var worst = list.Select(c => (Card: c, St: SendStateOf(c)))
                            .OrderByDescending(x => (int)LevelOf(x.St)).ThenBy(x => x.Card.MovementDateTime).First();
            var shown = first ? list.First() : list.Last();
            time = TimeOnly.FromDateTime(shown.MovementDateTime);
            extra = list.Count - 1;
            send = worst.St;
            protocol = worst.Card.Protocol;
            attempts = worst.Card.RetryAttempts;
        }

        Side(arrivals, true, out var aTime, out var aExtra, out var aSend, out var aProto, out var aTry);
        dto.ArrivalTime = aTime; dto.ArrivalExtra = aExtra; dto.ArrivalSend = aSend; dto.ArrivalProtocol = aProto; dto.ArrivalAttempts = aTry;
        Side(departures, false, out var dTime, out var dExtra, out var dSend, out var dProto, out var dTry);
        dto.DepartureTime = dTime; dto.DepartureExtra = dExtra; dto.DepartureSend = dSend; dto.DepartureProtocol = dProto; dto.DepartureAttempts = dTry;

        // ── Ergani status problems ───────────────────────────
        foreach (var c in cards)
        {
            var state = SendStateOf(c);
            if (LevelOf(state) == StatusLevel.Ok) continue;
            Add(new TodayIssueDto
            {
                Code = TodayIssueCode.SendProblem, Level = LevelOf(state),
                IsArrival = c.MovementType == "Arrival",
                Time = TimeOnly.FromDateTime(c.MovementDateTime),
                SendState = state, Protocol = c.Protocol, Attempts = c.RetryAttempts, Detail = c.RetryError
            });
        }

        // ── plan vs. reality ─────────────────────────────────
        var firstArrival = arrivals.FirstOrDefault();
        var last = cards.LastOrDefault();

        if (firstArrival != null && isWorkDay && plannedStart.HasValue)
        {
            var arrivalTime = TimeOnly.FromDateTime(firstArrival.MovementDateTime);
            var late = arrivalTime - plannedStart.Value;
            if (late > grace)
                Add(new TodayIssueDto { Code = TodayIssueCode.LateClockIn, Level = StatusLevel.Warning,
                    Time = arrivalTime, Planned = plannedStart, Minutes = (int)late.TotalMinutes });
        }

        if (arrivals.Count == 0 && isWorkDay && plannedStart.HasValue && After(plannedStart.Value) && departures.Count == 0)
            Add(new TodayIssueDto { Code = TodayIssueCode.NoClockIn, Level = StatusLevel.Error, Planned = plannedStart });

        if (arrivals.Count == 0 && departures.Count > 0)
            Add(new TodayIssueDto { Code = TodayIssueCode.ClockOutWithoutIn, Level = StatusLevel.Error });

        if (last is { MovementType: "Arrival" })
        {
            if (isWorkDay && plannedEnd.HasValue && plannedEnd > plannedStart && After(plannedEnd.Value))
                Add(new TodayIssueDto { Code = TodayIssueCode.NoClockOut,
                    Level = isPastDay ? StatusLevel.Error : StatusLevel.Warning, Planned = plannedEnd });
            else if (!isWorkDay && isPastDay)
                Add(new TodayIssueDto { Code = TodayIssueCode.NoClockOutPastDay, Level = StatusLevel.Error });
        }

        foreach (var c in departures.Where(d => d.WasEarlyDeparture))
            Add(new TodayIssueDto { Code = TodayIssueCode.EarlyClockOut, Level = StatusLevel.Warning,
                Time = TimeOnly.FromDateTime(c.MovementDateTime), Minutes = c.EarlyDepartureMinutes });

        for (var i = 1; i < cards.Count; i++)
        {
            if (cards[i].MovementType != cards[i - 1].MovementType) continue;
            Add(new TodayIssueDto
            {
                Code = cards[i].MovementType == "Arrival" ? TodayIssueCode.DoubleClockIn : TodayIssueCode.DoubleClockOut,
                Level = StatusLevel.Warning,
                Time = TimeOnly.FromDateTime(cards[i - 1].MovementDateTime),
                Time2 = TimeOnly.FromDateTime(cards[i].MovementDateTime)
            });
        }

        if (cards.Count > 0 && !isWorkDay)
        {
            Add(new TodayIssueDto
            {
                Level = StatusLevel.Warning,
                Code = wholeDayLeave != null ? TodayIssueCode.ClockedOnLeave
                     : sched == null ? TodayIssueCode.ClockedNoSchedule
                     : TodayIssueCode.ClockedRestDay
            });
        }

        dto.Issues = issues;
        dto.Level  = issues.Count == 0 ? StatusLevel.Ok : (StatusLevel)issues.Max(i => (int)i.Level);

        // ── presence ─────────────────────────────────────────
        if (last is { MovementType: "Arrival" })
        {
            dto.IsWorking = true;
            dto.Presence = TodayPresenceKind.Working;
            dto.PresenceTime = TimeOnly.FromDateTime(firstArrival!.MovementDateTime);
        }
        else if (last != null)
        {
            dto.HasLeft = true;
            dto.Presence = TodayPresenceKind.Left;
            dto.PresenceTime = TimeOnly.FromDateTime(last.MovementDateTime);
        }
        else if (wholeDayLeave != null) dto.Presence = TodayPresenceKind.OnLeave;
        else if (isWorkDay && plannedStart.HasValue)
        {
            if (After(plannedStart.Value)) { dto.IsNotArrived = true; dto.Presence = TodayPresenceKind.NotArrived; }
            else { dto.Presence = TodayPresenceKind.Expected; dto.PresenceTime = plannedStart; }
        }
        else dto.Presence = sched == null ? TodayPresenceKind.None : TodayPresenceKind.NotWorking;

        return dto;
    }
}
