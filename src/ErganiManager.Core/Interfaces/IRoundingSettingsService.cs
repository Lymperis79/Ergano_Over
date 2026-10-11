namespace ErganiManager.Core.Interfaces;

/// <summary>
/// One rounding band: when the minutes of a worked duration are between
/// <see cref="FromMinute"/> and <see cref="ToMinute"/> (inclusive) the time is rounded to
/// the whole hour plus <see cref="RoundToMinute"/> (0, 30, or 60 = the next hour).
/// </summary>
public sealed class RoundingBand
{
    public int FromMinute { get; set; }
    public int ToMinute { get; set; }
    public int RoundToMinute { get; set; }
}

public sealed class WorkShiftDefinition
{
    public string Name { get; set; } = string.Empty;
    public TimeSpan StartTime { get; set; } = new(9, 0, 0);
    public TimeSpan EndTime { get; set; } = new(17, 0, 0);

    public WorkShiftDefinition Clone() => new()
    {
        Name = Name,
        StartTime = StartTime,
        EndTime = EndTime
    };

    public override string ToString() => Name;
}

public sealed class RoundingRules
{
    public List<RoundingBand> Bands { get; set; } = Defaults();

    /// <summary>Three reusable working-time shifts shown when creating/editing a schedule.</summary>
    public List<WorkShiftDefinition> Shifts { get; set; } = DefaultShifts();

    /// <summary>Start of the configurable night-work window, as minutes after midnight.</summary>
    public int NightStartMinuteOfDay { get; set; } = 22 * 60;

    /// <summary>End of the configurable night-work window, as minutes after midnight on the following day.</summary>
    public int NightEndMinuteOfDay { get; set; } = 6 * 60;

    public TimeOnly NightStart => TimeOnly.FromTimeSpan(TimeSpan.FromMinutes(Math.Clamp(NightStartMinuteOfDay, 0, 1439)));
    public TimeOnly NightEnd => TimeOnly.FromTimeSpan(TimeSpan.FromMinutes(Math.Clamp(NightEndMinuteOfDay, 0, 1439)));

    public static List<WorkShiftDefinition> DefaultShifts() => new()
    {
        new WorkShiftDefinition { Name = "Shift 1", StartTime = new TimeSpan(8, 0, 0), EndTime = new TimeSpan(16, 0, 0) },
        new WorkShiftDefinition { Name = "Shift 2", StartTime = new TimeSpan(9, 0, 0), EndTime = new TimeSpan(17, 0, 0) },
        new WorkShiftDefinition { Name = "Shift 3", StartTime = new TimeSpan(14, 0, 0), EndTime = new TimeSpan(22, 0, 0) }
    };

    public static List<RoundingBand> Defaults() => new()
    {
        new RoundingBand { FromMinute = 1,  ToMinute = 16, RoundToMinute = 0  },
        new RoundingBand { FromMinute = 17, ToMinute = 30, RoundToMinute = 30 },
        new RoundingBand { FromMinute = 31, ToMinute = 46, RoundToMinute = 30 },
        new RoundingBand { FromMinute = 47, ToMinute = 59, RoundToMinute = 60 }
    };

    /// <summary>
    /// Rounds a duration in hours. Whole minutes are used (seconds are ignored); an exact hour stays as is,
    /// and minutes not covered by any band are left unrounded.
    /// e.g. 8:09 → 8:00, 8:20 → 8:30, 8:50 → 9:00 with the default bands.
    /// </summary>
    public double Round(double hours)
    {
        var totalMinutes = (int)Math.Floor(hours * 60 + 1e-6);
        var whole = totalMinutes / 60;
        var minutes = totalMinutes % 60;
        if (minutes == 0) return whole;

        var band = Bands.FirstOrDefault(b => minutes >= b.FromMinute && minutes <= b.ToMinute);
        return band == null ? totalMinutes / 60.0 : whole + band.RoundToMinute / 60.0;
    }
}

/// <summary>Stores the editable rounding rules (a small file in the application data folder).</summary>
public interface IRoundingSettingsService
{
    RoundingRules Current { get; }
    void Save(RoundingRules rules);
}
