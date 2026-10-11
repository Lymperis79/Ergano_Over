namespace ErganiManager.ErganiApi;

/// <summary>
/// Ergani II REST API endpoints and service codes.
/// </summary>
public static class ErganiEndpoints
{
    // ============================================================
    // BASE URLS
    // ============================================================

    public const string ProductionBaseUrl =
        "https://eservices.yeka.gr/WebServicesApi/api";

    public const string TrialBaseUrl =
        "https://trialv2eservices.yeka.gr/WebServicesApi/api";

    // ============================================================
    // AUTHENTICATION
    // ============================================================

    public const string AuthPath =
        "Authentication";

    public const string AuthRefreshPath =
        "Authentication/Refresh";

    public const string AuthLogoutPath =
        "Authentication/Logout";

    // ============================================================
    // DOCUMENTS
    // ============================================================

    public const string WorkCardSubmitPath =
        "Documents/WRKCardSE";

    public const string DailyScheduleSubmitPath =
        "Documents/WTODaily";

    public const string WeeklyScheduleSubmitPath =
        "Documents/WTOWeek";

    public const string OvertimeSubmitPath =
        "Documents/WTOOv";

    /// <summary>Οργάνωση Χρόνου Εργασίας - Άδειες.</summary>
    public const string LeaveSubmitPath =
        "Documents/WTOLeave";

    // ============================================================
    // LOOKUP
    // ============================================================

    public const string SubmissionsLookupPath =
        "Lookup/Submissions";

    // ============================================================
    // WEB SERVICES
    // ============================================================

    public const string ServicesListPath =
        "WebServices/ServicesList";

    public const string ExecuteServicePath =
        "WebServices/ExecuteService";

    // ============================================================
    // ERGANI DATA SERVICES
    // ============================================================

    /// <summary>
    /// Employer information.
    /// </summary>
    public const string EmployerInfoService =
        "EX_BASE_01";

    /// <summary>
    /// Employer branch information.
    /// </summary>
    public const string BranchInfoService =
        "EX_BASE_02";

    /// <summary>
    /// Parameter lookup.
    /// </summary>
    public const string ParameterLookupService =
        "EX_BASE_03";

    /// <summary>
    /// Monthly employee status.
    /// </summary>
    public const string MonthlyStatusService =
        "EX_BASE_04";

    /// <summary>
    /// Current workforce / employees.
    /// </summary>
    public const string WorkforceStatusService =
        "EX_BASE_05";

    /// <summary>
    /// Acceptance status.
    /// </summary>
    public const string AcceptanceStatusService =
        "EX_BASE_06";

    // ============================================================
    // USERTYPE
    // ============================================================

    /// <summary>
    /// External user.
    /// </summary>
    public const string UsertypeExternal =
        "01";

    /// <summary>
    /// ERGANI credentials.
    /// </summary>
    public const string UsertypeErgani =
        "02";

    /// <summary>
    /// EFKA construction-project credentials.
    /// </summary>
    public const string UsertypeEfka =
        "03";

    // ============================================================
    // HTTP
    // ============================================================

    public static readonly TimeSpan RequestTimeout =
        TimeSpan.FromSeconds(30);
}