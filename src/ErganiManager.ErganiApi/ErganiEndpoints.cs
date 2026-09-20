namespace ErganiManager.ErganiApi;

/// <summary>
/// All Ergani API paths per the official YEKA documentation (June 2025).
/// Base URL: https://trialeservices.yeka.gr/WebServicesApi/api/
/// </summary>
public static class ErganiEndpoints
{
    // ── Base URLs ──────────────────────────────────────────────────────────
    public const string ProductionBaseUrl = "https://eservices.yeka.gr/WebServicesApi/api";
    public const string TrialBaseUrl      = "https://trialeservices.yeka.gr/WebServicesApi/api";

    // ── Authentication ─────────────────────────────────────────────────────
    /// <summary>POST — obtain JWT. Body: { Username, Password, Usertype }</summary>
    public const string AuthPath          = "Authentication";
    /// <summary>POST — refresh JWT. Body: { AccessToken, RefreshToken }</summary>
    public const string AuthRefreshPath   = "Authentication/Refresh";
    /// <summary>POST — logout (invalidate refresh token). Body: "refreshToken"</summary>
    public const string AuthLogoutPath    = "Authentication/Logout";

    // ── Document submissions ───────────────────────────────────────────────
    /// <summary>POST — Work Card (arrival/departure). Code: WRKCardSE</summary>
    public const string WorkCardSubmitPath        = "Documents/WRKCardSE";
    /// <summary>POST — Daily schedule. Code: WTODaily</summary>
    public const string DailyScheduleSubmitPath   = "Documents/WTODaily";
    /// <summary>POST — Weekly schedule. Code: WTOWeek</summary>
    public const string WeeklyScheduleSubmitPath  = "Documents/WTOWeek";
    /// <summary>POST — Overtime. Code: (TBD per lookup)</summary>
    public const string OvertimeSubmitPath        = "Documents/WRKOvertime";

    // ── Lookup ─────────────────────────────────────────────────────────────
    /// <summary>GET — list all active submission types</summary>
    public const string SubmissionsLookupPath = "Lookup/Submissions";

    // ── Web Services (data retrieval) ──────────────────────────────────────
    /// <summary>GET — list all available services</summary>
    public const string ServicesListPath    = "WebServices/ServicesList";
    /// <summary>POST — execute a named service</summary>
    public const string ExecuteServicePath  = "WebServices/ExecuteService";

    // ── Usertype codes ─────────────────────────────────────────────────────
    /// <summary>Standard ΕΡΓΑΝΗ credentials</summary>
    public const string UsertypeErgani     = "02";
    /// <summary>External user</summary>
    public const string UsertypeExternal   = "01";

    // ── HTTP tuning ────────────────────────────────────────────────────────
    public static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(30);
}
