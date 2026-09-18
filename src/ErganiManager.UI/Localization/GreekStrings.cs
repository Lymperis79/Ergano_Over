using System.Collections.Generic;

namespace ErganiManager.UI.Localization;

public static class GreekStrings
{
    public static readonly Dictionary<string, string> Strings = new()
    {
        // ── Navigation ────────────────────────────────────────
        [L.NavCompanies]        = "🏢 Εταιρείες",
        [L.NavBranches]         = "🏬 Υποκαταστήματα",
        [L.NavEmployees]        = "👥 Εργαζόμενοι",
        [L.NavUsers]            = "👤 Χρήστες",
        [L.NavSchedules]        = "📅 Προγράμματα",
        [L.NavWorkCards]        = "🕐 Κάρτες Εργασίας",
        [L.NavOvertime]         = "⏱️ Υπερωρίες",
        [L.NavApiLog]           = "📋 Αρχείο API",

        // ── Common actions ────────────────────────────────────
        [L.Save]                = "Αποθήκευση",
        [L.Cancel]              = "Ακύρωση",
        [L.Delete]              = "Διαγραφή",
        [L.Edit]                = "Επεξεργασία",
        [L.Close]               = "Κλείσιμο",
        [L.Load]                = "Φόρτωση",
        [L.Export]              = "Εξαγωγή",
        [L.Import]              = "Εισαγωγή",
        [L.New]                 = "Νέο",
        [L.Search]              = "Αναζήτηση",
        [L.Refresh]             = "Ανανέωση",
        [L.RetryNow]            = "⚡ Επανάληψη Τώρα",
        [L.TestConnection]      = "Δοκιμή Σύνδεσης",

        // ── Auth / Login ──────────────────────────────────────
        [L.AppTitle]            = "Ergani Manager",
        [L.SignIn]              = "Είσοδος",
        [L.Username]            = "Όνομα χρήστη",
        [L.Password]            = "Κωδικός",
        [L.OfflineBanner]       = "⚠️ Η βάση δεδομένων δεν είναι διαθέσιμη — λειτουργία εκτός σύνδεσης. Οι καταχωρήσεις θα συγχρονιστούν όταν αποκατασταθεί η σύνδεση.",
        [L.InvalidCredentials]  = "Λανθασμένο όνομα χρήστη ή κωδικός.",
        [L.Welcome]             = "Καλωσήρθατε",
        [L.Logout]              = "Αποσύνδεση",

        // ── DB Setup ──────────────────────────────────────────
        [L.DbSetupTitle]        = "🗄️ Ρύθμιση Βάσης Δεδομένων",
        [L.DbSetupSubtitle]     = "Επιλέξτε πώς το Ergani Manager θα αποθηκεύει τα δεδομένα του.",
        [L.DbType]              = "Τύπος βάσης δεδομένων",
        [L.DbSqliteNote]        = "Το SQLite δεν χρειάζεται ρύθμιση — δημιουργείται αυτόματα τοπικό αρχείο βάσης δεδομένων.",
        [L.DbServer]            = "Διακομιστής",
        [L.DbName]              = "Όνομα βάσης",
        [L.DbWindowsAuth]       = "Χρήση Windows Authentication",
        [L.DbSaveAndContinue]   = "Αποθήκευση & Συνέχεια",
        [L.DbSchemaIncomplete]  = "⚠️ Η βάση δεδομένων ρυθμίστηκε αλλά το σχήμα δεν εφαρμόστηκε (αποτυχία προηγούμενης εγκατάστασης). Διορθώστε τις ρυθμίσεις και δοκιμάστε ξανά.",

        // ── First Admin ───────────────────────────────────────
        [L.CreateAdminTitle]    = "👤 Δημιουργία Λογαριασμού Διαχειριστή",
        [L.CreateAdminSubtitle] = "Η βάση δεδομένων είναι έτοιμη. Δημιουργήστε τον πρώτο διαχειριστή.",
        [L.ConfirmPassword]     = "Επιβεβαίωση κωδικού",
        [L.PasswordTooShort]    = "Ο κωδικός πρέπει να έχει τουλάχιστον 8 χαρακτήρες.",
        [L.PasswordMismatch]    = "Οι κωδικοί δεν ταιριάζουν.",

        // ── Companies ─────────────────────────────────────────
        [L.Companies]           = "Εταιρείες",
        [L.CompanyName]         = "Επωνυμία",
        [L.TaxId]               = "ΑΦΜ",
        [L.ErganiUsername]      = "Όνομα χρήστη Εργάνη",
        [L.ErganiPassword]      = "Κωδικός Εργάνης (αφήστε κενό για διατήρηση)",
        [L.ErganiBaseUrl]       = "URL Εργάνης",
        [L.Active]              = "Ενεργή",
        [L.TimeRules]           = "⏱ Κανόνες Χρόνου",
        [L.EarlyClockInBlock]   = "Αποκλεισμός πρόωρης έναρξης (λεπτά πριν την έναρξη βάρδιας)",
        [L.EarlyDepartureAlert] = "Ειδοποίηση πρόωρης αποχώρησης (λεπτά πριν τη λήξη βάρδιας)",
        [L.BlockNoSchedule]     = "Αποκλεισμός έναρξης χωρίς πρόγραμμα",
        [L.EmailAlerts]         = "✉ Ειδοποιήσεις Email",
        [L.AutoRetry]           = "🔄 Αυτόματη Επανάληψη Αποτυχημένων",
        [L.AutoRetryDesc]       = "Όταν ενεργοποιηθεί, οι αποτυχημένες υποβολές αποστέλλονται αυτόματα όταν η Εργάνη επανέλθει σε λειτουργία (με αιτιολογία ΕΡΓΟΔΟΤΙΚΑ_ΣΥΣΤΗΜΑΤΑ_ΜΗΔΙΑΘΕΣΙΜΑ).",

        // ── Branches ─────────────────────────────────────────
        [L.Branches]            = "Υποκαταστήματα",
        [L.BranchNumber]        = "Αριθμός Παραρτήματος",
        [L.Address]             = "Διεύθυνση",
        [L.SepeCode]            = "Κωδικός Υπηρεσίας ΣΕΠΕ",
        [L.ActivityCode]        = "Κωδικός Δραστηριότητας (ΚΑΔ)",
        [L.MunicipalCode]       = "Κωδικός Δήμου Καλλικράτη",

        // ── Employees ─────────────────────────────────────────
        [L.Employees]           = "Εργαζόμενοι",
        [L.FirstName]           = "Όνομα",
        [L.LastName]            = "Επώνυμο",
        [L.Amka]                = "ΑΜΚΑ",
        [L.BarcodeId]           = "Κωδικός Barcode",
        [L.ProfessionCode]      = "Κωδικός Ειδικότητας",
        [L.WeeklyWorkdays]      = "Εργάσιμες Ημέρες/Εβδ.",
        [L.Branch]              = "Παράρτημα",
        [L.ImportFromExcel]     = "⬆ Εισαγωγή από Excel",

        // ── Users ─────────────────────────────────────────────
        [L.Users]               = "Χρήστες",
        [L.Role]                = "Ρόλος",
        [L.Admin]               = "Διαχειριστής",
        [L.Operator]            = "Χειριστής",
        [L.ResetPassword]       = "Επαναφορά Κωδικού",
        [L.NewPassword]         = "Νέος κωδικός (τουλάχιστον 8 χαρακτήρες)",
        [L.LockToBranch]        = "Κλείδωμα σε Παράρτημα (προαιρετικό)",

        // ── Terminal ──────────────────────────────────────────
        [L.ScanPrompt]          = "Σαρώστε την κάρτα σας ή εισάγετε κωδικό...",
        [L.ClockInSuccess]      = "✅ ΕΝΑΡΞΗ ΕΡΓΑΣΙΑΣ ΚΑΤΑΓΡΑΦΗΚΕ",
        [L.ClockOutSuccess]     = "✅ ΛΗΞΗ ΕΡΓΑΣΙΑΣ ΚΑΤΑΓΡΑΦΗΚΕ",
        [L.ClockOutEarly]       = "⚠️ ΛΗΞΗ ΕΡΓΑΣΙΑΣ (ΠΡΩΙΜΗ ΑΠΟΧΩΡΗΣΗ)",
        [L.TooEarly]            = "🚫 ΠΡΩΙΜΗ ΕΝΑΡΞΗ ΕΡΓΑΣΙΑΣ",
        [L.NoSchedule]          = "🚫 ΔΕΝ ΥΠΑΡΧΕΙ ΠΡΟΓΡΑΜΜΑ",
        [L.UnknownBadge]        = "🚫 ΑΓΝΩΣΤΗ ΚΑΡΤΑ",
        [L.OfflineMode]         = "⚠️ Εκτός σύνδεσης — οι σαρώσεις θα συγχρονιστούν αυτόματα.",
        [L.Arrival]             = "ΠΡΟΣΕΛΕΥΣΗ",
        [L.Departure]           = "ΑΠΟΧΩΡΗΣΗ",
        [L.ShiftLabel]          = "Βάρδια",
        [L.PleaseReturnIn]      = "Παρακαλώ επιστρέψτε σε {0} λεπτό/ά.",

        // ── Schedules ─────────────────────────────────────────
        [L.Schedules]           = "Προγράμματα Εργασίας",
        [L.WorkType]            = "Τύπος Εργασίας",
        [L.StartTime]           = "Ώρα έναρξης",
        [L.EndTime]             = "Ώρα λήξης",
        [L.Comments]            = "Σχόλια (προαιρετικό)",
        [L.NotSubmitted]        = "⏳ Δεν έχει υποβληθεί στην Εργάνη",
        [L.SubmittedProtocol]   = "✅ Υποβλήθηκε στην Εργάνη — Πρωτόκολλο: {0}",
        [L.ActualClockTimes]    = "Πραγματικές: {0} → {1}",
        [L.ExportMonth]         = "⬇ Εξαγωγή Μήνα",

        // ── Work Cards ────────────────────────────────────────
        [L.WorkCards]           = "Ιστορικό Καρτών Εργασίας",
        [L.EarlyDeparture]      = "Πρόωρη Αποχώρηση",
        [L.MinutesEarly]        = "{0} λεπτά νωρίτερα",
        [L.Protocol]            = "Πρωτόκολλο",

        // ── Overtime ─────────────────────────────────────────
        [L.Overtime]            = "Υπερωρίες",
        [L.OvertimeDate]        = "Ημερομηνία",
        [L.Justification]       = "Αιτιολογία",
        [L.AseeApproval]        = "Έγκριση ΑΣΕΕ (προαιρετικό)",
        [L.Cancelled]           = "Ακυρωμένη",
        [L.CancelOvertime]      = "Ακύρωση ΥΠ",

        // ── Submission Log ────────────────────────────────────
        [L.ApiLog]              = "Αρχείο Υποβολών API",
        [L.FailedSubmissions]   = "🔄 Αποτυχημένες Υποβολές — Αναμονή Επανάληψης",
        [L.PendingRetry]        = "{0} υποβολή/ές σε αναμονή επανάληψης.",
        [L.NoFailedPending]     = "Δεν υπάρχουν αποτυχημένες υποβολές σε αναμονή.",
        [L.FailuresOnly]        = "Μόνο αποτυχίες",
        [L.RequestPayload]      = "Δεδομένα Αιτήματος",
        [L.Response]            = "Απάντηση",

        // ── Status ────────────────────────────────────────────
        [L.Submitted]           = "✅ Υποβλήθηκε",
        [L.Pending]             = "⏳ Σε αναμονή",
        [L.ErrorPrefix]         = "❌ ",
        [L.SuccessPrefix]       = "✅ ",

        // ── Language ──────────────────────────────────────────
        [L.Language]            = "Γλώσσα",
    };
}
