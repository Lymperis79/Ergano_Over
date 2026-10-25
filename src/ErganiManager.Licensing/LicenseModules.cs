namespace ErganiManager.Licensing;

/// <summary>
/// The parts of the program that a license can switch on. Everything that is not listed here (companies,
/// branches, employees, users, help, database tools) belongs to the base program and is always available.
/// A license holds a list of modules, or "*" for all of them.
/// </summary>
public static class LicenseModules
{
    public const string Schedules = "Schedules";   // schedules, submission of schedules / overtime / holidays
    public const string WorkCards = "WorkCards";   // work card history, manual scan, today board
    public const string Overtime  = "Overtime";    // overtime page
    public const string Reports   = "Reports";     // reports and the Excel export
    public const string Logs      = "Logs";        // API log and email log
    public const string Terminal  = "Terminal";    // the clock-in terminal (scan window)

    public const string All = "*";

    public static readonly IReadOnlyList<string> Names = new[] { Schedules, WorkCards, Overtime, Reports, Logs, Terminal };

    /// <summary>The module a menu (the keys of MenuKeys / the section names) belongs to; null = base program.</summary>
    public static string? ModuleOfMenu(string menuKey) => menuKey switch
    {
        "Schedules"                       => Schedules,
        "WorkCards" or "WorkCardScan" or "Today" => WorkCards,
        "Overtime"                        => Overtime,
        "Reports"                         => Reports,
        "SubmissionLog" or "EmailLog"     => Logs,
        _                                 => null
    };

    /// <summary>Turns "schedules, reports" into the canonical module names; "*" or empty = everything.</summary>
    public static List<string> Parse(string? text)
    {
        var parts = (text ?? string.Empty)
            .Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0 || parts.Contains(All)) return new List<string> { All };

        var result = new List<string>();
        foreach (var part in parts)
        {
            var match = Names.FirstOrDefault(n => string.Equals(n, part, StringComparison.OrdinalIgnoreCase))
                        ?? throw new ArgumentException($"Unknown module '{part}'. Modules: {string.Join(", ", Names)}.");
            if (!result.Contains(match)) result.Add(match);
        }
        return result;
    }
}
