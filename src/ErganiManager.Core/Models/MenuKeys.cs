namespace ErganiManager.Core.Models;

/// <summary>
/// The menus of the main window that can be shown or hidden per user. The keys are the names of the
/// sections (AdminSection) so they can be used for navigation as well.
/// A user with no restriction (<c>AllowedMenus == null</c>) sees every menu.
/// </summary>
public static class MenuKeys
{
    public const string Companies      = "Companies";
    public const string Branches       = "Branches";
    public const string Employees      = "Employees";
    public const string Users          = "Users";
    public const string Schedules      = "Schedules";
    public const string WorkCards      = "WorkCards";
    public const string Scan           = "WorkCardScan";
    public const string Today          = "Today";
    public const string Overtime       = "Overtime";
    public const string Reports        = "Reports";
    public const string ApiLog         = "SubmissionLog";
    public const string Help           = "Help";

    /// <summary>Database tools (backup / delete database). Super administrator only — never configurable.</summary>
    public const string Administration = "Administration";

    /// <summary>The menus an administrator can allow or hide for a user, in display order.</summary>
    public static readonly IReadOnlyList<string> Configurable = new[]
    {
        Companies, Branches, Employees,
        Schedules, WorkCards, Scan, Today, Overtime, Reports, ApiLog,
        Users, Help
    };

    /// <summary>Null/empty text = no restriction (all menus).</summary>
    public static HashSet<string>? Parse(string? stored)
    {
        if (string.IsNullOrWhiteSpace(stored)) return null;
        return stored.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(k => Configurable.Contains(k))
            .ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>Null when every menu is allowed (no restriction), otherwise the allowed keys.</summary>
    public static string? Serialize(IEnumerable<string> allowed)
    {
        var set = allowed.Where(k => Configurable.Contains(k)).ToHashSet(StringComparer.Ordinal);
        if (Configurable.All(set.Contains)) return null;
        return string.Join(",", Configurable.Where(set.Contains));
    }
}
