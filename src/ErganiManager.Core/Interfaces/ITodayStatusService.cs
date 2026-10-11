namespace ErganiManager.Core.Interfaces;

public enum StatusLevel
{
    Ok = 0,
    Warning = 1,
    Error = 2
}

/// <summary>One employee on the daily status board: plan, clock-in/out, Ergani status and problems.</summary>
public class TodayEmployeeStatusDto
{
    public int EmployeeId { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string BranchName { get; set; } = string.Empty;

    /// <summary>What was planned for the day ("09:00–17:00 🏢", "Rest day", "🏖 Κανονική άδεια", "No schedule").</summary>
    public string ScheduleText { get; set; } = string.Empty;

    public string ArrivalText { get; set; } = "—";
    public string ArrivalErgani { get; set; } = string.Empty;
    public string DepartureText { get; set; } = "—";
    public string DepartureErgani { get; set; } = string.Empty;

    /// <summary>Where the employee is now ("🟢 Working since 09:02", "⚪ Not arrived"...).</summary>
    public string PresenceText { get; set; } = string.Empty;

    public StatusLevel Level { get; set; }
    public List<string> Issues { get; set; } = new();

    /// <summary>Every movement of the day with its Ergani status (tooltip / details panel).</summary>
    public string DetailsText { get; set; } = string.Empty;

    // Sorting / summary helpers
    public bool IsWorking { get; set; }
    public bool HasLeft { get; set; }
    public bool IsNotArrived { get; set; }
    public bool IsExpectedToday { get; set; }

    public bool IsError => Level == StatusLevel.Error;
    public bool IsWarning => Level == StatusLevel.Warning;
    public bool HasIssues => Issues.Count > 0;
    public string IssuesText => Issues.Count == 0 ? "✔ OK" : string.Join("\n", Issues);
}

/// <summary>Builds the daily status board: clock-in/out plus Ergani status of every active employee.</summary>
public interface ITodayStatusService
{
    /// <param name="graceMinutes">Minutes after the planned start/end before a missing or late clocking is reported.</param>
    Task<List<TodayEmployeeStatusDto>> GetAsync(int companyId, DateOnly date, int graceMinutes);
}
