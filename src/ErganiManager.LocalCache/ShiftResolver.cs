using ErganiManager.LocalCache.Entities;
using Microsoft.EntityFrameworkCore;

namespace ErganiManager.LocalCache;

/// <summary>
/// What a scan means for one employee at one moment: arrival or departure, and which schedule governs it.
/// Calendar dates are NOT enough for this: an overnight shift (22:00 → 06:00) starts on one day and ends
/// on the next, and a shift starting just after midnight (00:10) opens its early-clock-in window on the
/// previous evening.
/// </summary>
public sealed class ShiftContext
{
    /// <summary>"Arrival" or "Departure".</summary>
    public string Movement { get; init; } = "Arrival";

    /// <summary>Time of the arrival this scan closes (only for a Departure that has an open arrival).</summary>
    public DateTime? OpenArrival { get; init; }

    /// <summary>The schedule that governs this scan (null when none applies).</summary>
    public CachedSchedule? Schedule { get; init; }

    /// <summary>Full start date-time of the governing shift.</summary>
    public DateTime? ShiftStart => ShiftResolver.StartOf(Schedule);

    /// <summary>Full end date-time of the governing shift (the next day for an overnight shift).</summary>
    public DateTime? ShiftEnd => ShiftResolver.EndOf(Schedule);
}

public static class ShiftResolver
{
    // WorkType values mirror ErganiManager.Data.Entities.WorkType: 2 = Rest, 3 = Absent.
    private static bool IsWork(CachedSchedule s) => s.WorkType is not (2 or 3);

    /// <summary>Hours after the scheduled end during which an overnight clock-out is still accepted.</summary>
    private const double CloseGraceHours = 6;

    /// <summary>Without any schedule: how long an arrival from a previous day can stay open.</summary>
    private const double NoScheduleMaxOpenHours = 12;

    public static DateTime? StartOf(CachedSchedule? s) =>
        s is { StartTime: { } st } && IsWork(s) ? s.ScheduleDate.ToDateTime(st) : null;

    public static DateTime? EndOf(CachedSchedule? s)
    {
        if (s is not { EndTime: { } et } || !IsWork(s)) return null;
        var end = s.ScheduleDate.ToDateTime(et);
        if (s.StartTime is { } st && et <= st) end = end.AddDays(1); // overnight (also 16:00 → 00:00)
        return end;
    }

    /// <summary>
    /// Works out the movement and the governing schedule for a scan at <paramref name="when"/>.
    /// <paramref name="forcedMovement"/> is set when the operator chose the movement by hand.
    /// </summary>
    public static async Task<ShiftContext> ResolveAsync(
        LocalCacheDbContext cache, int employeeId, DateTime when,
        int earlyClockInBlockMinutes, string? forcedMovement = null)
    {
        var last = await cache.PendingSubmissions
            .Where(p => p.EmployeeId == employeeId && p.ScannedAt <= when)
            .OrderByDescending(p => p.ScannedAt)
            .FirstOrDefaultAsync();

        DateTime? open = null;
        if (last != null && last.MovementType == "Arrival"
            && await IsStillOpenAsync(cache, employeeId, last.ScannedAt, when, earlyClockInBlockMinutes))
            open = last.ScannedAt;

        var movement = forcedMovement ?? (open != null ? "Departure" : "Arrival");

        // A departure is governed by the schedule its arrival belonged to (possibly yesterday's);
        // anything else by the schedule that applies at the scan time.
        var basis = movement == "Departure" && open != null ? open.Value : when;
        var schedule = await ScheduleAtAsync(cache, employeeId, basis, earlyClockInBlockMinutes);

        return new ShiftContext
        {
            Movement = movement,
            OpenArrival = movement == "Departure" ? open : null,
            Schedule = schedule
        };
    }

    /// <summary>
    /// The schedule that applies to someone arriving at <paramref name="at"/>:
    ///  1. an overnight shift that began yesterday and is still running,
    ///  2. otherwise tomorrow's shift, when its early-clock-in window already opened (start just after midnight)
    ///     and today has no shift left,
    ///  3. otherwise today's schedule.
    /// </summary>
    private static async Task<CachedSchedule?> ScheduleAtAsync(
        LocalCacheDbContext cache, int employeeId, DateTime at, int earlyMinutes)
    {
        var day = DateOnly.FromDateTime(at);
        var yesterday = await Find(cache, employeeId, day.AddDays(-1));
        var today = await Find(cache, employeeId, day);
        var tomorrow = await Find(cache, employeeId, day.AddDays(1));

        if (yesterday != null && EndOf(yesterday) is { } yEnd && yEnd > at && yEnd.Date > yesterday.ScheduleDate.ToDateTime(TimeOnly.MinValue))
            return yesterday;

        if (earlyMinutes > 0 && tomorrow != null && StartOf(tomorrow) is { } nextStart
            && at >= nextStart.AddMinutes(-earlyMinutes))
        {
            var todayShiftOver = today == null || StartOf(today) == null
                                 || (EndOf(today) is { } tEnd && at >= tEnd);
            if (todayShiftOver) return tomorrow;
        }

        return today;
    }

    /// <summary>
    /// Whether an arrival is still waiting for its departure. Same calendar day: always. A later day: only when
    /// its shift runs past midnight and has not ended (plus a grace period); without a schedule, within 12 hours.
    /// A forgotten clock-out therefore never turns the next shift's clock-in into a clock-out.
    /// </summary>
    private static async Task<bool> IsStillOpenAsync(
        LocalCacheDbContext cache, int employeeId, DateTime arrival, DateTime when, int earlyMinutes)
    {
        if (arrival.Date == when.Date) return true;

        var sched = await ScheduleAtAsync(cache, employeeId, arrival, earlyMinutes);
        if (EndOf(sched) is { } end) return when <= end.AddHours(CloseGraceHours);

        return (when - arrival) <= TimeSpan.FromHours(NoScheduleMaxOpenHours);
    }

    private static Task<CachedSchedule?> Find(LocalCacheDbContext cache, int employeeId, DateOnly date) =>
        cache.CachedSchedules.FirstOrDefaultAsync(s => s.EmployeeId == employeeId && s.ScheduleDate == date);
}
