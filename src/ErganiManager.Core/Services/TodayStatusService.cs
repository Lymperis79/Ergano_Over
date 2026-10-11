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

        var issues = new List<(StatusLevel Level, string Text)>();
        void Add(StatusLevel level, string text) => issues.Add((level, text));

        var wholeDayLeave = leaves.FirstOrDefault(l => l.StartTime == null);
        var leaveName = wholeDayLeave == null ? null
            : LeaveTypes.All.FirstOrDefault(t => t.Code == wholeDayLeave.LeaveTypeCode)?.Description ?? wholeDayLeave.LeaveTypeCode;

        var isWorkDay = wholeDayLeave == null && sched != null
            && (sched.WorkType == Entities.WorkType.Office || sched.WorkType == Entities.WorkType.Home)
            && sched.StartTime.HasValue && sched.EndTime.HasValue;
        TimeOnly? plannedStart = sched?.StartTime;
        TimeOnly? plannedEnd   = sched?.EndTime;

        // ── plan ─────────────────────────────────────────────
        if (wholeDayLeave != null) dto.ScheduleText = $"🏖 {leaveName}";
        else if (sched == null) dto.ScheduleText = "No schedule";
        else if (isWorkDay)
            dto.ScheduleText = $"{plannedStart:HH:mm}–{plannedEnd:HH:mm} {(sched.WorkType == Entities.WorkType.Home ? "🏠" : "🏢")}";
        else dto.ScheduleText = sched.WorkType == Entities.WorkType.Absent ? "🚫 Absent" : "💤 Rest day";
        dto.IsExpectedToday = isWorkDay;

        // ── clockings ────────────────────────────────────────
        var arrivals   = cards.Where(c => c.MovementType == "Arrival").ToList();
        var departures = cards.Where(c => c.MovementType == "Departure").ToList();

        (string Text, StatusLevel Level) CardStatus(WorkCardHistoryDto c)
        {
            var level = c.IsLocalFailed ? StatusLevel.Error
                      : c.IsLocalPending ? StatusLevel.Warning
                      : c.SubmittedToErgani ? StatusLevel.Ok
                      : StatusLevel.Error;
            return (c.StatusText, level);
        }

        string Side(List<WorkCardHistoryDto> list, bool first, out string errorStatus)
        {
            errorStatus = string.Empty;
            if (list.Count == 0) return "—";
            // The status shown is the worst one of that side, so one failed card is never hidden.
            var worst = list.Select(c => (Card: c, St: CardStatus(c)))
                            .OrderByDescending(x => (int)x.St.Level).ThenBy(x => x.Card.MovementDateTime).First();
            errorStatus = worst.St.Text;
            var shown = first ? list.First() : list.Last();
            return $"{shown.MovementDateTime:HH:mm}" + (list.Count > 1 ? $" (+{list.Count - 1})" : string.Empty);
        }

        dto.ArrivalText   = Side(arrivals, first: true,  out var arrivalStatus);
        dto.DepartureText = Side(departures, first: false, out var departureStatus);
        dto.ArrivalErgani   = arrivalStatus;
        dto.DepartureErgani = departureStatus;

        // ── Ergani status problems ───────────────────────────
        foreach (var c in cards)
        {
            var (text, level) = CardStatus(c);
            if (level == StatusLevel.Ok) continue;
            var kind = c.MovementType == "Arrival" ? "Clock-in" : "Clock-out";
            var detail = string.IsNullOrWhiteSpace(c.RetryError) ? string.Empty : $" ({c.RetryError})";
            Add(level, $"{kind} {c.MovementDateTime:HH:mm}: {text.TrimStart('❌', '⏳', ' ')}{detail}");
        }

        // ── plan vs. reality ─────────────────────────────────
        var firstArrival = arrivals.FirstOrDefault();
        var last = cards.LastOrDefault();

        if (firstArrival != null && isWorkDay && plannedStart.HasValue)
        {
            var late = TimeOnly.FromDateTime(firstArrival.MovementDateTime) - plannedStart.Value;
            if (late > grace)
                Add(StatusLevel.Warning, $"Late clock-in: {firstArrival.MovementDateTime:HH:mm} (planned {plannedStart:HH:mm}, +{(int)late.TotalMinutes} min)");
        }

        if (arrivals.Count == 0 && isWorkDay && plannedStart.HasValue && After(plannedStart.Value) && departures.Count == 0)
            Add(StatusLevel.Error, $"No clock-in (planned {plannedStart:HH:mm})");

        if (arrivals.Count == 0 && departures.Count > 0)
            Add(StatusLevel.Error, "Clock-out without a clock-in");

        if (last is { MovementType: "Arrival" })
        {
            if (isWorkDay && plannedEnd.HasValue && plannedEnd > plannedStart && After(plannedEnd.Value))
                Add(isPastDay ? StatusLevel.Error : StatusLevel.Warning, $"No clock-out (planned {plannedEnd:HH:mm})");
            else if (!isWorkDay && isPastDay)
                Add(StatusLevel.Error, "No clock-out");
        }

        foreach (var c in departures.Where(d => d.WasEarlyDeparture))
            Add(StatusLevel.Warning, $"Early clock-out {c.MovementDateTime:HH:mm}" +
                (c.EarlyDepartureMinutes.HasValue ? $" ({c.EarlyDepartureMinutes} min early)" : string.Empty));

        for (var i = 1; i < cards.Count; i++)
        {
            if (cards[i].MovementType == cards[i - 1].MovementType)
                Add(StatusLevel.Warning,
                    $"Two {(cards[i].MovementType == "Arrival" ? "clock-ins" : "clock-outs")} in a row " +
                    $"({cards[i - 1].MovementDateTime:HH:mm}, {cards[i].MovementDateTime:HH:mm})");
        }

        if (cards.Count > 0 && !isWorkDay)
        {
            Add(StatusLevel.Warning,
                wholeDayLeave != null ? "Clocked in/out while on leave"
                : sched == null       ? "Clocked in/out but there is no schedule for this day"
                                      : "Clocked in/out on a rest/absent day");
        }

        dto.Issues = issues.Select(i => i.Text).Distinct().ToList();
        dto.Level  = issues.Count == 0 ? StatusLevel.Ok : (StatusLevel)issues.Max(i => (int)i.Level);

        // ── presence ─────────────────────────────────────────
        if (last is { MovementType: "Arrival" })
        {
            dto.IsWorking = true;
            dto.PresenceText = $"🟢 Working since {firstArrival!.MovementDateTime:HH:mm}";
        }
        else if (last != null)
        {
            dto.HasLeft = true;
            dto.PresenceText = $"🔴 Left at {last.MovementDateTime:HH:mm}";
        }
        else if (wholeDayLeave != null) dto.PresenceText = "🏖 On leave";
        else if (isWorkDay && plannedStart.HasValue)
        {
            if (After(plannedStart.Value)) { dto.IsNotArrived = true; dto.PresenceText = "⚪ Not arrived"; }
            else dto.PresenceText = $"⏳ Expected {plannedStart:HH:mm}";
        }
        else dto.PresenceText = sched == null ? "—" : "💤 Not working";

        // ── details ──────────────────────────────────────────
        var lines = cards.Select(c => $"{c.MovementDateTime:HH:mm}  {(c.MovementType == "Arrival" ? "Clock-in " : "Clock-out")}  {c.StatusText}").ToList();
        if (lines.Count == 0) lines.Add("No clock-in/out recorded.");
        if (dto.Issues.Count > 0) { lines.Add(string.Empty); lines.Add("Problems:"); lines.AddRange(dto.Issues.Select(i => "• " + i)); }
        dto.DetailsText = string.Join("\n", lines);

        return dto;
    }
}
