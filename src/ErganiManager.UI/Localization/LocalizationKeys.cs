namespace ErganiManager.UI.Localization;

public static class L
{
    // Navigation
    public const string NavCompanies    = nameof(NavCompanies);
    public const string NavBranches     = nameof(NavBranches);
    public const string NavEmployees    = nameof(NavEmployees);
    public const string NavUsers        = nameof(NavUsers);
    public const string NavSchedules    = nameof(NavSchedules);
    public const string NavWorkCards    = nameof(NavWorkCards);
    public const string NavOvertime     = nameof(NavOvertime);
    public const string NavApiLog       = nameof(NavApiLog);

    // Common actions
    public const string Save            = nameof(Save);
    public const string Cancel          = nameof(Cancel);
    public const string Delete          = nameof(Delete);
    public const string Edit            = nameof(Edit);
    public const string Close           = nameof(Close);
    public const string Load            = nameof(Load);
    public const string Export          = nameof(Export);
    public const string Import          = nameof(Import);
    public const string New             = nameof(New);
    public const string Search          = nameof(Search);
    public const string Refresh         = nameof(Refresh);
    public const string RetryNow        = nameof(RetryNow);
    public const string TestConnection  = nameof(TestConnection);

    // Auth / Login
    public const string AppTitle            = nameof(AppTitle);
    public const string SignIn              = nameof(SignIn);
    public const string Username            = nameof(Username);
    public const string Password            = nameof(Password);
    public const string OfflineBanner       = nameof(OfflineBanner);
    public const string InvalidCredentials  = nameof(InvalidCredentials);
    public const string Welcome             = nameof(Welcome);
    public const string Logout              = nameof(Logout);

    // DB Setup
    public const string DbSetupTitle        = nameof(DbSetupTitle);
    public const string DbSetupSubtitle     = nameof(DbSetupSubtitle);
    public const string DbType              = nameof(DbType);
    public const string DbSqliteNote        = nameof(DbSqliteNote);
    public const string DbServer            = nameof(DbServer);
    public const string DbName              = nameof(DbName);
    public const string DbWindowsAuth       = nameof(DbWindowsAuth);
    public const string DbSaveAndContinue   = nameof(DbSaveAndContinue);
    public const string DbSchemaIncomplete  = nameof(DbSchemaIncomplete);

    // First Admin
    public const string CreateAdminTitle    = nameof(CreateAdminTitle);
    public const string CreateAdminSubtitle = nameof(CreateAdminSubtitle);
    public const string ConfirmPassword     = nameof(ConfirmPassword);
    public const string PasswordTooShort    = nameof(PasswordTooShort);
    public const string PasswordMismatch    = nameof(PasswordMismatch);

    // Companies
    public const string Companies           = nameof(Companies);
    public const string CompanyName         = nameof(CompanyName);
    public const string TaxId               = nameof(TaxId);
    public const string ErganiUsername      = nameof(ErganiUsername);
    public const string ErganiPassword      = nameof(ErganiPassword);
    public const string ErganiBaseUrl       = nameof(ErganiBaseUrl);
    public const string Active              = nameof(Active);
    public const string TimeRules           = nameof(TimeRules);
    public const string EarlyClockInBlock   = nameof(EarlyClockInBlock);
    public const string EarlyDepartureAlert = nameof(EarlyDepartureAlert);
    public const string BlockNoSchedule     = nameof(BlockNoSchedule);
    public const string EmailAlerts         = nameof(EmailAlerts);
    public const string AutoRetry           = nameof(AutoRetry);
    public const string AutoRetryDesc       = nameof(AutoRetryDesc);

    // Branches
    public const string Branches            = nameof(Branches);
    public const string BranchNumber        = nameof(BranchNumber);
    public const string Address             = nameof(Address);
    public const string SepeCode            = nameof(SepeCode);
    public const string ActivityCode        = nameof(ActivityCode);
    public const string MunicipalCode       = nameof(MunicipalCode);

    // Employees
    public const string Employees           = nameof(Employees);
    public const string FirstName           = nameof(FirstName);
    public const string LastName            = nameof(LastName);
    public const string Amka                = nameof(Amka);
    public const string BarcodeId           = nameof(BarcodeId);
    public const string ProfessionCode      = nameof(ProfessionCode);
    public const string WeeklyWorkdays      = nameof(WeeklyWorkdays);
    public const string Branch              = nameof(Branch);
    public const string ImportFromExcel     = nameof(ImportFromExcel);

    // Users
    public const string Users               = nameof(Users);
    public const string Role                = nameof(Role);
    public const string Admin               = nameof(Admin);
    public const string Operator            = nameof(Operator);
    public const string ResetPassword       = nameof(ResetPassword);
    public const string NewPassword         = nameof(NewPassword);
    public const string LockToBranch        = nameof(LockToBranch);

    // Terminal
    public const string ScanPrompt          = nameof(ScanPrompt);
    public const string ClockInSuccess      = nameof(ClockInSuccess);
    public const string ClockOutSuccess     = nameof(ClockOutSuccess);
    public const string ClockOutEarly       = nameof(ClockOutEarly);
    public const string TooEarly            = nameof(TooEarly);
    public const string NoSchedule          = nameof(NoSchedule);
    public const string UnknownBadge        = nameof(UnknownBadge);
    public const string OfflineMode         = nameof(OfflineMode);
    public const string Arrival             = nameof(Arrival);
    public const string Departure           = nameof(Departure);
    public const string ShiftLabel          = nameof(ShiftLabel);
    public const string PleaseReturnIn      = nameof(PleaseReturnIn);

    // Schedules
    public const string Schedules           = nameof(Schedules);
    public const string WorkType            = nameof(WorkType);
    public const string StartTime           = nameof(StartTime);
    public const string EndTime             = nameof(EndTime);
    public const string Comments            = nameof(Comments);
    public const string NotSubmitted        = nameof(NotSubmitted);
    public const string SubmittedProtocol   = nameof(SubmittedProtocol);
    public const string ActualClockTimes    = nameof(ActualClockTimes);
    public const string ExportMonth         = nameof(ExportMonth);

    // Work Cards
    public const string WorkCards           = nameof(WorkCards);
    public const string EarlyDeparture      = nameof(EarlyDeparture);
    public const string MinutesEarly        = nameof(MinutesEarly);
    public const string Protocol            = nameof(Protocol);

    // Overtime
    public const string Overtime            = nameof(Overtime);
    public const string OvertimeDate        = nameof(OvertimeDate);
    public const string Justification       = nameof(Justification);
    public const string AseeApproval        = nameof(AseeApproval);
    public const string Cancelled           = nameof(Cancelled);
    public const string CancelOvertime      = nameof(CancelOvertime);

    // Submission Log
    public const string ApiLog              = nameof(ApiLog);
    public const string FailedSubmissions   = nameof(FailedSubmissions);
    public const string PendingRetry        = nameof(PendingRetry);
    public const string NoFailedPending     = nameof(NoFailedPending);
    public const string FailuresOnly        = nameof(FailuresOnly);
    public const string RequestPayload      = nameof(RequestPayload);
    public const string Response            = nameof(Response);

    // Status
    public const string Submitted           = nameof(Submitted);
    public const string Pending             = nameof(Pending);
    public const string ErrorPrefix         = nameof(ErrorPrefix);
    public const string SuccessPrefix       = nameof(SuccessPrefix);

    // Language
    public const string Language            = nameof(Language);
}
