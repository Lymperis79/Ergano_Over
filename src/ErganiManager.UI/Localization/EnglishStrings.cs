using System.Collections.Generic;

namespace ErganiManager.UI.Localization;

public static class EnglishStrings
{
    public static readonly Dictionary<string, string> Strings = new()
    {
        // ── Navigation ────────────────────────────────────────
        [L.NavCompanies]        = "🏢 Companies",
        [L.NavBranches]         = "🏬 Branches",
        [L.NavEmployees]        = "👥 Employees",
        [L.NavUsers]            = "👤 Users",
        [L.NavSchedules]        = "📅 Schedules",
        [L.NavWorkCards]        = "🕐 Work Cards",
        [L.NavOvertime]         = "⏱️ Overtime",
        [L.NavApiLog]           = "📋 API Log",

        // ── Common actions ────────────────────────────────────
        [L.Save]                = "Save",
        [L.Cancel]              = "Cancel",
        [L.Delete]              = "Delete",
        [L.Edit]                = "Edit",
        [L.Close]               = "Close",
        [L.Load]                = "Load",
        [L.Export]              = "Export",
        [L.Import]              = "Import",
        [L.New]                 = "New",
        [L.Search]              = "Search",
        [L.Refresh]             = "Refresh",
        [L.RetryNow]            = "⚡ Retry Now",
        [L.RetrySelected]       = "⚡ Retry selected",
        [L.SelectAllFailed]     = "Select all failed",
        [L.TestConnection]      = "Test Connection",

        // ── Auth / Login ──────────────────────────────────────
        [L.AppTitle]            = "Ergani Manager",
        [L.SignIn]              = "Sign In",
        [L.Username]            = "Username",
        [L.Password]            = "Password",
        [L.OfflineBanner]       = "⚠️ Database unavailable — working in offline mode. Only previously synced users can log in, and clock-in/out scans will be queued until the connection is restored.",
        [L.InvalidCredentials]  = "Invalid username or password.",
        [L.Welcome]             = "Welcome",
        [L.Logout]              = "Log out",

        // ── DB Setup ──────────────────────────────────────────
        [L.DbSetupTitle]        = "🗄️ Database Configuration",
        [L.DbSetupSubtitle]     = "Choose how Ergani Manager will store its data. You can change this later from Settings.",
        [L.DbType]              = "Database type",
        [L.DbSqliteNote]        = "SQLite needs no configuration — a local database file will be created automatically.",
        [L.DbServer]            = "Server",
        [L.DbName]              = "Database name",
        [L.DbWindowsAuth]       = "Use Windows Authentication",
        [L.DbSaveAndContinue]   = "Save & Continue",
        [L.DbSchemaIncomplete]  = "⚠️ The database was configured but the schema could not be applied (a previous setup attempt failed). Fix the connection settings below and try again.",

        // ── First Admin ───────────────────────────────────────
        [L.CreateAdminTitle]    = "👤 Create Administrator Account",
        [L.CreateAdminSubtitle] = "The database is ready. Create the first administrator account — this account can manage all companies.",
        [L.ConfirmPassword]     = "Confirm password",
        [L.PasswordTooShort]    = "Password must be at least 8 characters.",
        [L.PasswordMismatch]    = "Passwords do not match.",

        // ── Companies ─────────────────────────────────────────
        [L.Companies]           = "Companies",
        [L.CompanyName]         = "Name",
        [L.TaxId]               = "Tax ID (AFM)",
        [L.ErganiUsername]      = "Ergani Username",
        [L.ErganiPassword]      = "Ergani Password (leave blank to keep unchanged)",
        [L.ErganiBaseUrl]       = "Ergani Base URL",
        [L.Active]              = "Active",
        [L.TimeRules]           = "⏱ Time Rules",
        [L.NightWorkingHours] = "🌙 Night working hours",
        [L.NightWorkingHoursDescription] = "Actual working time inside this interval is shown in the Night hours column of the Declared vs actual report. The default is 22:00 until 06:00 the next morning.",
        [L.NightStart] = "Night start",
        [L.NightEnd] = "Night end",
        [L.NightSettingsSaved] = "✅ Night working hours saved.",
        [L.NightSettingsApply] = "Generate the report again to apply the change.",
        [L.ReportNightHours] = "Night hours",
        [L.EarlyClockInBlock]   = "Block clock-in earlier than (minutes before shift start)",
        [L.EarlyDepartureAlert] = "Alert if clock-out earlier than (minutes before shift end)",
        [L.BlockNoSchedule]     = "Block clock-in when no schedule is on file",
        [L.EmailAlerts]         = "✉ Email Alerts",
        [L.AutoRetry]           = "🔄 Auto-Retry Failed Submissions",
        [L.AutoRetryDesc]       = "When enabled, failed clock-in/out submissions are automatically resent (with EMPLOYER_SYSTEMS_UNAVAILABLE justification) once the Ergani service recovers.",

        // ── Branches ─────────────────────────────────────────
        [L.Branches]            = "Branches",
        [L.BranchNumber]        = "Branch Number",
        [L.Address]             = "Address",
        [L.SepeCode]            = "SEPE Service Code",
        [L.ActivityCode]        = "Activity Code (KAD)",
        [L.MunicipalCode]       = "Kallikratis Municipal Code",

        // ── Employees ─────────────────────────────────────────
        [L.Employees]           = "Employees",
        [L.FirstName]           = "First Name",
        [L.LastName]            = "Last Name",
        [L.Amka]                = "Social Security Number (AMKA)",
        [L.BarcodeId]           = "Barcode ID",
        [L.ProfessionCode]      = "Profession Code",
        [L.WeeklyWorkdays]      = "Weekly Workdays",
        [L.Branch]              = "Branch",
        [L.ImportFromExcel]     = "⬆ Import from Excel",

        // ── Users ─────────────────────────────────────────────
        [L.Users]               = "Users",
        [L.Role]                = "Role",
        [L.Admin]               = "Admin",
        [L.Operator]            = "Operator",
        [L.ResetPassword]       = "Reset Password",
        [L.NewPassword]         = "New password (min 8 characters)",
        [L.LockToBranch]        = "Lock to Branch (optional)",

        // ── Terminal ──────────────────────────────────────────
        [L.ScanPrompt]          = "Scan your badge or enter ID...",
        [L.ClockInSuccess]      = "✅ CLOCK-IN REGISTERED",
        [L.ClockOutSuccess]     = "✅ CLOCK-OUT REGISTERED",
        [L.ClockOutEarly]       = "⚠️ CLOCK-OUT REGISTERED (EARLY)",
        [L.TooEarly]            = "🚫 TOO EARLY TO CLOCK IN",
        [L.NoSchedule]          = "🚫 NO SCHEDULE ON FILE",
        [L.UnknownBadge]        = "🚫 UNKNOWN BADGE",
        [L.OfflineMode]         = "⚠️ Working offline — scans will sync automatically when reconnected.",
        [L.Arrival]             = "ARRIVAL",
        [L.Departure]           = "DEPARTURE",
        [L.ShiftLabel]          = "Shift",
        [L.PleaseReturnIn]      = "Please return in {0} minute(s).",

        // ── Schedules ─────────────────────────────────────────
        [L.Schedules]           = "Schedules",
        [L.WorkType]            = "Work Type",
        [L.StartTime]           = "Start time",
        [L.EndTime]             = "End time",
        [L.Comments]            = "Comments (optional)",
        [L.NotSubmitted]        = "⏳ Not yet submitted to Ergani",
        [L.SubmittedProtocol]   = "✅ Submitted to Ergani — Protocol: {0}",
        [L.ActualClockTimes]    = "Actual: {0} → {1}",
        [L.ExportMonth]         = "⬇ Export Month",

        // ── Work Cards ────────────────────────────────────────
        [L.WorkCards]           = "Work Card History",
        [L.EarlyDeparture]      = "Early Departure",
        [L.MinutesEarly]        = "{0} min early",
        [L.Protocol]            = "Protocol",
        [L.OpenErganiPortal] = "🌐 Open Ergani portal",
        [L.OpenWorkCardsPortal] = "🌐 Open Work Cards portal",
        [L.SchedulePortal] = "🌐 Schedule Portal",
        [L.SchedulePortalDescription] = "These settings are used ONLY by the Open Ergani portal button in the Schedules form. The login username/password still come from the selected company.",
        [L.WorkCardsPortal] = "🌐 Work Cards Portal",
        [L.WorkCardsPortalDescription] = "These settings are used ONLY by the Open Work Cards portal button. Today\'s date is entered in both date fields and the Search button is pressed.",
        [L.PortalBaseAddress] = "Portal base address",
        [L.PortalTargetPage] = "Page after login",
        [L.PortalFromDateFieldId] = "FROM date field ID",
        [L.PortalToDateFieldId] = "TO date field ID",
        [L.PortalDateFormat] = "Date format",
        [L.PortalAutoSearch] = "Automatically press Search after the dates are filled",
        [L.PortalAutoDownload] = "After Search, automatically download the Excel export",
        [L.PortalSearchButtonId] = "Search button ID",
        [L.PortalDownloadFolder] = "Excel download folder",
        [L.SaveSchedulePortal] = "💾 Save Schedule Portal settings",
        [L.SaveWorkCardsPortal] = "💾 Save Work Cards Portal settings",
        [L.SchedulePortalSaved] = "✅ Schedule portal settings saved.",
        [L.WorkCardsPortalSaved] = "✅ Work Cards portal settings saved.",
        [L.CouldNotSaveSchedulePortal] = "❌ Could not save Schedule portal settings: {0}",
        [L.CouldNotSaveWorkCardsPortal] = "❌ Could not save Work Cards portal settings: {0}",
        [L.PortalInvalidUrl] = "Invalid {0} portal URL.",
        [L.PortalDateFormatPlaceholder] = "dd/MM/yyyy",
        [L.PortalDownloadFolderPlaceholder] = "Empty = Documents\\\\ErganiManager\\\\Downloads",

        // ── Overtime ─────────────────────────────────────────
        [L.Overtime]            = "Overtime",
        [L.OvertimeDate]        = "Date",
        [L.Justification]       = "Justification",
        [L.AseeApproval]        = "ASEE Approval (optional)",
        [L.Cancelled]           = "Cancelled",
        [L.CancelOvertime]      = "Cancel OT",

        // ── Submission Log ────────────────────────────────────
        [L.ApiLog]              = "API Submission Log",
        [L.FailedSubmissions]   = "🔄 Failed Submissions — Pending Retry",
        [L.PendingRetry]        = "{0} submission(s) awaiting retry.",
        [L.NoFailedPending]     = "No pending failed submissions.",
        [L.FailuresOnly]        = "Failures only",
        [L.RequestPayload]      = "Request Payload",
        [L.Response]            = "Response",

        // ── Status ────────────────────────────────────────────
        [L.Submitted]           = "✅ Submitted",
        [L.Pending]             = "⏳ Pending",
        [L.ErrorPrefix]         = "❌ ",
        [L.SuccessPrefix]       = "✅ ",

        // ── Ergani user types ─────────────────────────────────
        [L.UserType]            = "User Type (Usertype)",
        [L.UsertypeExternal]    = "External",
        [L.UsertypeErgani]      = "Login with \"ERGANI\" credentials",
        [L.UsertypeEfka]        = "Login with credentials for Construction Works from EFKA",

        [L.NavReports] = "📊 Reports",

        // ── Late reasons (f_aitiologia) ───────────────────────
        [L.AitiologiaPower] = "Power outage / telecommunications problem (POWER_OUTAGE)",
        [L.AitiologiaEmployer] = "Employer systems problem (EMPLOYER_SYSTEMS_UNAVAILABLE)",
        [L.AitiologiaErgani] = "Problem connecting to ERGANI (ERGANI_SYSTEMS_UNAVAILABLE)",

        // ── Administration / Help ─────────────────────────────
        [L.NavHelp] = "❓ Help",
        [L.NavAdmin] = "🛠 Administration",
        [L.AdminTitle] = "Administration",
        [L.AdminDatabase] = "Database",
        [L.AdminBackup] = "💾 Backup database…",
        [L.AdminLocalCache] = "Temporary local database (cache)",
        [L.AdminDeleteCache] = "🧹 Delete local cache",
        [L.AdminDangerZone] = "Danger zone",
        [L.AdminDeleteDb] = "🗑 Delete database…",
        [L.AdminGeneralSettings] = "General",
        [L.AdminHoursSettings] = "Hours settings",
        [L.AdminPortalSettings] = "Portal settings",
        [L.AdminDatabaseSettings] = "Database & cache",
        [L.AdminShifts] = "Predefined working shifts",
        [L.AdminShiftsDescription] = "Configure three reusable shifts. They are available as a drop-down when creating or editing an employee schedule. The selected shift fills the start and end times below, which can still be adjusted for that schedule.",
        [L.ShiftName] = "Shift name",
        [L.ShiftStart] = "Start",
        [L.ShiftEnd] = "End",
        [L.ShiftSelection] = "Predefined shift",
        [L.ShiftSelectionDescription] = "Select a predefined shift to fill the working times below.",
        [L.ThreeShiftsRequired] = "Exactly three shifts must be configured.",
        [L.ShiftValuesRequired] = "All three shifts need a name, start time and end time.",
        [L.ShiftTimesInvalid] = "Shift times must be valid times between 00:00 and 23:59.",
        [L.ShiftTimesEqual] = "A shift start and end time cannot be the same.",
        [L.AdminConfirm] = "Confirm",
        [L.HelpTitle] = "Help",
        [L.HelpEditAction] = "✏ Edit",
        [L.HelpResetDefault] = "↺ Restore default",

        // ── Language ──────────────────────────────────────────
        [L.Language]            = "Language",
    };
}
