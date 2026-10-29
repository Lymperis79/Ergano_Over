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

    /// <summary>Legacy compatibility helper. New split-shift code should use GetPotentialExtraHours.</summary>
    public static (TimeOnly From, TimeOnly To)? GetOvertimeRange(TimeOnly? start, TimeOnly? end)
    {
        if (start is not TimeOnly s || end is not TimeOnly e) return null;
        var normalEnd = s.AddHours(NormalDailyHours);
        if (normalEnd <= s || e <= normalEnd) return null;
        return (normalEnd, e);
    }
}
