namespace ErganiManager.Core.Interfaces;

public enum StatusLevel
{
    Ok = 0,
    Warning = 1,
    Error = 2
}

/// <summary>What was planned for the day.</summary>
public enum TodayScheduleKind { None, Work, Rest, Absent, Leave }

/// <summary>Where the employee is now.</summary>
public enum TodayPresenceKind { None, Working, Left, OnLeave, NotArrived, Expected, NotWorking }

/// <summary>Ergani status of one clock-in/out.</summary>
public enum TodaySendState { None, Sent, Waiting, Retrying, Failed, NotSubmitted }

public enum TodayIssueCode
{
    SendProblem,        // IsArrival, Time, SendState, Detail (+ Protocol/Attempts)
    LateClockIn,        // Time, Planned, Minutes
    NoClockIn,          // Planned
    ClockOutWithoutIn,
    NoClockOut,         // Planned
    NoClockOutPastDay,
    EarlyClockOut,      // Time, Minutes?
    DoubleClockIn,      // Time, Time2
    DoubleClockOut,     // Time, Time2
    ClockedOnLeave,
    ClockedNoSchedule,
    ClockedRestDay
}

/// <summary>A problem found for an employee. Language neutral: the screen turns it into text.</summary>
public class TodayIssueDto
{
    public TodayIssueCode Code { get; set; }
    public StatusLevel Level { get; set; }
    public bool IsArrival { get; set; }
    public TimeOnly? Time { get; set; }
    public TimeOnly? Time2 { get; set; }
    public TimeOnly? Planned { get; set; }
    public int? Minutes { get; set; }
    public TodaySendState SendState { get; set; }
    public string? Protocol { get; set; }
    public int Attempts { get; set; }
    public string? Detail { get; set; }
}

/// <summary>One clock-in or clock-out with its Ergani status.</summary>
public class TodayMovementDto
{
    public bool IsArrival { get; set; }
    public TimeOnly Time { get; set; }
    public TodaySendState SendState { get; set; }
    public string? Protocol { get; set; }
    public int Attempts { get; set; }
    public string? Detail { get; set; }
}

/// <summary>One employee on the daily status board.</summary>
public class TodayEmployeeStatusDto
{
    public int EmployeeId { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string BranchName { get; set; } = string.Empty;

    // Plan
    public TodayScheduleKind ScheduleKind { get; set; }
    public TimeOnly? PlannedStart { get; set; }
    public TimeOnly? PlannedEnd { get; set; }
    public bool IsHome { get; set; }
    public string? LeaveName { get; set; }

    // Clockings
    public List<TodayMovementDto> Movements { get; set; } = new();
    public TimeOnly? ArrivalTime { get; set; }
    public int ArrivalExtra { get; set; }
    public TodaySendState ArrivalSend { get; set; }
    public string? ArrivalProtocol { get; set; }
    public int ArrivalAttempts { get; set; }
    public TimeOnly? DepartureTime { get; set; }
    public int DepartureExtra { get; set; }
    public TodaySendState DepartureSend { get; set; }
    public string? DepartureProtocol { get; set; }
    public int DepartureAttempts { get; set; }

    // Now
    public TodayPresenceKind Presence { get; set; }
    public TimeOnly? PresenceTime { get; set; }

    public StatusLevel Level { get; set; }
    public List<TodayIssueDto> Issues { get; set; } = new();

    // Summary helpers
    public bool IsWorking { get; set; }
    public bool HasLeft { get; set; }
    public bool IsNotArrived { get; set; }
    public bool IsExpectedToday { get; set; }
    public bool IsOfficialHoliday { get; set; }

    public bool IsError => Level == StatusLevel.Error;
    public bool IsWarning => Level == StatusLevel.Warning;
    public bool HasIssues => Issues.Count > 0;
}

/// <summary>Builds the daily status board: clock-in/out plus Ergani status of every active employee.</summary>
public interface ITodayStatusService
{
    /// <param name="graceMinutes">Minutes after the planned start/end before a missing or late clocking is reported.</param>
    Task<List<TodayEmployeeStatusDto>> GetAsync(int companyId, DateOnly date, int graceMinutes);
}
