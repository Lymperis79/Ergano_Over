namespace ErganiManager.ErganiApi;

/// <summary>
/// ── EDIT THIS FILE IF ERGANI CHANGES THEIR API PATHS OR BASE URL ──
///
/// Every Ergani-specific URL/path used by the application lives here, in one
/// place. Nothing else in the codebase should ever hardcode a path string —
/// ErganiClient and everything else reads from this class.
///
/// WHY THIS MATTERS: the exact endpoint paths below are inferred from the
/// field-naming conventions used by community SDKs (Python/Rust) for this
/// API, not copied from an official, verified Ergani API spec. If your
/// Ergani credentials/documentation show different paths, change ONLY the
/// values below — no other file needs to change.
///
/// Default base URLs:
///   Production: https://eservices.yeka.gr/WebServicesAPI/api
///   Trial/Test: https://trialeservices.yeka.gr/WebServicesAPI/api
/// (The Company entity stores its own ErganiBaseUrl per company — these
/// constants are just the fallback defaults offered in the UI.)
/// </summary>
public static class ErganiEndpoints
{
    // ── Base URLs (defaults shown in the Company setup screen) ──
    public const string ProductionBaseUrl = "https://eservices.yeka.gr/WebServicesAPI/api";
    public const string TrialBaseUrl = "https://trialeservices.yeka.gr/WebServicesAPI/api";

    // ── Relative paths (appended to whichever base URL the company uses) ──
    public const string WorkCardSubmitPath = "WorkCard/Save";
    public const string DailyScheduleSubmitPath = "Schedule/SaveDaily";
    public const string WeeklyScheduleSubmitPath = "Schedule/SaveWeekly";
    public const string OvertimeSubmitPath = "Overtime/Save";

    // ── HTTP client tuning ──
    public static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(30);
}
