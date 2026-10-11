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
    public const string RetrySelected   = nameof(RetrySelected);
    public const string SelectAllFailed = nameof(SelectAllFailed);
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
    public const string NightWorkingHours   = nameof(NightWorkingHours);
    public const string NightWorkingHoursDescription = nameof(NightWorkingHoursDescription);
    public const string NightStart          = nameof(NightStart);
    public const string NightEnd            = nameof(NightEnd);
    public const string NightSettingsSaved  = nameof(NightSettingsSaved);
    public const string NightSettingsApply  = nameof(NightSettingsApply);
    public const string EarlyClockInBlock   = nameof(EarlyClockInBlock);
    public const string EarlyDepartureAlert = nameof(EarlyDepartureAlert);
    public const string BlockNoSchedule     = nameof(BlockNoSchedule);
    public const string BlockRestAbsent     = nameof(BlockRestAbsent);
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

    // Portal automation
    public const string OpenErganiPortal = nameof(OpenErganiPortal);
    public const string OpenWorkCardsPortal = nameof(OpenWorkCardsPortal);
    public const string SchedulePortal = nameof(SchedulePortal);
    public const string SchedulePortalDescription = nameof(SchedulePortalDescription);
    public const string WorkCardsPortal = nameof(WorkCardsPortal);
    public const string WorkCardsPortalDescription = nameof(WorkCardsPortalDescription);
    public const string PortalBaseAddress = nameof(PortalBaseAddress);
    public const string PortalTargetPage = nameof(PortalTargetPage);
    public const string PortalFromDateFieldId = nameof(PortalFromDateFieldId);
    public const string PortalToDateFieldId = nameof(PortalToDateFieldId);
    public const string PortalDateFormat = nameof(PortalDateFormat);
    public const string PortalAutoSearch = nameof(PortalAutoSearch);
    public const string PortalAutoDownload = nameof(PortalAutoDownload);
    public const string PortalSearchButtonId = nameof(PortalSearchButtonId);
    public const string PortalDownloadFolder = nameof(PortalDownloadFolder);
    public const string SaveSchedulePortal = nameof(SaveSchedulePortal);
    public const string SaveWorkCardsPortal = nameof(SaveWorkCardsPortal);
    public const string SchedulePortalSaved = nameof(SchedulePortalSaved);
    public const string WorkCardsPortalSaved = nameof(WorkCardsPortalSaved);
    public const string CouldNotSaveSchedulePortal = nameof(CouldNotSaveSchedulePortal);
    public const string CouldNotSaveWorkCardsPortal = nameof(CouldNotSaveWorkCardsPortal);
    public const string PortalInvalidUrl = nameof(PortalInvalidUrl);
    public const string PortalDateFormatPlaceholder = nameof(PortalDateFormatPlaceholder);
    public const string PortalDownloadFolderPlaceholder = nameof(PortalDownloadFolderPlaceholder);

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

    // Ergani user types
    public const string UsertypeExternal    = nameof(UsertypeExternal);
    public const string UsertypeErgani      = nameof(UsertypeErgani);
    public const string UsertypeEfka        = nameof(UsertypeEfka);
    public const string UserType            = nameof(UserType);

    // Late reasons (f_aitiologia)
    public const string AitiologiaPower     = nameof(AitiologiaPower);
    public const string AitiologiaEmployer  = nameof(AitiologiaEmployer);
    public const string AitiologiaErgani    = nameof(AitiologiaErgani);

    // Reports
    public const string NavReports          = nameof(NavReports);

    // Administration / Help
    public const string ReportNightHours   = nameof(ReportNightHours);
    public const string NavHelp             = nameof(NavHelp);
    public const string NavAdmin            = nameof(NavAdmin);
    public const string AdminTitle          = nameof(AdminTitle);
    public const string AdminDatabase       = nameof(AdminDatabase);
    public const string AdminBackup         = nameof(AdminBackup);
    public const string AdminLocalCache     = nameof(AdminLocalCache);
    public const string AdminDeleteCache    = nameof(AdminDeleteCache);
    public const string AdminDangerZone     = nameof(AdminDangerZone);
    public const string AdminDeleteDb       = nameof(AdminDeleteDb);
    public const string AdminConfirm        = nameof(AdminConfirm);
    public const string AdminGeneralSettings = nameof(AdminGeneralSettings);
    public const string AdminHoursSettings   = nameof(AdminHoursSettings);
    public const string AdminPortalSettings  = nameof(AdminPortalSettings);
    public const string AdminDatabaseSettings = nameof(AdminDatabaseSettings);
    public const string AdminShifts          = nameof(AdminShifts);
    public const string AdminShiftsDescription = nameof(AdminShiftsDescription);
    public const string ShiftName            = nameof(ShiftName);
    public const string ShiftStart           = nameof(ShiftStart);
    public const string ShiftEnd             = nameof(ShiftEnd);
    public const string ShiftSelection       = nameof(ShiftSelection);
    public const string ShiftSelectionDescription = nameof(ShiftSelectionDescription);
    public const string ThreeShiftsRequired  = nameof(ThreeShiftsRequired);
    public const string ShiftValuesRequired  = nameof(ShiftValuesRequired);
    public const string ShiftTimesInvalid    = nameof(ShiftTimesInvalid);
    public const string ShiftTimesEqual      = nameof(ShiftTimesEqual);
    public const string HelpTitle           = nameof(HelpTitle);
    public const string HelpEditAction      = nameof(HelpEditAction);
    public const string HelpResetDefault    = nameof(HelpResetDefault);

    // Language
    public const string Language            = nameof(Language);

    public const string NavToday               = nameof(NavToday);
    public const string TodayTitle             = nameof(TodayTitle);
    public const string TodayNoCompany         = nameof(TodayNoCompany);
    public const string TodayGoToday           = nameof(TodayGoToday);
    public const string TodayRefresh           = nameof(TodayRefresh);
    public const string TodayLateAfter         = nameof(TodayLateAfter);
    public const string TodayOnlyProblems      = nameof(TodayOnlyProblems);
    public const string TodayAutoRefresh       = nameof(TodayAutoRefresh);
    public const string TodayUpdated           = nameof(TodayUpdated);
    public const string TodayExpected          = nameof(TodayExpected);
    public const string TodayWorking           = nameof(TodayWorking);
    public const string TodayLeft              = nameof(TodayLeft);
    public const string TodayNotArrived        = nameof(TodayNotArrived);
    public const string TodayErrors            = nameof(TodayErrors);
    public const string TodayWarnings          = nameof(TodayWarnings);
    public const string TodayColEmployee       = nameof(TodayColEmployee);
    public const string TodayColPlanned        = nameof(TodayColPlanned);
    public const string TodayColIn             = nameof(TodayColIn);
    public const string TodayColOut            = nameof(TodayColOut);
    public const string TodayColNow            = nameof(TodayColNow);
    public const string TodayColProblems       = nameof(TodayColProblems);
    public const string TodayNoSchedule        = nameof(TodayNoSchedule);
    public const string TodayRestDay           = nameof(TodayRestDay);
    public const string TodayAbsent            = nameof(TodayAbsent);
    public const string TodayPresWorking       = nameof(TodayPresWorking);
    public const string TodayPresLeft          = nameof(TodayPresLeft);
    public const string TodayPresOnLeave       = nameof(TodayPresOnLeave);
    public const string TodayPresNotArrived    = nameof(TodayPresNotArrived);
    public const string TodayPresExpected      = nameof(TodayPresExpected);
    public const string TodayPresNotWorking    = nameof(TodayPresNotWorking);
    public const string TodaySendSent          = nameof(TodaySendSent);
    public const string TodaySendSentProtocol  = nameof(TodaySendSentProtocol);
    public const string TodaySendWaiting       = nameof(TodaySendWaiting);
    public const string TodaySendRetrying      = nameof(TodaySendRetrying);
    public const string TodaySendFailed        = nameof(TodaySendFailed);
    public const string TodaySendNotSubmitted  = nameof(TodaySendNotSubmitted);
    public const string TodayIssueSendIn       = nameof(TodayIssueSendIn);
    public const string TodayIssueSendOut      = nameof(TodayIssueSendOut);
    public const string TodayIssueLate         = nameof(TodayIssueLate);
    public const string TodayIssueNoIn         = nameof(TodayIssueNoIn);
    public const string TodayIssueOutNoIn      = nameof(TodayIssueOutNoIn);
    public const string TodayIssueNoOut        = nameof(TodayIssueNoOut);
    public const string TodayIssueNoOutPast    = nameof(TodayIssueNoOutPast);
    public const string TodayIssueEarlyOut     = nameof(TodayIssueEarlyOut);
    public const string TodayIssueEarlyOutMin  = nameof(TodayIssueEarlyOutMin);
    public const string TodayIssueDoubleIn     = nameof(TodayIssueDoubleIn);
    public const string TodayIssueDoubleOut    = nameof(TodayIssueDoubleOut);
    public const string TodayIssueOnLeave      = nameof(TodayIssueOnLeave);
    public const string TodayIssueNoSchedule   = nameof(TodayIssueNoSchedule);
    public const string TodayIssueRestDay      = nameof(TodayIssueRestDay);
    public const string TodayOk                = nameof(TodayOk);
    public const string TodayMoveIn            = nameof(TodayMoveIn);
    public const string TodayMoveOut           = nameof(TodayMoveOut);
    public const string TodayNoMovements       = nameof(TodayNoMovements);
    public const string TodayProblemsLabel     = nameof(TodayProblemsLabel);

    public const string NavMasterData          = nameof(NavMasterData);
    public const string NavAdminGroup          = nameof(NavAdminGroup);
    public const string NavDatabaseTools       = nameof(NavDatabaseTools);
    public const string NavScan                = nameof(NavScan);
    public const string UsersMenus             = nameof(UsersMenus);
    public const string UsersMenusHint         = nameof(UsersMenusHint);
    public const string UsersEditMenus         = nameof(UsersEditMenus);
    public const string UsersSelectAll         = nameof(UsersSelectAll);
    public const string UsersClearAll          = nameof(UsersClearAll);
    public const string UsersMenusFor          = nameof(UsersMenusFor);
    public const string UsersMenusSaved        = nameof(UsersMenusSaved);
    public const string UsersMenusNeedOne      = nameof(UsersMenusNeedOne);
    public const string UsersMenusAdminOnly    = nameof(UsersMenusAdminOnly);
    public const string UsersLimitedTip        = nameof(UsersLimitedTip);
}
