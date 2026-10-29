namespace ErganiManager.Core.Models;

/// <summary>Duration calculations for planned working periods. Extra time is a review flag, not a legal classification.</summary>
public static class ScheduleHours
{
    public const int NormalDailyHours = 8;

    public static double GetTotalHours(IEnumerable<(TimeOnly Start, TimeOnly End)> periods)
    {
        ArgumentNullException.ThrowIfNull(periods);
        var total = 0d;
        var ordered = periods.OrderBy(p => p.Start).ToList();
        for (var i = 0; i < ordered.Count; i++)
        {
            var (start, end) = ordered[i];
            if (end <= start)
                throw new ArgumentException("Each working period must end after it starts.", nameof(periods));
            if (i > 0 && start < ordered[i - 1].End)
                throw new ArgumentException("Working periods cannot overlap.", nameof(periods));
            total += (end - start).TotalHours;
        }
        return total;
    }

    /// <summary>Calculates hours beyond the employee's configured normal daily hours; does not classify them as overtime.</summary>
    public static double GetPotentialExtraHours(IEnumerable<(TimeOnly Start, TimeOnly End)> periods, double normalDailyHours)
    {
        if (normalDailyHours < 0 || normalDailyHours > 24)
            throw new ArgumentOutOfRangeException(nameof(normalDailyHours));
        return Math.Max(0, GetTotalHours(periods) - normalDailyHours);
    }

    /// <summary>
    /// Finds the part of a day's working periods that exceeds the normal daily hours.
    /// The normal-hours threshold is cumulative worked time, not elapsed clock time from
    /// the first shift. For example, 08:00-14:00 and 17:00-21:00 produces 19:00-21:00.
    /// </summary>
    public static (TimeOnly From, TimeOnly To)? GetOvertimeRange(
        IEnumerable<(TimeOnly Start, TimeOnly End)> periods,
        double normalDailyHours = NormalDailyHours)
    {
        ArgumentNullException.ThrowIfNull(periods);
        if (normalDailyHours < 0 || normalDailyHours > 24)
            throw new ArgumentOutOfRangeException(nameof(normalDailyHours));

        var ordered = periods.OrderBy(p => p.Start).ToList();
        var workedHours = 0d;
        foreach (var (start, end) in ordered)
        {
            if (end <= start)
                throw new ArgumentException("Each working period must end after it starts.", nameof(periods));

            var duration = (end - start).TotalHours;
            if (workedHours + duration > normalDailyHours)
            {
                var normalHoursRemaining = Math.Max(0, normalDailyHours - workedHours);
                var overtimeStart = start.AddHours(normalHoursRemaining);
                return overtimeStart < end ? (overtimeStart, end) : null;
            }

            workedHours += duration;
        }

        return null;
    }

    /// <summary>Legacy compatibility helper for a single continuous shift.</summary>
    public static (TimeOnly From, TimeOnly To)? GetOvertimeRange(TimeOnly? start, TimeOnly? end)
    {
        if (start is not TimeOnly s || end is not TimeOnly e) return null;
        return GetOvertimeRange(new[] { (s, e) });
    }
}
