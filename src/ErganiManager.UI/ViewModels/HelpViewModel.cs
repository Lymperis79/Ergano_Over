using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Globalization;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ErganiManager.LocalCache;
using ErganiManager.Core.Models;

namespace ErganiManager.UI.ViewModels;

/// <summary>
/// Searchable, bilingual in-app help. Topic overrides are saved separately per language,
/// so updating the built-in guide never overwrites existing administrator-authored help files.
/// </summary>
public partial class HelpViewModel : ViewModelBase, IAdminSectionViewModel
{
    [ObservableProperty] private string _searchText = string.Empty;
    [ObservableProperty] private HelpTopic? _selectedTopic;
    [ObservableProperty] private string _editText = string.Empty;
    [ObservableProperty] private bool _isEditing;
    [ObservableProperty] private string _statusMessage = string.Empty;

    public ObservableCollection<HelpTopic> FilteredTopics { get; } = new();

    public string PageSubtitle => IsGreek
        ? "Οδηγός χρήσης, αναζήτηση και αντιμετώπιση προβλημάτων"
        : "User guide, searchable instructions and troubleshooting";

    public string SearchPlaceholder => IsGreek
        ? "Αναζήτηση σε θέματα, λειτουργίες και σφάλματα…"
        : "Search topics, features and errors…";

    public string TopicsHeading => IsGreek ? "ΘΕΜΑΤΑ ΒΟΗΘΕΙΑΣ" : "HELP TOPICS";
    public string NoResultsText => IsGreek
        ? "Δεν βρέθηκαν θέματα. Δοκιμάστε διαφορετικές λέξεις."
        : "No topics found. Try different keywords.";
    public string EditHint => IsGreek
        ? "Επεξεργαστείτε το επιλεγμένο θέμα. Οι αλλαγές αποθηκεύονται μόνο για αυτή τη γλώσσα."
        : "Edit the selected topic. Changes are saved for this language only.";
    public bool HasNoSearchResults => !string.IsNullOrWhiteSpace(SearchText) && FilteredTopics.Count == 0;
    public string SelectTopicText => IsGreek
        ? "Επιλέξτε ένα θέμα από αριστερά για να δείτε τις οδηγίες."
        : "Choose a topic on the left to read its instructions.";
    public string EditTopicText => IsGreek ? "Επεξεργασία θέματος" : "Edit topic";
    public string RestoreDefaultText => IsGreek ? "Επαναφορά προεπιλογής" : "Restore default";
    public string SearchCountText => IsGreek
        ? $"{FilteredTopics.Count} θέματα"
        : $"{FilteredTopics.Count} topics";
    public bool CanRestoreDefault => !IsEditing && SelectedTopic is not null && SelectedTopic.Id != "legacy-custom";

    private bool IsGreek => Loc.CurrentLanguage == AppLanguage.Greek;
    private string LanguageSuffix => IsGreek ? "el" : "en";

    public HelpViewModel()
    {
        LoadTopics();
        Loc.LanguageChanged += (_, _) =>
        {
            if (!IsEditing)
            {
                OnPropertyChanged(nameof(PageSubtitle));
                OnPropertyChanged(nameof(SearchPlaceholder));
                OnPropertyChanged(nameof(TopicsHeading));
                OnPropertyChanged(nameof(NoResultsText));
                OnPropertyChanged(nameof(EditHint));
                OnPropertyChanged(nameof(SelectTopicText));
                OnPropertyChanged(nameof(EditTopicText));
                OnPropertyChanged(nameof(RestoreDefaultText));
                OnPropertyChanged(nameof(SearchCountText));
                LoadTopics();
            }
        };
    }

    public void Initialize(UserSession session) => LoadTopics();

    partial void OnSearchTextChanged(string value)
    {
        ApplySearch();
        OnPropertyChanged(nameof(HasNoSearchResults));
    }

    partial void OnSelectedTopicChanged(HelpTopic? value)
    {
        if (value is null)
        {
            EditText = string.Empty;
            OnPropertyChanged(nameof(CanRestoreDefault));
            return;
        }

        EditText = value.Content;
        IsEditing = false;
        OnPropertyChanged(nameof(CanRestoreDefault));
    }

    partial void OnIsEditingChanged(bool value)
    {
        OnPropertyChanged(nameof(EditTopicText));
        OnPropertyChanged(nameof(CanRestoreDefault));
    }

    private string LegacyFilePath => Path.Combine(AppPaths.GetAppDataFolder(),
        IsGreek ? "help.el.txt" : "help.en.txt");

    private string OverridePath(string topicId) => Path.Combine(AppPaths.GetAppDataFolder(),
        $"help.{LanguageSuffix}.topic.{topicId}.txt");

    private void LoadTopics()
    {
        var selectedId = SelectedTopic?.Id;
        var topics = BuildTopics();

        // Keep the original editable-help file intact. If a previous version created one,
        // expose it as a separate legacy article instead of silently replacing its contents.
        if (File.Exists(LegacyFilePath))
        {
            try
            {
                var legacyOverride = OverridePath("legacy-custom");
                var legacyText = File.Exists(legacyOverride)
                    ? File.ReadAllText(legacyOverride)
                    : File.ReadAllText(LegacyFilePath);
                topics.Add(new HelpTopic(
                    "legacy-custom",
                    IsGreek ? "Προσαρμοσμένο περιεχόμενο" : "Previously saved custom help",
                    IsGreek ? "Παλαιότερο περιεχόμενο βοήθειας" : "Content saved by an earlier version",
                    IsGreek ? "Το αρχικό αρχείο διατηρείται." : "The original saved file is preserved.",
                    legacyText,
                    "legacy custom previous old"));
            }
            catch (Exception ex)
            {
                StatusMessage = $"⚠ {ex.Message}";
            }
        }

        foreach (var topic in topics.ToArray())
        {
            try
            {
                var overrideFile = OverridePath(topic.Id);
                if (File.Exists(overrideFile))
                    topics[topics.IndexOf(topic)] = topic with { Content = File.ReadAllText(overrideFile) };
            }
            catch (Exception ex)
            {
                StatusMessage = $"⚠ {ex.Message}";
            }
        }

        _allTopics = topics;
        ApplySearch();
        SelectedTopic = FilteredTopics.FirstOrDefault(x => x.Id == selectedId)
            ?? FilteredTopics.FirstOrDefault(x => x.Id == "getting-started")
            ?? FilteredTopics.FirstOrDefault();
        OnPropertyChanged(nameof(PageSubtitle));
        OnPropertyChanged(nameof(SearchPlaceholder));
        OnPropertyChanged(nameof(TopicsHeading));
        OnPropertyChanged(nameof(NoResultsText));
        OnPropertyChanged(nameof(EditHint));
        OnPropertyChanged(nameof(SelectTopicText));
        OnPropertyChanged(nameof(EditTopicText));
        OnPropertyChanged(nameof(RestoreDefaultText));
        OnPropertyChanged(nameof(SearchCountText));
        OnPropertyChanged(nameof(CanRestoreDefault));
    }

    private System.Collections.Generic.List<HelpTopic> _allTopics = new();

    private void ApplySearch()
    {
        var query = NormalizeSearch((SearchText ?? string.Empty).Trim());
        var terms = query.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var results = _allTopics.Where(topic =>
        {
            var searchable = NormalizeSearch($"{topic.Title} {topic.Category} {topic.Summary} {topic.Content} {topic.Keywords}");
            return terms.Length == 0 || terms.All(term =>
                SearchAlternatives(term).Any(alternative =>
                    searchable.Contains(NormalizeSearch(alternative), StringComparison.Ordinal)));
        }).ToList();

        FilteredTopics.Clear();
        foreach (var topic in results)
            FilteredTopics.Add(topic);

        if (SelectedTopic is not null && !FilteredTopics.Any(x => x.Id == SelectedTopic.Id))
            SelectedTopic = FilteredTopics.FirstOrDefault();
        else if (SelectedTopic is null && FilteredTopics.Count > 0)
            SelectedTopic = FilteredTopics[0];

        OnPropertyChanged(nameof(SearchCountText));
        OnPropertyChanged(nameof(HasNoSearchResults));
    }

    private static string NormalizeSearch(string value)
    {
        var decomposed = value.Normalize(NormalizationForm.FormD);
        return new string(decomposed
            .Where(character => CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark)
            .ToArray()).ToLowerInvariant();
    }

    // Common aliases let users search in English even when the guide is Greek, and vice versa.
    private static string[] SearchAlternatives(string term) => term switch
    {
        "overtime" or "υπερωρια" or "υπερωριες" or "extra" => ["overtime", "υπερωρια", "υπερωριες", "extra"],
        "schedule" or "schedules" or "ωραριο" or "προγραμμα" or "προγραμματα" => ["schedule", "schedules", "ωραριο", "προγραμμα", "προγραμματα"],
        "employee" or "employees" or "εργαζομενος" or "εργαζομενοι" => ["employee", "employees", "εργαζομενος", "εργαζομενοι"],
        "branch" or "branches" or "υποκαταστημα" or "υποκαταστηματα" => ["branch", "branches", "υποκαταστημα", "υποκαταστηματα"],
        "company" or "companies" or "εταιρεια" or "εταιρειες" => ["company", "companies", "εταιρεια", "εταιρειες"],
        "leave" or "holiday" or "holidays" or "αδεια" or "αδειες" or "αργιες" => ["leave", "holiday", "holidays", "αδεια", "αδειες", "αργιες"],
        "work" or "card" or "cards" or "workcard" or "workcards" or "καρτα" or "καρτες" or "σαρωση" or "σαρωσεις" => ["work", "card", "cards", "workcard", "workcards", "καρτα", "καρτες", "σαρωση", "σαρωσεις"],
        "submission" or "submissions" or "υποβολη" or "υποβολες" => ["submission", "submissions", "υποβολη", "υποβολες"],
        "failed" or "failure" or "error" or "errors" or "σφαλμα" or "σφαλματα" or "απετυχε" or "αποτυχια" => ["failed", "failure", "error", "errors", "σφαλμα", "σφαλματα", "απετυχε", "αποτυχια"],
        "pending" or "εκκρεμης" or "εκκρεμη" or "εκκρεμεις" => ["pending", "εκκρεμης", "εκκρεμη", "εκκρεμεις"],
        "user" or "users" or "χρηστης" or "χρηστες" => ["user", "users", "χρηστης", "χρηστες"],
        "today" or "daily" or "σημερα" or "ημερησια" => ["today", "daily", "σημερα", "ημερησια"],
        "database" or "databases" or "db" or "βαση" or "δεδομενων" => ["database", "databases", "db", "βαση", "δεδομενων"],
        "backup" or "backups" or "αντιγραφο" or "ασφαλειας" => ["backup", "backups", "αντιγραφο", "ασφαλειας"],
        "email" or "mail" or "smtp" or "αλληλογραφια" or "ηλεκτρονικο" => ["email", "mail", "smtp", "αλληλογραφια", "ηλεκτρονικο"],
        "permission" or "permissions" or "δικαιωμα" or "δικαιωματα" => ["permission", "permissions", "δικαιωμα", "δικαιωματα"],
        "offline" or "online" or "εκτος" or "συνδεσης" => ["offline", "online", "εκτος", "συνδεσης"],
        "report" or "reports" or "αναφορα" or "αναφορες" => ["report", "reports", "αναφορα", "αναφορες"],
        "protocol" or "πρωτοκολλο" or "πρωτοκολλου" => ["protocol", "πρωτοκολλο", "πρωτοκολλου"],
        "import" or "imported" or "εισαγωγη" or "εισηχθη" => ["import", "imported", "εισαγωγη", "εισηχθη"],
        "sync" or "synchronize" or "synchronization" or "συγχρονισμος" => ["sync", "synchronize", "synchronization", "συγχρονισμος"],
        _ => [term]
    };

    [RelayCommand]
    private void StartEdit()
    {
        if (SelectedTopic is null) return;
        EditText = SelectedTopic.Content;
        IsEditing = true;
        StatusMessage = string.Empty;
    }

    [RelayCommand]
    private void CancelEdit()
    {
        EditText = SelectedTopic?.Content ?? string.Empty;
        IsEditing = false;
    }

    [RelayCommand]
    private void SaveEdit()
    {
        if (SelectedTopic is null) return;

        try
        {
            Directory.CreateDirectory(AppPaths.GetAppDataFolder());
            File.WriteAllText(OverridePath(SelectedTopic.Id), EditText);
            var updated = SelectedTopic with { Content = EditText };
            var index = _allTopics.FindIndex(x => x.Id == updated.Id);
            if (index >= 0) _allTopics[index] = updated;
            SelectedTopic = updated;
            ApplySearch();
            IsEditing = false;
            StatusMessage = IsGreek ? "✓ Το θέμα αποθηκεύτηκε." : "✓ Topic saved.";
        }
        catch (Exception ex)
        {
            StatusMessage = IsGreek
                ? $"❌ Αποτυχία αποθήκευσης: {ex.Message}"
                : $"❌ Could not save topic: {ex.Message}";
        }
    }

    [RelayCommand]
    private void RestoreDefault()
    {
        if (SelectedTopic is null || SelectedTopic.Id == "legacy-custom") return;

        try
        {
            var path = OverridePath(SelectedTopic.Id);
            if (File.Exists(path)) File.Delete(path);
            var defaultTopic = BuildTopics().FirstOrDefault(x => x.Id == SelectedTopic.Id);
            if (defaultTopic is null) return;

            var index = _allTopics.FindIndex(x => x.Id == defaultTopic.Id);
            if (index >= 0) _allTopics[index] = defaultTopic;
            SelectedTopic = defaultTopic;
            ApplySearch();
            StatusMessage = IsGreek ? "✓ Επαναφέρθηκε το προεπιλεγμένο θέμα." : "✓ Default topic restored.";
        }
        catch (Exception ex)
        {
            StatusMessage = IsGreek
                ? $"❌ Αποτυχία επαναφοράς: {ex.Message}"
                : $"❌ Could not restore default: {ex.Message}";
        }
    }

    private System.Collections.Generic.List<HelpTopic> BuildTopics() => IsGreek
        ? BuildGreekTopics()
        : BuildEnglishTopics();

    private static System.Collections.Generic.List<HelpTopic> BuildEnglishTopics() =>
    [
        new("getting-started", "Getting started", "First steps",
            "Understand the workspace, company selector and navigation.",
            """
            ERGANI MANAGER — GETTING STARTED

1. Sign in with your application account. Your available screens depend on your assigned permissions.
2. If you can manage more than one company, choose the company in the selector at the top before editing or submitting records.
3. Use the left navigation to open Dashboard, Companies, Branches, Employees, Schedules, Work Cards, Overtime, Reports, logs and Help.
4. The selected company determines which company-specific records and settings are shown. Check the company name before sending data to Ergani.
5. Use Refresh or the relevant screen's reload action when you need to fetch the latest available data.

Important
• Application data and Ergani data are not the same thing. A saved local record is not proof that Ergani accepted it.
• Read the submission result and protocol information after sending.
• Never share your password or exported records with people who do not need access.
""",
            "start login sign in navigation company select language permissions"),

        new("companies", "Company & configuration", "Companies and Ergani credentials",
            "Company details, user type, environment and connection checks.",
            """
            COMPANIES AND ERGANI CREDENTIALS

1. Open Companies and select or add the company you need to configure.
2. Enter the company details and the credentials required by that company's Ergani account.
3. Select the correct user type:
   • 01 — External.
   • 02 — Login with Ergani credentials.
   • 03 — Login with credentials for EFKA construction/technical projects.
4. Check the configured Ergani API base URL. Use the trial environment only for testing and production only when you intend to submit real records.
5. Use the connection test, when available, before importing or submitting data.
6. Confirm the active company selector before performing company-specific work.

Security
• Credentials are sensitive. Restrict access to the company form and do not put passwords in screenshots, Help articles or logs.
• A successful connection test confirms connectivity/authentication at that moment; it does not validate every future submission.
""",
            "company companies credentials username password user type 01 02 03 external trial production API connection"),

        new("branches-employees", "Company & configuration", "Branches and employees",
            "Maintain company structure and employee records before scheduling.",
            """
            BRANCHES AND EMPLOYEES

Branches
1. Open Branches for the selected company.
2. Import or maintain the branch records used by the application.
3. Check the branch association when an employee or submission is missing from a list.

Employees
1. Open Employees and select the appropriate company.
2. Import or edit employee details and confirm the branch association.
3. Verify active/inactive status and the employee identifier before preparing schedules, overtime, leave or work-card records.
4. If an employee is missing, refresh/import the latest available records and check that the correct company is selected.

Data quality
• Do not create duplicate employee records to work around a missing entry.
• Employee details in the app should agree with the corresponding Ergani record.
• A change in this application does not necessarily update the Ergani portal until the relevant supported submission is made.
""",
            "branch branches employee employees import active inactive employee id association"),

        new("dashboard", "Monitoring & records", "Dashboard",
            "Understand the selected-company overview and recent submission activity.",
            """
            DASHBOARD

The dashboard is intended to summarize the currently selected company, not combine unrelated companies.

Depending on the data available, it shows:
• Active branches and employees.
• Submission activity for the current month.
• Failed submissions in the displayed time window.
• Pending schedule, overtime and leave items.
• A breakdown of submission outcomes by category.
• Recent submission records and automatic-backup status.

How to use it
1. Confirm the selected company in the application header.
2. Use Refresh to update the summary.
3. Open the relevant operational screen or submission log to investigate a count or error.
4. Treat dashboard counts as an overview; the underlying record and its submission response are the source for resolving individual cases.

A dashboard total is not itself confirmation that a submission was accepted by Ergani.
""",
            "dashboard cards counts pending failed successful refresh company metrics"),

        new("schedules", "Daily operations", "Schedules and bulk changes",
            "Edit calendar days, apply changes in bulk, clone schedules and submit.",
            """
            SCHEDULES

Reviewing the calendar
1. Open Schedules and choose the employee and month.
2. Check the weekday/date alignment and the selected employee before editing.
3. Select a day to open its details. Use the bulk action when the same change applies to several selected days.
4. Use the clone/copy workflow carefully: verify source employee, destination employees and date range before confirming.

Hours and overtime
• The application may generate overtime records from schedule days that exceed the configured normal-hours rule. Review generated overtime in the Overtime screen before submitting it.
• Check start/end times, breaks and any company-specific working-time settings.
• Do not assume that editing a schedule withdraws or changes a record already sent to Ergani.

Submitting
1. Review pending changes and the selected dates.
2. Use the relevant schedule submission action.
3. Read the response and protocol number, then check the submission log.
4. If submission fails, keep the saved record and investigate the error before retrying.

Selection behavior can depend on the active screen and current version. Confirm the pending-item count and submission confirmation shown by the application before sending.
""",
            "schedule schedules calendar day bulk apply clone copy hours overtime pending submit protocol"),

        new("overtime", "Daily operations", "Overtime",
            "Create, review and submit overtime records for one or multiple employees.",
            """
            OVERTIME

Creating or reviewing records
1. Open Overtime and select the appropriate company.
2. Create a record and choose the employee or employees, date, time range and required justification.
3. Review overtime generated from schedule hours as well as overtime entered manually.
4. Confirm that the date and time range are correct and that the record is eligible for submission.

Submitting
1. Select only the eligible unsent records you intend to send.
2. Start the submission and wait for the result.
3. Review the success/failure summary and any protocol number.
4. Use the Overtime Log or API submission log, where available, to investigate failures.

Important
• A saved or pending overtime record is not the same as a successful Ergani submission.
• A successfully submitted row should be treated as sent; do not submit it again unless the application explicitly presents it as eligible.
• If a schedule change affects overtime that was already sent, correct the discrepancy through the appropriate Ergani workflow. Editing a local record alone does not retract a submission.
• Check the justification and working-time rules that apply to the company before sending.
""",
            "overtime extra hours generated schedule justification multiple employees pending submit failed log"),

        new("leave-holidays", "Daily operations", "Holidays and leave",
            "Record leave, review entitlement information and submit supported leave types.",
            """
            HOLIDAYS AND LEAVE

Before saving
1. Open the holiday/leave workflow from the relevant screen.
2. Select the employee, leave type and date or date range.
3. Complete any required reference year and entitled-leave-day information.
4. Check that the selected leave type matches the intended absence.

Review and submit
1. Review pending leave records and their current status.
2. Submit only records that are eligible and intended to be sent.
3. Check the returned result, protocol information and submission history.
4. If a submission fails, open the record and correct the invalid or missing information before retrying.

Important
• Leave types and payment categories may be configured by an administrator. Their codes must match the applicable Ergani definitions.
• A leave entry saved in the application is not proof that Ergani accepted it.
• Records already submitted to Ergani may need a corrective workflow; deleting or changing a local record does not undo the portal submission.
• Keep entitlement records and history for audit/reference purposes.
""",
            "holiday holidays leave annual leave entitlement reference year days failed edit submit"),

        new("work-cards", "Daily operations", "Work Cards and scans",
            "Review work-card history, scan outcomes, manual entries and late reasons.",
            """
            WORK CARDS

Reviewing history
1. Open Work Cards / Work Card History and select the relevant employee and date range.
2. Review recorded card events and their current status.
3. If an expected event is missing, check the date range, employee selection, connectivity and available import/synchronization actions.

Clock-in and blocked scans
• A scan may be blocked by the company's clock-in rules, such as a schedule requirement. Check the company settings and the displayed block reason.
• Blocked scans should be reviewed in the relevant log; a blocked scan is not the same as a successful clock-in.
• If the rule is intentionally disabled, verify the saved company setting and test again with a known employee/date.

Manual or late card entries
• Enter the correct date and time and provide a late-entry reason when the configured time limit requires one.
• Check the form's warning about offline behavior before using a manual entry.
• Some manual submissions require an immediate connection and may not be queued for later sending.

After an error, preserve the record and its response details while investigating. Do not repeatedly scan or resubmit without checking whether the original event was accepted.
""",
            "workcard work cards scan clock in blocked scan late reason manual time limit history"),

        new("api-submissions", "Monitoring & records", "Ergani submissions and API log",
            "Find submission outcomes, protocol details, error responses and documents.",
            """
            ERGANI SUBMISSIONS AND API LOG

Use the API submission log to trace what the application attempted to send and what response it received.

1. Open the API / Submission Log.
2. Filter or locate the record by company, employee, date, category or submission status where those filters are available.
3. Read the full error/response details. Check whether the failure points to connectivity, authentication, missing data, validation or an Ergani service issue.
4. When a protocol number is returned, retain it for reference.
5. Open or save the associated PDF/document when the screen provides that action.
6. After correcting the cause, retry only records that are still eligible for submission. If automatic retry is enabled, queued/failed items may be retried when connectivity returns; confirm the actual result in the log.

Status meanings
• Pending / not sent: saved locally but not confirmed as accepted by Ergani.
• Successful / submitted: the application received a successful response for that operation.
• Failed: the operation did not complete successfully; inspect the response before retrying.
• Imported: data was brought into the application; this label alone does not mean a new submission was sent.

The exact status labels depend on the workflow. Use the record's response and protocol details as the authoritative evidence.
""",
            "API submission log protocol PDF document response error failed pending submitted imported"),

        new("email", "Monitoring & records", "Email settings and notifications",
            "Configure company email settings, test SMTP and investigate delivery failures.",
            """
            EMAIL SETTINGS AND NOTIFICATIONS

Configuration
1. Open the company settings that contain email/SMTP options.
2. Enter the server, port, security mode, sender and authentication details required by your email provider.
3. Verify the recipient settings for the intended alerts.
4. Use the Test Email action when available and check both the in-app result and the recipient mailbox.

If email does not arrive
• A successful SMTP test usually indicates that the test message was accepted by the configured mail server; it does not guarantee delivery to the recipient's inbox.
• Check the server/port, TLS or SSL setting, username/password, sender permissions, recipient address and spam/quarantine folders.
• Review the email log for the timestamp and reported error.
• If the PC has no internet connection, the application cannot deliver a message at that moment. Any retry or queued-delivery behavior depends on the configured implementation; check the log rather than assuming a message was sent.
• Restore connectivity and verify whether the application retries the message or whether a manual retry is required.

Keep mail credentials private and do not paste them into support requests.
""",
            "email SMTP mail test message notification offline internet failed delivery retry password"),

        new("backup", "Administration", "Database, offline mode and backups",
            "Understand database availability, offline cache and automatic backup behavior.",
            """
            DATABASE, OFFLINE MODE AND BACKUPS

Database connection
• The application can use a configured central database and an offline/local cache, depending on deployment settings.
• If the database is unavailable, read the exact connection/setup message. Check server availability, network/VPN, database name, credentials and provider configuration before changing anything.
• An offline cache is not necessarily a full replacement for the central database. Check synchronization status after connectivity returns.

Automatic backups
• Automatic backup scheduling runs inside the application process and therefore only while the application is open.
• Backup behavior and supported providers depend on the configured database provider. Do not assume a SQLite file-copy backup is suitable for SQL Server or MariaDB.
• Review the backup settings, destination folder, retention count and last-success information.
• A displayed backup status should be confirmed by checking that the expected backup file exists and is usable.

Safe recovery
1. Before database maintenance, make a verified backup using the supported method for that provider.
2. Confirm which database is central and which is a local/offline cache.
3. Do not delete or recreate a database merely to clear an error. First diagnose the connection and schema status.
4. Preserve logs and existing records when troubleshooting.
5. For restore/migration operations, follow the deployment-specific procedure and involve the database administrator if unsure.
""",
            "database backup automatic backup offline cache SQL Server SQLite MariaDB restore connection schema setup"),

        new("administration", "Administration", "Administration and permissions",
            "Understand users, access rights, settings and safe maintenance.",
            """
            ADMINISTRATION AND PERMISSIONS

Access
• Available screens and actions depend on the user's role and assigned menu permissions.
• Use the application user-management tools to review users and access. Grant only the permissions required for a person's work.
• Super-administrator actions may affect the whole installation or database; use them carefully.

Company settings
• Settings such as Ergani credentials, working-time rules, leave types, email and other operational options may be company-specific.
• Select the correct company before changing these settings.
• After a change, verify the saved value and test the related workflow.

Maintenance
• Database deletion, local-cache cleanup, migrations and restore operations are not routine troubleshooting steps.
• Before any destructive operation, verify a backup and confirm the target database/path.
• Never remove submission history or operational records merely to make a screen look clean.
• If setup reports an unavailable or incomplete database, collect the full error and verify the provider/connection configuration before attempting repair.
""",
            "administration admin permissions users settings database delete cache migration maintenance"),

        new("reports", "Monitoring & records", "Reports and exports",
            "Generate reports and safely handle exported files.",
            """
            REPORTS AND EXPORTS

1. Open Reports and choose the report type and filters available in your version.
2. Confirm company, employee, date range and any status filters before generating a report.
3. Review the results before exporting to Excel or another supported format.
4. When the application reports that a file was saved, use the displayed file link/action if available to open it.
5. Store exported files in an approved location; they may contain employee and business information.

If results look incomplete
• Verify the active company and date range.
• Check whether the report includes only submitted records, pending records or all matching records.
• Compare with the underlying screen and relevant submission log.
• Avoid treating an export as a substitute for the official Ergani response or protocol.
""",
            "report reports export Excel file saved employee date range filter"),

        new("status-icons", "Monitoring & records", "Statuses, icons and history",
            "Interpret pending, successful, failed and imported records.",
            """
            STATUSES, ICONS AND HISTORY

Use the status shown on the specific record and the related log. Icons provide a quick visual cue, but the text response and protocol details are more useful when diagnosing an issue.

Common concepts
• Pending / unsent: a record is saved but has not been confirmed as sent.
• Sent / successful: the submission received a successful response.
• Failed: the operation failed; read the returned error.
• Imported: a record came from an import/synchronization workflow.
• Generated from schedule: an overtime item may have been created from the schedule's working hours and may still need review and submission.

History and correction
• Submission history is valuable for auditing and troubleshooting and should be retained.
• A local edit does not automatically amend a previously submitted record in Ergani.
• If a successful submission contains incorrect data, use the appropriate corrective process and preserve the original protocol/response.
• Do not retry successful records just because an unrelated row failed.
""",
            "status icon pending sent successful failed imported history audit generated schedule"),

        new("troubleshooting", "Troubleshooting", "Common errors and safe recovery",
            "A checklist for connection, authentication, submission, PDF and mail problems.",
            """
            TROUBLESHOOTING CHECKLIST

Database unavailable / database setup required
1. Check whether the configured database server is reachable.
2. Verify the provider, connection string, database name and credentials.
3. Determine whether the error refers to the central database or offline/local cache.
4. Preserve existing data and logs; do not delete the database as a first response.

Invalid username or password
• Confirm whether you are logging into the application or authenticating to Ergani; these are separate credentials.
• For offline application login, the user may need to have been synchronized previously, depending on deployment mode.
• Check the exact error and account status before resetting anything.

Ergani submission failed
• Read the full response, identify the affected employee/date/category and correct the validation or connectivity issue.
• Confirm whether a protocol was returned.
• Retry only after the cause is understood and the record remains unsent/eligible.

PDF/document unavailable
• Verify that the submission has the expected protocol/date identifiers and that the relevant document action is being used.
• A missing PDF does not necessarily mean the original submission failed; check the API response and log.

Email not received
• Test SMTP, inspect the email log, check spam/quarantine and confirm connectivity.

General rule
Capture the full error message, time, company, affected record and relevant protocol number. Remove passwords and other secrets before sharing diagnostic information.
""",
            "error troubleshooting database unavailable invalid password PDF 404 email SMTP offline failed"),

        new("glossary", "Reference", "Glossary and terminology",
            "A quick reference for common English and Greek application terms.",
            """
            GLOSSARY

Company — Εταιρεία
Branch — Υποκατάστημα
Employee — Εργαζόμενος
Schedule / working time — Πρόγραμμα / ωράριο
Overtime — Υπερωρία
Leave / holiday — Άδεια
Work card — Κάρτα εργασίας
Submission — Υποβολή
Protocol number — Αριθμός πρωτοκόλλου
Pending / unsent — Σε εκκρεμότητα / μη απεσταλμένο
Submitted / sent — Υποβλήθηκε / απεστάλη
Failed — Απέτυχε
Imported — Έγινε εισαγωγή
Offline cache — Τοπική προσωρινή βάση εκτός σύνδεσης
Backup — Αντίγραφο ασφαλείας
SMTP — Πρωτόκολλο αποστολής email

Ergani user types
• 01 — External.
• 02 — Login with Ergani credentials.
• 03 — Login with credentials for EFKA construction/technical projects.

Terminology can differ slightly between screens. When a status affects whether an item can be sent again, rely on the specific screen's instructions and submission log.
""",
            "glossary terminology Greek English translation status user type EFKA"),

        new("users", "Administration", "Users and permissions",
            "Manage user accounts, roles and access to application screens.",
            """
            USERS AND PERMISSIONS

1. Open Users with an account that has permission to manage users.
2. Review the account, active status, role and permitted menu items before changing access.
3. Grant only the screens and actions the person needs.
4. If a menu item is missing, check the user's role and permissions, then confirm the active company and application mode.

Good practice
• Keep an appropriately protected administrator account available.
• Do not share user accounts; separate accounts make actions easier to audit.
• Do not remove the last administrator or change your own access until another authorized administrator can sign in.
• Application login credentials are different from credentials used to authenticate with Ergani.
""",
            "users user account permissions roles access menu login administrator"),

        new("today-board", "Daily operations", "Today board and daily status",
            "Compare the daily schedule with available attendance information.",
            """
            TODAY BOARD

1. Open Today and confirm the active company and date.
2. Review each employee's schedule and the attendance/work-card information available.
3. If a person or event is missing, check the date, employee's branch, active status and work-card history.
4. Open the underlying schedule or work-card record to investigate discrepancies.

The daily board is an operational overview. A missing event can be caused by data availability, synchronization, filters or a blocked scan; it is not automatically proof that no event occurred.

For a disputed clock-in or clock-out, review the original event and its log and preserve the details for follow-up.
""",
            "today daily board status attendance schedule clock in clock out employee"),

        new("work-card-scan", "Daily operations", "Work-card scanning and terminal",
            "Review scans, blocked events, manual entries and connectivity limitations.",
            """
            WORK-CARD SCANNING AND TERMINAL

Normal scan
1. Confirm the terminal is connected and configured for the correct company/branch.
2. Scan the employee identifier using the configured device or terminal workflow.
3. Read the result and check whether the scan was accepted or blocked.
4. If rejected, note the displayed reason and inspect the corresponding scan log.

Blocked scans
• Company rules can block clock-in, for example when a schedule is required and none is on file.
• Verify the saved company setting before testing a rule change.
• A blocked scan is not a successful attendance event.

Connectivity
• A lost connection can affect whether an event is sent immediately or queued. Check the status and log; do not assume every entry type is queued.
• Manual/late card workflows can behave differently offline from ordinary scans. Follow the warning shown in the form.
• After connectivity returns, verify whether queued events synchronized and whether failed items need attention.

When reporting a scan problem, keep the employee, timestamp, branch, device and result. Do not use a duplicate scan as the first troubleshooting step.
""",
            "scan terminal device work card blocked scan clock in offline queue branch manual"),

        new("database-options", "Administration", "Database options and connection setup",
            "Understand the configured provider and connection settings before maintenance.",
            """
            DATABASE OPTIONS AND CONNECTION SETUP

Change database settings only when you understand how this installation connects to its central database and local/offline data.

Before changing settings
1. Identify the configured provider and target database.
2. Confirm server/address, database name and connection details with the system administrator.
3. Determine whether the application uses a central database, local SQLite database or offline cache.
4. Verify a backup using a method supported by that provider before maintenance or migration.

If the database is unavailable
• Read the complete message and log details.
• Check network access, server availability, database name, credentials and provider-specific configuration.
• Use an available connection/setup test to diagnose the issue before changing or recreating data.
• Do not delete the database or cache as a general fix. A cache may contain data needed for offline work.

SQL Server, MariaDB and SQLite have different backup and maintenance requirements. Follow the instructions for the provider configured in your installation.
""",
            "database options setup connection provider SQL Server MariaDB SQLite central local cache"),

        new("imports-sync", "Company & configuration", "Imports and synchronization",
            "Import employee/branch data and understand offline-cache synchronization.",
            """
            IMPORTS AND SYNCHRONIZATION

Before importing
1. Confirm the active company and the source of the data.
2. Check the file format, required columns and employee/branch identifiers expected by the import screen.
3. Review the preview or validation results, when available, before confirming the import.
4. Investigate skipped or rejected rows using the displayed message or import log instead of repeatedly importing the same file.

After importing
• Verify the employee/branch counts and inspect a few records in the destination screen.
• Avoid creating duplicates to compensate for missing records; check identifiers, company and branch associations first.
• An import changes the application's records but does not necessarily submit anything to Ergani.

Synchronization and offline cache
• The application may synchronize selected company data between the central database and an offline cache.
• Use the available refresh/synchronization action when the connection is restored, then inspect the reported result and error log.
• A successful cache refresh is not proof that pending schedule, overtime, leave or work-card submissions were accepted by Ergani.
• Do not delete the cache or central database as a way to force synchronization. Preserve the existing records and diagnose the reported error first.
""",
            "import imports synchronization sync employee branch file rows skipped cache offline refresh"),

        new("release-notes", "Reference", "Help guide and version notes",
            "How this guide is maintained and how to report a missing topic.",
            """
            ABOUT THIS HELP GUIDE

This guide is built into the application and is available without an internet connection.

• The guide follows the application's selected language (English or Greek).
• Use search to find a feature, error message or alternate term.
• If the Edit topic action is available to your account, you can save a language-specific override for that topic.
• Restoring a topic removes its override and returns to the built-in text.
• Older saved Help text, if present, is shown separately and is not silently overwritten.

This is an operational guide, not a replacement for official Ergani documentation or legal advice. The exact controls available can vary by role, configuration and application version.

When reporting a missing instruction, include the screen name, selected language, application version if known, and the steps that led to the issue. Do not include passwords or secrets.
""",
            "help guide version notes edit restore language offline support")];
    private static System.Collections.Generic.List<HelpTopic> BuildGreekTopics() =>
    [
        new("getting-started", "Ξεκινώντας", "Πρώτα βήματα",
            "Γνωρίστε το περιβάλλον, την επιλογή εταιρείας και την πλοήγηση.",
            """
            ERGANI MANAGER — ΠΡΩΤΑ ΒΗΜΑΤΑ

1. Συνδεθείτε με τον λογαριασμό της εφαρμογής. Οι διαθέσιμες οθόνες εξαρτώνται από τα δικαιώματά σας.
2. Αν διαχειρίζεστε περισσότερες από μία εταιρείες, επιλέξτε τη σωστή εταιρεία από τον επιλογέα στο επάνω μέρος πριν επεξεργαστείτε ή υποβάλετε εγγραφές.
3. Χρησιμοποιήστε το αριστερό μενού για να ανοίξετε Πίνακα ελέγχου, Εταιρείες, Υποκαταστήματα, Εργαζομένους, Προγράμματα, Κάρτες εργασίας, Υπερωρίες, Αναφορές, αρχεία καταγραφής και Βοήθεια.
4. Η επιλεγμένη εταιρεία καθορίζει ποιες εγγραφές και ρυθμίσεις εταιρείας εμφανίζονται. Ελέγξτε την εταιρεία πριν στείλετε δεδομένα στο ΕΡΓΑΝΗ.
5. Χρησιμοποιήστε την Ανανέωση ή την αντίστοιχη ενέργεια της οθόνης για να ανακτήσετε τα πιο πρόσφατα διαθέσιμα δεδομένα.

Σημαντικό
• Τα δεδομένα της εφαρμογής και τα δεδομένα του ΕΡΓΑΝΗ δεν είναι το ίδιο. Η αποθήκευση μιας εγγραφής δεν αποδεικνύει ότι έγινε αποδεκτή από το ΕΡΓΑΝΗ.
• Μετά την αποστολή, ελέγξτε το αποτέλεσμα και τον αριθμό πρωτοκόλλου.
• Μην κοινοποιείτε κωδικούς πρόσβασης ή εξαγόμενα αρχεία σε άτομα που δεν χρειάζονται πρόσβαση.
""",
            "αρχή σύνδεση πλοήγηση εταιρεία επιλογή γλώσσα δικαιώματα"),

        new("companies", "Εταιρεία και ρυθμίσεις", "Εταιρείες και κωδικοί ΕΡΓΑΝΗ",
            "Στοιχεία εταιρείας, τύπος χρήστη, περιβάλλον και έλεγχος σύνδεσης.",
            """
            ΕΤΑΙΡΕΙΕΣ ΚΑΙ ΚΩΔΙΚΟΙ ΕΡΓΑΝΗ

1. Ανοίξτε τις Εταιρείες και επιλέξτε ή προσθέστε την εταιρεία που θέλετε να ρυθμίσετε.
2. Συμπληρώστε τα στοιχεία και τα διαπιστευτήρια που απαιτούνται για τον λογαριασμό ΕΡΓΑΝΗ της συγκεκριμένης εταιρείας.
3. Επιλέξτε τον σωστό τύπο χρήστη:
   • 01 — Εξωτερικός.
   • 02 — Σύνδεση με κωδικούς ΕΡΓΑΝΗ.
   • 03 — Σύνδεση με κωδικούς για Οικοδομοτεχνικά Έργα ΕΦΚΑ.
4. Ελέγξτε τη βασική διεύθυνση API του ΕΡΓΑΝΗ. Χρησιμοποιήστε το δοκιμαστικό περιβάλλον μόνο για δοκιμές και το παραγωγικό μόνο όταν πρόκειται να υποβάλετε πραγματικά δεδομένα.
5. Χρησιμοποιήστε τον έλεγχο σύνδεσης, όπου διατίθεται, πριν από εισαγωγή ή υποβολή δεδομένων.
6. Πριν από κάθε εργασία, επιβεβαιώστε την ενεργή εταιρεία στον επιλογέα.

Ασφάλεια
• Τα διαπιστευτήρια είναι ευαίσθητα. Μην τοποθετείτε κωδικούς σε στιγμιότυπα οθόνης, άρθρα βοήθειας ή αρχεία καταγραφής.
• Ένας επιτυχής έλεγχος επιβεβαιώνει τη σύνδεση/ταυτοποίηση εκείνη τη στιγμή· δεν εγγυάται την επιτυχία κάθε μελλοντικής υποβολής.
""",
            "εταιρεία εταιρείες κωδικοί username password τύπος χρήστη 01 02 03 εξωτερικός δοκιμαστικό παραγωγικό API σύνδεση"),

        new("branches-employees", "Εταιρεία και ρυθμίσεις", "Υποκαταστήματα και εργαζόμενοι",
            "Διατηρήστε τη δομή της εταιρείας και τα στοιχεία εργαζομένων.",
            """
            ΥΠΟΚΑΤΑΣΤΗΜΑΤΑ ΚΑΙ ΕΡΓΑΖΟΜΕΝΟΙ

Υποκαταστήματα
1. Ανοίξτε τα Υποκαταστήματα για την επιλεγμένη εταιρεία.
2. Εισαγάγετε ή διατηρήστε τις εγγραφές υποκαταστημάτων που χρησιμοποιεί η εφαρμογή.
3. Ελέγξτε τη σύνδεση με το υποκατάστημα όταν λείπει εργαζόμενος ή υποβολή από κάποια λίστα.

Εργαζόμενοι
1. Ανοίξτε τους Εργαζομένους και επιλέξτε τη σωστή εταιρεία.
2. Εισαγάγετε ή επεξεργαστείτε τα στοιχεία και επιβεβαιώστε το υποκατάστημα.
3. Ελέγξτε την ενεργή/ανενεργή κατάσταση και το αναγνωριστικό πριν δημιουργήσετε πρόγραμμα, υπερωρία, άδεια ή εγγραφή κάρτας εργασίας.
4. Αν λείπει εργαζόμενος, ανανεώστε ή εισαγάγετε τα πιο πρόσφατα διαθέσιμα δεδομένα και ελέγξτε την επιλεγμένη εταιρεία.

Ποιότητα δεδομένων
• Μην δημιουργείτε διπλές εγγραφές εργαζομένων για να παρακάμψετε μια ελλιπή λίστα.
• Τα στοιχεία πρέπει να συμφωνούν με την αντίστοιχη εγγραφή στο ΕΡΓΑΝΗ.
• Μια αλλαγή στην εφαρμογή δεν ενημερώνει απαραίτητα την πύλη ΕΡΓΑΝΗ χωρίς την αντίστοιχη υποστηριζόμενη υποβολή.
""",
            "υποκατάστημα υποκαταστήματα εργαζόμενος εργαζόμενοι εισαγωγή ενεργός ανενεργός αναγνωριστικό"),

        new("dashboard", "Παρακολούθηση και εγγραφές", "Πίνακας ελέγχου",
            "Σύνοψη για την επιλεγμένη εταιρεία και πρόσφατες υποβολές.",
            """
            ΠΙΝΑΚΑΣ ΕΛΕΓΧΟΥ

Ο πίνακας ελέγχου συνοψίζει την επιλεγμένη εταιρεία και δεν συνδυάζει άσχετες εταιρείες.

Ανάλογα με τα διαθέσιμα δεδομένα, εμφανίζει:
• Ενεργά υποκαταστήματα και εργαζομένους.
• Δραστηριότητα υποβολών για τον τρέχοντα μήνα.
• Αποτυχημένες υποβολές στο εμφανιζόμενο χρονικό διάστημα.
• Εκκρεμή προγράμματα, υπερωρίες και άδειες.
• Ανάλυση αποτελεσμάτων ανά κατηγορία.
• Πρόσφατες υποβολές και κατάσταση αυτόματων αντιγράφων ασφαλείας.

Χρήση
1. Επιβεβαιώστε την επιλεγμένη εταιρεία στην κεφαλίδα.
2. Πατήστε Ανανέωση για ενημέρωση της σύνοψης.
3. Ανοίξτε την αντίστοιχη οθόνη ή το αρχείο υποβολών για να διερευνήσετε μια ένδειξη ή ένα σφάλμα.
4. Χρησιμοποιήστε τον πίνακα ως σύνοψη· για τη διόρθωση μεμονωμένης εγγραφής, ελέγξτε την εγγραφή και την απάντηση υποβολής.

Ένας συνολικός αριθμός στον πίνακα δεν αποτελεί από μόνος του επιβεβαίωση αποδοχής από το ΕΡΓΑΝΗ.
""",
            "πίνακας ελέγχου μετρήσεις εκκρεμή αποτυχημένα επιτυχημένα ανανέωση εταιρεία"),

        new("schedules", "Καθημερινές εργασίες", "Προγράμματα και μαζικές αλλαγές",
            "Επεξεργασία ημερών, μαζικές αλλαγές, αντιγραφή και υποβολή.",
            """
            ΠΡΟΓΡΑΜΜΑΤΑ

Προβολή ημερολογίου
1. Ανοίξτε τα Προγράμματα και επιλέξτε εργαζόμενο και μήνα.
2. Ελέγξτε την αντιστοίχιση ημέρας εβδομάδας/ημερομηνίας και τον επιλεγμένο εργαζόμενο.
3. Επιλέξτε ημέρα για λεπτομέρειες ή χρησιμοποιήστε τη μαζική ενέργεια όταν η ίδια αλλαγή αφορά πολλές επιλεγμένες ημέρες.
4. Στη διαδικασία αντιγραφής/κλωνοποίησης, ελέγξτε εργαζόμενο-πηγή, εργαζομένους-προορισμού και εύρος ημερομηνιών πριν επιβεβαιώσετε.

Ώρες και υπερωρίες
• Η εφαρμογή μπορεί να δημιουργεί εγγραφές υπερωρίας από ημέρες που υπερβαίνουν τον κανόνα κανονικών ωρών. Ελέγξτε τις παραγόμενες υπερωρίες στην οθόνη Υπερωρίες πριν από την υποβολή.
• Ελέγξτε ώρες έναρξης/λήξης, διαλείμματα και τις ρυθμίσεις ωραρίου της εταιρείας.
• Η αλλαγή προγράμματος δεν αναιρεί ούτε τροποποιεί από μόνη της εγγραφή που έχει ήδη σταλεί στο ΕΡΓΑΝΗ.

Υποβολή
1. Ελέγξτε τις εκκρεμείς αλλαγές και τις επιλεγμένες ημερομηνίες.
2. Χρησιμοποιήστε την αντίστοιχη ενέργεια υποβολής προγράμματος.
3. Διαβάστε την απάντηση και τον αριθμό πρωτοκόλλου και ελέγξτε το αρχείο υποβολών.
4. Αν αποτύχει, διατηρήστε την αποθηκευμένη εγγραφή και διερευνήστε το σφάλμα πριν επαναλάβετε.

Η συμπεριφορά της επιλογής εξαρτάται από την ενεργή οθόνη και την έκδοση. Πριν στείλετε, επιβεβαιώστε τον αριθμό εκκρεμών στοιχείων και την επιβεβαίωση που εμφανίζει η εφαρμογή.
""",
            "πρόγραμμα ημερολόγιο ημέρα μαζική εφαρμογή αντιγραφή ώρες υπερωρία εκκρεμή υποβολή πρωτόκολλο"),

        new("overtime", "Καθημερινές εργασίες", "Υπερωρίες",
            "Δημιουργία, έλεγχος και υποβολή υπερωριών για έναν ή περισσότερους εργαζομένους.",
            """
            ΥΠΕΡΩΡΙΕΣ

Δημιουργία και έλεγχος
1. Ανοίξτε τις Υπερωρίες και επιλέξτε τη σωστή εταιρεία.
2. Δημιουργήστε εγγραφή και επιλέξτε εργαζόμενο ή εργαζομένους, ημερομηνία, χρονικό διάστημα και απαιτούμενη αιτιολογία.
3. Ελέγξτε τόσο τις χειροκίνητες υπερωρίες όσο και όσες δημιουργήθηκαν από το πρόγραμμα.
4. Επιβεβαιώστε ημερομηνία, ώρες και καταλληλότητα υποβολής.

Υποβολή
1. Επιλέξτε μόνο τις κατάλληλες μη απεσταλμένες εγγραφές που θέλετε να υποβάλετε.
2. Ξεκινήστε την υποβολή και περιμένετε το αποτέλεσμα.
3. Ελέγξτε τη σύνοψη επιτυχίας/αποτυχίας και τυχόν αριθμό πρωτοκόλλου.
4. Χρησιμοποιήστε το Ιστορικό Υπερωριών ή το αρχείο υποβολών API, όπου διατίθεται, για τη διερεύνηση αποτυχιών.

Σημαντικό
• Μια αποθηκευμένη ή εκκρεμής υπερωρία δεν σημαίνει ότι υποβλήθηκε επιτυχώς στο ΕΡΓΑΝΗ.
• Η επιτυχώς υποβληθείσα εγγραφή θεωρείται απεσταλμένη· μην την υποβάλετε ξανά εκτός αν η εφαρμογή την εμφανίζει ρητά ως επιλέξιμη.
• Αν αλλαγή προγράμματος επηρεάζει υπερωρία που έχει ήδη σταλεί, διορθώστε τη διαφορά με την κατάλληλη διαδικασία στο ΕΡΓΑΝΗ. Η τοπική αλλαγή δεν ανακαλεί την υποβολή.
• Ελέγξτε την αιτιολογία και τους κανόνες ωραρίου της εταιρείας πριν από την αποστολή.
""",
            "υπερωρία υπερωρίες επιπλέον ώρες πρόγραμμα αιτιολογία πολλοί εργαζόμενοι εκκρεμής υποβολή αποτυχία"),

        new("leave-holidays", "Καθημερινές εργασίες", "Άδειες και αργίες",
            "Καταχώριση αδειών, έλεγχος δικαιώματος και υποβολή υποστηριζόμενων τύπων.",
            """
            ΑΔΕΙΕΣ ΚΑΙ ΑΡΓΙΕΣ

Πριν από την αποθήκευση
1. Ανοίξτε τη διαδικασία αδειών/αργιών από την αντίστοιχη οθόνη.
2. Επιλέξτε εργαζόμενο, τύπο άδειας και ημερομηνία ή εύρος ημερομηνιών.
3. Συμπληρώστε το απαιτούμενο έτος αναφοράς και τις ημέρες δικαιούμενης άδειας, όπου ζητούνται.
4. Ελέγξτε ότι ο τύπος άδειας αντιστοιχεί στη σωστή απουσία.

Έλεγχος και υποβολή
1. Ελέγξτε τις εκκρεμείς εγγραφές και την κατάστασή τους.
2. Υποβάλετε μόνο τις εγγραφές που είναι κατάλληλες και πρέπει να σταλούν.
3. Ελέγξτε το αποτέλεσμα, τον αριθμό πρωτοκόλλου και το ιστορικό υποβολών.
4. Αν αποτύχει η υποβολή, διορθώστε τα ελλιπή ή λανθασμένα στοιχεία πριν από νέα προσπάθεια.

Σημαντικό
• Οι τύποι αδειών και οι κατηγορίες πληρωμής μπορεί να ρυθμίζονται από διαχειριστή. Οι κωδικοί πρέπει να συμφωνούν με τους ισχύοντες ορισμούς ΕΡΓΑΝΗ.
• Η αποθήκευση άδειας στην εφαρμογή δεν αποδεικνύει αποδοχή από το ΕΡΓΑΝΗ.
• Εγγραφές που έχουν ήδη υποβληθεί μπορεί να απαιτούν διορθωτική διαδικασία· η διαγραφή ή αλλαγή τοπικής εγγραφής δεν αναιρεί την υποβολή στην πύλη.
• Διατηρείτε τα στοιχεία δικαιώματος άδειας και το ιστορικό για έλεγχο/αναφορά.
""",
            "άδεια άδειες αργίες ετήσια άδεια δικαίωμα έτος αναφοράς ημέρες αποτυχία επεξεργασία υποβολή"),

        new("work-cards", "Καθημερινές εργασίες", "Κάρτες εργασίας και σαρώσεις",
            "Ιστορικό καρτών, αποτελέσματα σαρώσεων, χειροκίνητες εγγραφές και αιτιολογίες καθυστέρησης.",
            """
            ΚΑΡΤΕΣ ΕΡΓΑΣΙΑΣ

Ιστορικό
1. Ανοίξτε τις Κάρτες εργασίας / Ιστορικό καρτών και επιλέξτε εργαζόμενο και χρονικό διάστημα.
2. Ελέγξτε τις καταγεγραμμένες κινήσεις και την κατάστασή τους.
3. Αν λείπει μια κίνηση, ελέγξτε το εύρος ημερομηνιών, τον εργαζόμενο, τη σύνδεση και τις διαθέσιμες ενέργειες εισαγωγής/συγχρονισμού.

Αποκλεισμένες σαρώσεις
• Μια σάρωση μπορεί να αποκλειστεί από κανόνες της εταιρείας, όπως η απαίτηση να υπάρχει πρόγραμμα. Ελέγξτε τις ρυθμίσεις και τον εμφανιζόμενο λόγο αποκλεισμού.
• Οι αποκλεισμένες σαρώσεις πρέπει να ελέγχονται στο σχετικό αρχείο καταγραφής· δεν ισοδυναμούν με επιτυχή είσοδο.
• Αν ο κανόνας είναι απενεργοποιημένος, επαληθεύστε ότι αποθηκεύτηκε η ρύθμιση της εταιρείας και δοκιμάστε ξανά με γνωστό εργαζόμενο.

Χειροκίνητες ή καθυστερημένες κάρτες
• Καταχωρίστε σωστή ημερομηνία και ώρα και δώστε αιτιολογία καθυστέρησης όταν απαιτείται από το χρονικό όριο.
• Διαβάστε την προειδοποίηση της φόρμας σχετικά με τη λειτουργία εκτός σύνδεσης.
• Ορισμένες χειροκίνητες υποβολές απαιτούν άμεση σύνδεση και ενδέχεται να μην μπαίνουν σε ουρά για μεταγενέστερη αποστολή.

Μετά από σφάλμα, διατηρήστε την εγγραφή και τις λεπτομέρειες απάντησης. Μην επαναλαμβάνετε σαρώσεις ή υποβολές χωρίς να ελέγξετε αν το αρχικό συμβάν έγινε αποδεκτό.
""",
            "κάρτα εργασίας κάρτες σάρωση είσοδος αποκλεισμός καθυστέρηση αιτιολογία χειροκίνητη ώρα ιστορικό"),

        new("api-submissions", "Παρακολούθηση και εγγραφές", "Υποβολές ΕΡΓΑΝΗ και αρχείο API",
            "Εύρεση αποτελεσμάτων, πρωτοκόλλων, σφαλμάτων και εγγράφων.",
            """
            ΥΠΟΒΟΛΕΣ ΕΡΓΑΝΗ ΚΑΙ ΑΡΧΕΙΟ API

Χρησιμοποιήστε το αρχείο υποβολών API για να εντοπίσετε τι προσπάθησε να στείλει η εφαρμογή και ποια απάντηση έλαβε.

1. Ανοίξτε το Αρχείο Υποβολών / API.
2. Εντοπίστε την εγγραφή με βάση εταιρεία, εργαζόμενο, ημερομηνία, κατηγορία ή κατάσταση, όπου διατίθενται φίλτρα.
3. Διαβάστε ολόκληρη την απάντηση/το σφάλμα. Ελέγξτε αν αφορά σύνδεση, ταυτοποίηση, ελλιπή δεδομένα, επικύρωση ή υπηρεσία ΕΡΓΑΝΗ.
4. Όταν επιστρέφεται αριθμός πρωτοκόλλου, διατηρήστε τον για αναφορά.
5. Ανοίξτε ή αποθηκεύστε το σχετικό PDF/έγγραφο, όταν υπάρχει αυτή η ενέργεια.
6. Μετά τη διόρθωση, επαναλάβετε μόνο εγγραφές που εξακολουθούν να είναι επιλέξιμες. Αν είναι ενεργή η αυτόματη επανάληψη, στοιχεία σε ουρά/αποτυχία μπορεί να επαναληφθούν όταν αποκατασταθεί η σύνδεση· επιβεβαιώστε το πραγματικό αποτέλεσμα στο αρχείο καταγραφής.

Ερμηνεία καταστάσεων
• Εκκρεμής / μη απεσταλμένη: αποθηκεύτηκε τοπικά, χωρίς επιβεβαίωση αποδοχής από το ΕΡΓΑΝΗ.
• Επιτυχής / υποβλήθηκε: η εφαρμογή έλαβε επιτυχή απάντηση για τη συγκεκριμένη ενέργεια.
• Απέτυχε: η ενέργεια δεν ολοκληρώθηκε επιτυχώς· διαβάστε την απάντηση πριν από επανάληψη.
• Εισήχθη: τα δεδομένα εισήχθησαν στην εφαρμογή· αυτό δεν σημαίνει από μόνο του ότι στάλθηκε νέα υποβολή.

Οι ακριβείς ονομασίες κατάστασης εξαρτώνται από τη διαδικασία. Για τη διερεύνηση, βασιστείτε στην απάντηση και στα στοιχεία πρωτοκόλλου.
""",
            "API υποβολή αρχείο πρωτόκολλο PDF έγγραφο απάντηση σφάλμα αποτυχία εκκρεμής υποβλήθηκε εισαγωγή"),

        new("email", "Παρακολούθηση και εγγραφές", "Ρυθμίσεις email και ειδοποιήσεις",
            "Ρύθμιση email εταιρείας, δοκιμή SMTP και διερεύνηση αποτυχιών.",
            """
            ΡΥΘΜΙΣΕΙΣ EMAIL ΚΑΙ ΕΙΔΟΠΟΙΗΣΕΙΣ

Ρύθμιση
1. Ανοίξτε τις ρυθμίσεις εταιρείας που περιλαμβάνουν email/SMTP.
2. Συμπληρώστε διακομιστή, θύρα, ασφάλεια, αποστολέα και στοιχεία ταυτοποίησης που απαιτεί ο πάροχος email.
3. Ελέγξτε τους παραλήπτες των ειδοποιήσεων.
4. Χρησιμοποιήστε τη δοκιμαστική αποστολή, όταν διατίθεται, και ελέγξτε το αποτέλεσμα στην εφαρμογή και το γραμματοκιβώτιο.

Αν δεν φτάνει το email
• Μια επιτυχής δοκιμή SMTP συνήθως σημαίνει ότι ο διακομιστής αποδέχτηκε το δοκιμαστικό μήνυμα· δεν εγγυάται παράδοση στα εισερχόμενα.
• Ελέγξτε διακομιστή/θύρα, TLS ή SSL, όνομα χρήστη/κωδικό, δικαιώματα αποστολέα, διεύθυνση παραλήπτη και φάκελο ανεπιθύμητων.
• Ελέγξτε το αρχείο email για ώρα και αναφερόμενο σφάλμα.
• Χωρίς σύνδεση στο διαδίκτυο, η εφαρμογή δεν μπορεί να παραδώσει μήνυμα εκείνη τη στιγμή. Η επανάληψη ή η ουρά εξαρτώνται από την υλοποίηση· ελέγξτε το αρχείο αντί να θεωρείτε ότι στάλθηκε.
• Μετά την αποκατάσταση σύνδεσης, επαληθεύστε αν έγινε αυτόματη επανάληψη ή αν απαιτείται χειροκίνητη ενέργεια.

Διατηρείτε τα στοιχεία email ιδιωτικά και μην τα κοινοποιείτε σε αιτήματα υποστήριξης.
""",
            "email SMTP αλληλογραφία δοκιμή μήνυμα ειδοποίηση εκτός σύνδεσης διαδίκτυο αποτυχία παράδοση επανάληψη κωδικός"),

        new("backup", "Διαχείριση", "Βάση δεδομένων, λειτουργία εκτός σύνδεσης και αντίγραφα",
            "Διαθεσιμότητα βάσης, τοπική cache και συμπεριφορά αυτόματων αντιγράφων.",
            """
            ΒΑΣΗ ΔΕΔΟΜΕΝΩΝ, OFFLINE ΚΑΙ ΑΝΤΙΓΡΑΦΑ ΑΣΦΑΛΕΙΑΣ

Σύνδεση βάσης
• Ανάλογα με τη ρύθμιση εγκατάστασης, η εφαρμογή μπορεί να χρησιμοποιεί κεντρική βάση και τοπική cache εκτός σύνδεσης.
• Αν η βάση δεν είναι διαθέσιμη, διαβάστε το ακριβές μήνυμα. Ελέγξτε διακομιστή, δίκτυο/VPN, όνομα βάσης, διαπιστευτήρια και πάροχο πριν αλλάξετε κάτι.
• Η τοπική cache δεν αποτελεί απαραίτητα πλήρες υποκατάστατο της κεντρικής βάσης. Μετά την επανασύνδεση, ελέγξτε τον συγχρονισμό.

Αυτόματα αντίγραφα ασφαλείας
• Ο προγραμματισμός αυτόματων αντιγράφων εκτελείται μέσα στην εφαρμογή και συνεπώς μόνο όσο αυτή παραμένει ανοιχτή.
• Η διαδικασία και οι υποστηριζόμενοι πάροχοι εξαρτώνται από τον τύπο βάσης. Η αντιγραφή αρχείου SQLite δεν είναι κατάλληλη μέθοδος για SQL Server ή MariaDB.
• Ελέγξτε τις ρυθμίσεις αντιγράφων, τον φάκελο προορισμού, τη διατήρηση και την τελευταία επιτυχή εκτέλεση.
• Επιβεβαιώστε ότι το αναμενόμενο αρχείο υπάρχει και μπορεί να χρησιμοποιηθεί.

Ασφαλής ανάκτηση
1. Πριν από συντήρηση, δημιουργήστε επαληθευμένο αντίγραφο με τη μέθοδο που υποστηρίζεται από τον συγκεκριμένο πάροχο.
2. Επιβεβαιώστε ποια είναι η κεντρική βάση και ποια η τοπική cache.
3. Μη διαγράφετε ή δημιουργείτε ξανά βάση απλώς για να εξαφανιστεί ένα σφάλμα. Διερευνήστε πρώτα τη σύνδεση και το σχήμα.
4. Διατηρήστε αρχεία καταγραφής και υπάρχουσες εγγραφές.
5. Για επαναφορά ή μετάβαση, ακολουθήστε τη διαδικασία της εγκατάστασης και ζητήστε βοήθεια διαχειριστή βάσης αν δεν είστε βέβαιοι.
""",
            "βάση δεδομένων αντίγραφο ασφαλείας αυτόματο offline cache SQL Server SQLite MariaDB επαναφορά σύνδεση σχήμα"),

        new("administration", "Διαχείριση", "Διαχείριση και δικαιώματα",
            "Χρήστες, δικαιώματα πρόσβασης, ρυθμίσεις και ασφαλής συντήρηση.",
            """
            ΔΙΑΧΕΙΡΙΣΗ ΚΑΙ ΔΙΚΑΙΩΜΑΤΑ

Πρόσβαση
• Οι διαθέσιμες οθόνες και ενέργειες εξαρτώνται από τον ρόλο και τα δικαιώματα μενού του χρήστη.
• Χρησιμοποιήστε τη διαχείριση χρηστών για να ελέγξετε λογαριασμούς και πρόσβαση. Δώστε μόνο τα απαραίτητα δικαιώματα.
• Οι ενέργειες υπερδιαχειριστή μπορεί να επηρεάζουν ολόκληρη την εγκατάσταση ή τη βάση· χρησιμοποιήστε τες προσεκτικά.

Ρυθμίσεις εταιρείας
• Κωδικοί ΕΡΓΑΝΗ, κανόνες ωραρίου, τύποι αδειών, email και άλλες λειτουργικές ρυθμίσεις μπορεί να είναι ανά εταιρεία.
• Επιλέξτε τη σωστή εταιρεία πριν αλλάξετε ρυθμίσεις.
• Μετά την αλλαγή, ελέγξτε την αποθηκευμένη τιμή και δοκιμάστε τη σχετική διαδικασία.

Συντήρηση
• Διαγραφή βάσης, καθαρισμός τοπικής cache, μεταφορά σχήματος και επαναφορά δεν είναι συνήθη βήματα αντιμετώπισης προβλημάτων.
• Πριν από καταστροφική ενέργεια, επαληθεύστε αντίγραφο ασφαλείας και προορισμό.
• Μην αφαιρείτε ιστορικό υποβολών ή λειτουργικές εγγραφές απλώς για να καθαρίσει μια οθόνη.
• Αν η εγκατάσταση αναφέρει μη διαθέσιμη ή ελλιπή βάση, καταγράψτε το πλήρες σφάλμα και ελέγξτε σύνδεση/πάροχο πριν από επισκευή.
""",
            "διαχείριση admin δικαιώματα χρήστες ρυθμίσεις βάση διαγραφή cache μετάβαση συντήρηση"),

        new("reports", "Παρακολούθηση και εγγραφές", "Αναφορές και εξαγωγές",
            "Δημιουργία αναφορών και ασφαλής διαχείριση εξαγόμενων αρχείων.",
            """
            ΑΝΑΦΟΡΕΣ ΚΑΙ ΕΞΑΓΩΓΕΣ

1. Ανοίξτε τις Αναφορές και επιλέξτε τύπο και φίλτρα που διατίθενται στην έκδοσή σας.
2. Επιβεβαιώστε εταιρεία, εργαζόμενο, εύρος ημερομηνιών και φίλτρα κατάστασης πριν δημιουργήσετε αναφορά.
3. Ελέγξτε τα αποτελέσματα πριν από εξαγωγή σε Excel ή άλλη υποστηριζόμενη μορφή.
4. Όταν η εφαρμογή αναφέρει ότι αποθηκεύτηκε αρχείο, χρησιμοποιήστε τον εμφανιζόμενο σύνδεσμο/ενέργεια για να το ανοίξετε, αν διατίθεται.
5. Αποθηκεύστε τα εξαγόμενα αρχεία σε εγκεκριμένη τοποθεσία· μπορεί να περιέχουν προσωπικά στοιχεία εργαζομένων ή εταιρικά δεδομένα.

Αν τα αποτελέσματα είναι ελλιπή
• Ελέγξτε την ενεργή εταιρεία και τις ημερομηνίες.
• Ελέγξτε αν η αναφορά περιλαμβάνει μόνο υποβληθείσες εγγραφές, εκκρεμείς ή όλες τις αντίστοιχες εγγραφές.
• Συγκρίνετε με την αρχική οθόνη και το σχετικό αρχείο υποβολών.
• Η εξαγωγή δεν αντικαθιστά την επίσημη απάντηση ή το πρωτόκολλο του ΕΡΓΑΝΗ.
""",
            "αναφορά αναφορές εξαγωγή Excel αρχείο εργαζόμενος ημερομηνίες φίλτρο"),

        new("status-icons", "Παρακολούθηση και εγγραφές", "Καταστάσεις, εικονίδια και ιστορικό",
            "Ερμηνεία εκκρεμών, επιτυχών, αποτυχημένων και εισαχθέντων εγγραφών.",
            """
            ΚΑΤΑΣΤΑΣΕΙΣ, ΕΙΚΟΝΙΔΙΑ ΚΑΙ ΙΣΤΟΡΙΚΟ

Ελέγχετε την κατάσταση της συγκεκριμένης εγγραφής και το σχετικό αρχείο καταγραφής. Τα εικονίδια είναι γρήγορη ένδειξη, αλλά η απάντηση και το πρωτόκολλο βοηθούν περισσότερο στη διερεύνηση.

Συνήθεις έννοιες
• Εκκρεμής / μη απεσταλμένη: η εγγραφή αποθηκεύτηκε, αλλά δεν έχει επιβεβαιωθεί αποστολή.
• Απεστάλη / επιτυχής: η υποβολή έλαβε επιτυχή απάντηση.
• Απέτυχε: η ενέργεια απέτυχε· διαβάστε το σφάλμα.
• Εισήχθη: η εγγραφή προήλθε από εισαγωγή/συγχρονισμό.
• Δημιουργήθηκε από πρόγραμμα: μια υπερωρία μπορεί να δημιουργήθηκε από τις ώρες προγράμματος και να χρειάζεται έλεγχο και υποβολή.

Ιστορικό και διόρθωση
• Το ιστορικό υποβολών είναι χρήσιμο για έλεγχο και διερεύνηση και πρέπει να διατηρείται.
• Η τοπική αλλαγή δεν τροποποιεί αυτόματα εγγραφή που έχει ήδη υποβληθεί στο ΕΡΓΑΝΗ.
• Αν επιτυχής υποβολή περιέχει λάθος, χρησιμοποιήστε την κατάλληλη διορθωτική διαδικασία και διατηρήστε το αρχικό πρωτόκολλο/την απάντηση.
• Μην επαναλαμβάνετε επιτυχείς εγγραφές επειδή απέτυχε κάποια άλλη.
""",
            "κατάσταση εικονίδιο εκκρεμής απεστάλη επιτυχής αποτυχία εισαγωγή ιστορικό έλεγχος"),

        new("troubleshooting", "Αντιμετώπιση προβλημάτων", "Συνήθη σφάλματα και ασφαλής ανάκτηση",
            "Έλεγχοι για σύνδεση, ταυτοποίηση, υποβολές, PDF και email.",
            """
            ΛΙΣΤΑ ΕΛΕΓΧΟΥ ΠΡΟΒΛΗΜΑΤΩΝ

Μη διαθέσιμη βάση / απαιτείται εγκατάσταση βάσης
1. Ελέγξτε αν είναι προσβάσιμος ο διακομιστής της βάσης.
2. Επαληθεύστε πάροχο, συμβολοσειρά σύνδεσης, όνομα βάσης και διαπιστευτήρια.
3. Διαπιστώστε αν το μήνυμα αφορά κεντρική βάση ή τοπική cache.
4. Διατηρήστε δεδομένα και αρχεία καταγραφής· μην διαγράψετε τη βάση ως πρώτο βήμα.

Μη έγκυρο όνομα χρήστη ή κωδικός
• Ελέγξτε αν πρόκειται για σύνδεση στην εφαρμογή ή ταυτοποίηση στο ΕΡΓΑΝΗ· είναι διαφορετικά διαπιστευτήρια.
• Για offline σύνδεση στην εφαρμογή, μπορεί να απαιτείται ο χρήστης να είχε συγχρονιστεί προηγουμένως, ανάλογα με την εγκατάσταση.
• Ελέγξτε το ακριβές μήνυμα και την κατάσταση λογαριασμού πριν από αλλαγές.

Αποτυχία υποβολής ΕΡΓΑΝΗ
• Διαβάστε όλη την απάντηση, εντοπίστε εργαζόμενο/ημερομηνία/κατηγορία και διορθώστε το πρόβλημα επικύρωσης ή σύνδεσης.
• Ελέγξτε αν επιστράφηκε πρωτόκολλο.
• Επαναλάβετε μόνο αφού γίνει κατανοητή η αιτία και η εγγραφή παραμένει επιλέξιμη.

Το PDF/έγγραφο δεν είναι διαθέσιμο
• Ελέγξτε ότι η υποβολή έχει τα αναμενόμενα στοιχεία πρωτοκόλλου/ημερομηνίας και ότι χρησιμοποιείται η σωστή ενέργεια εγγράφου.
• Η απουσία PDF δεν σημαίνει απαραίτητα αποτυχία της αρχικής υποβολής· ελέγξτε απάντηση API και αρχείο καταγραφής.

Δεν έφτασε email
• Δοκιμάστε SMTP, ελέγξτε το αρχείο email, τα ανεπιθύμητα και τη σύνδεση.

Γενικός κανόνας
Καταγράψτε πλήρες μήνυμα σφάλματος, ώρα, εταιρεία, επηρεαζόμενη εγγραφή και πρωτόκολλο. Αφαιρέστε κωδικούς και μυστικά πριν κοινοποιήσετε διαγνωστικά στοιχεία.
""",
            "σφάλμα αντιμετώπιση βάση μη διαθέσιμη άκυρος κωδικός PDF 404 email SMTP offline αποτυχία"),

        new("glossary", "Αναφορά", "Γλωσσάρι και ορολογία",
            "Σύντομη αναφορά σε βασικούς ελληνικούς και αγγλικούς όρους.",
            """
            ΓΛΩΣΣΑΡΙ

Company — Εταιρεία
Branch — Υποκατάστημα
Employee — Εργαζόμενος
Schedule / working time — Πρόγραμμα / ωράριο
Overtime — Υπερωρία
Leave / holiday — Άδεια
Work card — Κάρτα εργασίας
Submission — Υποβολή
Protocol number — Αριθμός πρωτοκόλλου
Pending / unsent — Σε εκκρεμότητα / μη απεσταλμένο
Submitted / sent — Υποβλήθηκε / απεστάλη
Failed — Απέτυχε
Imported — Έγινε εισαγωγή
Offline cache — Τοπική προσωρινή βάση εκτός σύνδεσης
Backup — Αντίγραφο ασφαλείας
SMTP — Πρωτόκολλο αποστολής email

Τύποι χρήστη ΕΡΓΑΝΗ
• 01 — Εξωτερικός.
• 02 — Σύνδεση με κωδικούς ΕΡΓΑΝΗ.
• 03 — Σύνδεση με κωδικούς για Οικοδομοτεχνικά Έργα ΕΦΚΑ.

Η ορολογία μπορεί να διαφέρει ελαφρά μεταξύ οθονών. Όταν μια κατάσταση καθορίζει αν μια εγγραφή μπορεί να ξανασταλεί, ακολουθήστε τις οδηγίες της συγκεκριμένης οθόνης και το αρχείο υποβολών.
""",
            "γλωσσάρι ορολογία ελληνικά αγγλικά μετάφραση κατάσταση τύπος χρήστη ΕΦΚΑ"),

        new("users", "Διαχείριση", "Χρήστες και δικαιώματα",
            "Λογαριασμοί χρηστών, ρόλοι και πρόσβαση στις οθόνες.",
            """
            ΧΡΗΣΤΕΣ ΚΑΙ ΔΙΚΑΙΩΜΑΤΑ

1. Ανοίξτε τους Χρήστες με λογαριασμό που έχει δικαίωμα διαχείρισης χρηστών.
2. Ελέγξτε λογαριασμό, ενεργή κατάσταση, ρόλο και επιτρεπόμενα στοιχεία μενού πριν αλλάξετε πρόσβαση.
3. Δώστε μόνο τις οθόνες και ενέργειες που χρειάζεται ο χρήστης.
4. Αν λείπει επιλογή από το μενού, ελέγξτε ρόλο/δικαιώματα και μετά την επιλεγμένη εταιρεία και τη λειτουργία της εφαρμογής.

Καλή πρακτική
• Διατηρείτε έναν κατάλληλα προστατευμένο λογαριασμό διαχειριστή.
• Μην μοιράζεστε λογαριασμούς· οι ξεχωριστοί λογαριασμοί βοηθούν στον έλεγχο ενεργειών.
• Μην αφαιρείτε τον τελευταίο διαχειριστή και μην αλλάζετε τα δικά σας δικαιώματα πριν επιβεβαιώσετε ότι άλλος εξουσιοδοτημένος διαχειριστής μπορεί να συνδεθεί.
• Τα στοιχεία σύνδεσης στην εφαρμογή διαφέρουν από τους κωδικούς ταυτοποίησης στο ΕΡΓΑΝΗ.
""",
            "χρήστες λογαριασμός δικαιώματα ρόλοι πρόσβαση μενού σύνδεση διαχειριστής"),

        new("today-board", "Καθημερινές εργασίες", "Πίνακας σημερινής κατάστασης",
            "Σύγκριση προγράμματος και διαθέσιμων στοιχείων παρουσίας.",
            """
            ΠΙΝΑΚΑΣ ΣΗΜΕΡΙΝΗΣ ΚΑΤΑΣΤΑΣΗΣ

1. Ανοίξτε το Σήμερα και επιβεβαιώστε την ενεργή εταιρεία και ημερομηνία.
2. Ελέγξτε το πρόγραμμα εργαζομένου και τα διαθέσιμα στοιχεία κάρτας εργασίας/παρουσίας.
3. Αν λείπει εργαζόμενος ή συμβάν, ελέγξτε ημερομηνία, υποκατάστημα, ενεργή κατάσταση και ιστορικό κάρτας.
4. Ανοίξτε την αρχική εγγραφή προγράμματος ή κάρτας για να διερευνήσετε τη διαφορά.

Ο ημερήσιος πίνακας είναι λειτουργική σύνοψη. Η απουσία συμβάντος μπορεί να οφείλεται σε διαθεσιμότητα δεδομένων, συγχρονισμό, φίλτρα ή αποκλεισμένη σάρωση· δεν αποδεικνύει αυτόματα ότι δεν συνέβη τίποτα.

Για αμφισβητούμενη είσοδο ή έξοδο, ελέγξτε το αρχικό συμβάν και το αρχείο καταγραφής και διατηρήστε τις λεπτομέρειες.
""",
            "σήμερα ημερήσιος πίνακας κατάσταση παρουσία πρόγραμμα είσοδος έξοδος εργαζόμενος"),

        new("work-card-scan", "Καθημερινές εργασίες", "Σάρωση κάρτας και τερματικό",
            "Σαρώσεις, αποκλεισμένα συμβάντα, χειροκίνητες εγγραφές και σύνδεση.",
            """
            ΣΑΡΩΣΗ ΚΑΡΤΑΣ ΕΡΓΑΣΙΑΣ ΚΑΙ ΤΕΡΜΑΤΙΚΟ

Κανονική σάρωση
1. Επιβεβαιώστε ότι το τερματικό είναι συνδεδεμένο και ρυθμισμένο για τη σωστή εταιρεία/υποκατάστημα.
2. Σαρώστε το αναγνωριστικό εργαζομένου.
3. Διαβάστε το αποτέλεσμα και ελέγξτε αν η σάρωση έγινε δεκτή ή αποκλείστηκε.
4. Αν απορριφθεί, καταγράψτε τον λόγο και ελέγξτε το αρχείο σαρώσεων.

Αποκλεισμένες σαρώσεις
• Κανόνες εταιρείας μπορεί να αποκλείουν είσοδο, για παράδειγμα όταν απαιτείται πρόγραμμα και δεν υπάρχει.
• Επαληθεύστε την αποθηκευμένη ρύθμιση πριν δοκιμάσετε αλλαγή κανόνα.
• Η αποκλεισμένη σάρωση δεν είναι επιτυχές συμβάν παρουσίας.

Σύνδεση
• Η απώλεια σύνδεσης επηρεάζει το αν το συμβάν αποστέλλεται αμέσως ή μπαίνει σε ουρά. Ελέγξτε την κατάσταση και το αρχείο καταγραφής.
• Οι χειροκίνητες/καθυστερημένες κάρτες μπορεί να έχουν διαφορετική συμπεριφορά offline. Ακολουθήστε την προειδοποίηση της φόρμας.
• Μετά την επανασύνδεση, ελέγξτε αν συγχρονίστηκαν τα συμβάντα και αν χρειάζεται χειρισμός αποτυχημένων εγγραφών.

Όταν αναφέρετε πρόβλημα, κρατήστε εργαζόμενο, ώρα, υποκατάστημα, συσκευή και αποτέλεσμα. Μην ξεκινάτε με διπλή σάρωση.
""",
            "σάρωση τερματικό συσκευή κάρτα εργασίας αποκλεισμός είσοδος εκτός σύνδεσης ουρά υποκατάστημα χειροκίνητη"),

        new("database-options", "Διαχείριση", "Επιλογές βάσης και ρύθμιση σύνδεσης",
            "Πάροχος βάσης, σύνδεση και ρυθμίσεις εγκατάστασης.",
            """
            ΕΠΙΛΟΓΕΣ ΒΑΣΗΣ ΚΑΙ ΡΥΘΜΙΣΗ ΣΥΝΔΕΣΗΣ

Αλλάξτε τις ρυθμίσεις μόνο αν γνωρίζετε πώς συνδέεται η εγκατάσταση με την κεντρική βάση και τα τοπικά δεδομένα.

Πριν από αλλαγές
1. Εντοπίστε τον πάροχο και τη βάση που έχει ρυθμιστεί.
2. Επιβεβαιώστε διακομιστή/διεύθυνση, όνομα βάσης και στοιχεία σύνδεσης με τον διαχειριστή.
3. Διαπιστώστε αν χρησιμοποιείται κεντρική βάση, τοπική SQLite ή offline cache.
4. Πριν από συντήρηση ή μετάβαση, επαληθεύστε αντίγραφο ασφαλείας με μέθοδο που υποστηρίζει ο πάροχος.

Αν η βάση δεν είναι διαθέσιμη
• Διαβάστε ολόκληρο το μήνυμα και τις λεπτομέρειες καταγραφής.
• Ελέγξτε δίκτυο, διακομιστή, όνομα βάσης, διαπιστευτήρια και ρυθμίσεις παρόχου.
• Χρησιμοποιήστε διαθέσιμο έλεγχο σύνδεσης/εγκατάστασης πριν αλλάξετε ή δημιουργήσετε ξανά δεδομένα.
• Μη διαγράφετε βάση ή cache ως γενική λύση· η cache μπορεί να περιέχει δεδομένα για εργασία offline.

SQL Server, MariaDB και SQLite έχουν διαφορετικές απαιτήσεις αντιγράφων ασφαλείας και συντήρησης. Ακολουθήστε τις οδηγίες για τον πάροχο της εγκατάστασής σας.
""",
            "βάση επιλογές εγκατάσταση σύνδεση πάροχος SQL Server MariaDB SQLite κεντρική τοπική cache"),

        new("imports-sync", "Εταιρεία και ρυθμίσεις", "Εισαγωγή και συγχρονισμός",
            "Εισαγωγή στοιχείων εργαζομένων/υποκαταστημάτων και συγχρονισμός offline cache.",
            """
            ΕΙΣΑΓΩΓΗ ΚΑΙ ΣΥΓΧΡΟΝΙΣΜΟΣ

Πριν από την εισαγωγή
1. Επιβεβαιώστε την ενεργή εταιρεία και την προέλευση των δεδομένων.
2. Ελέγξτε τη μορφή αρχείου, τις απαιτούμενες στήλες και τα αναγνωριστικά εργαζομένων/υποκαταστημάτων της φόρμας εισαγωγής.
3. Ελέγξτε την προεπισκόπηση ή τα αποτελέσματα επικύρωσης, όπου διατίθενται, πριν επιβεβαιώσετε.
4. Διερευνήστε γραμμές που παραλείφθηκαν ή απορρίφθηκαν από το μήνυμα ή το αρχείο εισαγωγής αντί να εισάγετε επανειλημμένα το ίδιο αρχείο.

Μετά την εισαγωγή
• Επαληθεύστε πλήθος εργαζομένων/υποκαταστημάτων και ελέγξτε μερικές εγγραφές στην οθόνη προορισμού.
• Μη δημιουργείτε διπλές εγγραφές για να καλύψετε στοιχεία που λείπουν· ελέγξτε πρώτα αναγνωριστικά, εταιρεία και υποκατάστημα.
• Η εισαγωγή ενημερώνει τις εγγραφές της εφαρμογής, αλλά δεν υποβάλλει απαραίτητα δεδομένα στο ΕΡΓΑΝΗ.

Συγχρονισμός και offline cache
• Η εφαρμογή μπορεί να συγχρονίζει δεδομένα επιλεγμένης εταιρείας μεταξύ κεντρικής βάσης και τοπικής cache.
• Όταν αποκατασταθεί η σύνδεση, χρησιμοποιήστε την διαθέσιμη ενέργεια ανανέωσης/συγχρονισμού και ελέγξτε αποτέλεσμα και σφάλματα.
• Επιτυχής ανανέωση cache δεν αποδεικνύει ότι έγιναν δεκτές από το ΕΡΓΑΝΗ εκκρεμείς υποβολές προγραμμάτων, υπερωριών, αδειών ή καρτών εργασίας.
• Μη διαγράφετε cache ή κεντρική βάση για να εξαναγκάσετε συγχρονισμό. Διατηρήστε τις εγγραφές και διερευνήστε πρώτα το σφάλμα.
""",
            "εισαγωγή εισαγωγές συγχρονισμός εργαζόμενος υποκατάστημα αρχείο γραμμές cache offline ανανέωση"),

        new("release-notes", "Αναφορά", "Οδηγός βοήθειας και εκδόσεις",
            "Πώς συντηρείται ο οδηγός και πώς αναφέρετε θέμα που λείπει.",
            """
            ΣΧΕΤΙΚΑ ΜΕ ΤΟΝ ΟΔΗΓΟ ΒΟΗΘΕΙΑΣ

Ο οδηγός είναι ενσωματωμένος στην εφαρμογή και λειτουργεί χωρίς σύνδεση στο διαδίκτυο.

• Ακολουθεί τη γλώσσα της εφαρμογής (Ελληνικά ή Αγγλικά).
• Χρησιμοποιήστε την αναζήτηση για λειτουργία, μήνυμα σφάλματος ή εναλλακτικό όρο.
• Αν ο λογαριασμός σας διαθέτει την ενέργεια Επεξεργασία θέματος, μπορείτε να αποθηκεύσετε προσαρμοσμένο κείμενο ανά γλώσσα.
• Η επαναφορά θέματος αφαιρεί την προσαρμογή και επαναφέρει το ενσωματωμένο κείμενο.
• Παλαιότερο αποθηκευμένο κείμενο βοήθειας, αν υπάρχει, εμφανίζεται ξεχωριστά και δεν αντικαθίσταται σιωπηρά.

Ο οδηγός είναι λειτουργική βοήθεια και δεν αντικαθιστά την επίσημη τεκμηρίωση του ΕΡΓΑΝΗ ή νομικές συμβουλές. Τα ακριβή χειριστήρια μπορεί να διαφέρουν ανά ρόλο, ρύθμιση και έκδοση.

Όταν αναφέρετε ότι λείπει μια οδηγία, συμπεριλάβετε όνομα οθόνης, γλώσσα, έκδοση εφαρμογής αν είναι γνωστή και βήματα που οδήγησαν στο πρόβλημα. Μην συμπεριλάβετε κωδικούς ή μυστικά.
""",
            "βοήθεια οδηγός έκδοση σημειώσεις επεξεργασία επαναφορά γλώσσα εκτός σύνδεσης υποστήριξη")
    ];

}

public sealed record HelpTopic(
    string Id,
    string Category,
    string Title,
    string Summary,
    string Content,
    string Keywords);
