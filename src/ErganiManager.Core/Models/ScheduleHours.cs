namespace ErganiManager.Core.Models;

/// <summary>The normal-hours rule shared by the schedule dialog and the Ergani submitter.</summary>
public static class ScheduleHours
{
    /// <summary>Hours of a day that go to Ergani as the normal schedule; the rest is overtime.</summary>
    public const int NormalDailyHours = 8;

    /// <summary>When start→end is longer than the normal day, returns the overtime part
    /// (From = start + 8h, To = end). Otherwise null.</summary>
    public static (TimeOnly From, TimeOnly To)? GetOvertimeRange(TimeOnly? start, TimeOnly? end)
    {
        if (start is not TimeOnly s || end is not TimeOnly e) return null;

        var normalEnd = s.AddHours(NormalDailyHours);
        // AddHours wraps past midnight; a wrapped value means the day is shorter than 8h.
        if (normalEnd <= s || e <= normalEnd) return null;

        return (normalEnd, e);
    }
}
