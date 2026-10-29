namespace ErganiManager.UI.ViewModels;

/// <summary>One line of the "last scans" list on the terminal.</summary>
public sealed class RecentScanItem
{
    public string TimeText { get; init; } = string.Empty;
    public string EmployeeName { get; init; } = "—";
    public string MovementText { get; init; } = string.Empty;

    /// <summary>Arrival/departure direction styling for the terminal list.</summary>
    public bool IsArrival { get; init; }
    public bool IsDeparture { get; init; }
    public string StatusText { get; init; } = string.Empty;

    /// <summary>Accepted scan (green).</summary>
    public bool IsOk { get; init; }

    /// <summary>Accepted with a warning, e.g. early departure, or still waiting to be sent (amber).</summary>
    public bool IsWarn { get; init; }

    /// <summary>Refused: blocked, unknown badge or error (red).</summary>
    public bool IsBad { get; init; }
}
