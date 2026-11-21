using System.ComponentModel;
using System.Globalization;
using System.Text.Json;

namespace SPharmacyLog.UI.Localization;

public sealed class LocalizationService : ILocalizationService
{
    private static readonly string SettingsFile = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "SPharmacyLog", "language.json");

    private static readonly IReadOnlyDictionary<string, string> English = new Dictionary<string, string>
    {
        ["App.Title"] = "S Pharmacy Log",
        ["Login.Title"] = "Sign in",
        ["Login.Username"] = "Username",
        ["Login.Password"] = "Password",
        ["Login.SignIn"] = "Sign in",
        ["Login.CreateAdmin"] = "Create first administrator",
        ["Login.FirstAdminHint"] = "First launch: create the administrator account (minimum 8-character password).",
        ["Login.InvalidCredentials"] = "Login failed. Check the username, password, and account status.",
        ["Login.AdminCreated"] = "Administrator created. Sign in with the new credentials.",
        ["Login.Subtitle"] = "Prescription, customer and medicine tracking",
        ["Navigation.ExpiryCalendar"] = "Expiry calendar",
        ["Navigation.Customers"] = "Customers",
        ["Customers.Search"] = "Search by customer no, name, surname, AMKA, phone or email…",
        ["Customers.New"] = "＋ New customer", ["Customers.Details"] = "CUSTOMER DETAILS", ["Customers.Save"] = "Save customer", ["Customers.Clear"] = "Clear",
        ["Customers.Required"] = "Customer number, name and surname are required.", ["Customers.Duplicate"] = "This customer number is already in use.", ["Customers.NewStatus"] = "New customer", ["Customers.Saved"] = "Customer {0} saved successfully.", ["Customers.Editing"] = "Editing customer {0}",
        ["Navigation.Borrowing"] = "Borrowing",
        ["Navigation.Subscriptions"] = "Subscriptions",
        ["Navigation.Payments"] = "Payments & ledger",
        ["Navigation.Users"] = "Users",
        ["Calendar.Title"] = "Prescription expiry calendar",
        ["Calendar.Description"] = "Each day shows active prescriptions expiring. Select a date for the list.",
        ["Calendar.Expiring"] = "expiring",
        ["Calendar.None"] = "—",
        ["Calendar.DaySummary"] = "{0} — {1} active prescription(s) expiring",
        ["Common.Queued"] = "Module is queued for implementation.",
        ["Language.English"] = "English",
        ["Language.Greek"] = "Greek"
    };

    private static readonly IReadOnlyDictionary<string, string> Greek = new Dictionary<string, string>
    {
        ["App.Title"] = "S Pharmacy Log",
        ["Login.Title"] = "Σύνδεση",
        ["Login.Username"] = "Όνομα χρήστη",
        ["Login.Password"] = "Κωδικός πρόσβασης",
        ["Login.SignIn"] = "Σύνδεση",
        ["Login.CreateAdmin"] = "Δημιουργία πρώτου διαχειριστή",
        ["Login.FirstAdminHint"] = "Πρώτη εκκίνηση: δημιουργήστε λογαριασμό διαχειριστή (τουλάχιστον 8 χαρακτήρες κωδικού).",
        ["Login.InvalidCredentials"] = "Αποτυχία σύνδεσης. Ελέγξτε τα στοιχεία και την κατάσταση του λογαριασμού.",
        ["Login.AdminCreated"] = "Ο διαχειριστής δημιουργήθηκε. Συνδεθείτε με τα νέα στοιχεία.",
        ["Login.Subtitle"] = "Διαχείριση συνταγών, πελατών και φαρμάκων",
        ["Navigation.ExpiryCalendar"] = "Ημερολόγιο λήξεων",
        ["Navigation.Customers"] = "Πελάτες",
        ["Customers.Search"] = "Αναζήτηση με αριθμό, όνομα, επώνυμο, ΑΜΚΑ, τηλέφωνο ή email…",
        ["Customers.New"] = "＋ Νέος πελάτης", ["Customers.Details"] = "ΣΤΟΙΧΕΙΑ ΠΕΛΑΤΗ", ["Customers.Save"] = "Αποθήκευση πελάτη", ["Customers.Clear"] = "Καθαρισμός",
        ["Customers.Required"] = "Ο αριθμός πελάτη, το όνομα και το επώνυμο είναι υποχρεωτικά.", ["Customers.Duplicate"] = "Ο αριθμός πελάτη χρησιμοποιείται ήδη.", ["Customers.NewStatus"] = "Νέος πελάτης", ["Customers.Saved"] = "Ο πελάτης {0} αποθηκεύτηκε επιτυχώς.", ["Customers.Editing"] = "Επεξεργασία πελάτη {0}",
        ["Navigation.Borrowing"] = "Δανεισμοί",
        ["Navigation.Subscriptions"] = "Συνδρομές",
        ["Navigation.Payments"] = "Πληρωμές & καρτέλα",
        ["Navigation.Users"] = "Χρήστες",
        ["Calendar.Title"] = "Ημερολόγιο λήξης συνταγών",
        ["Calendar.Description"] = "Κάθε ημέρα εμφανίζει τις ενεργές συνταγές που λήγουν. Επιλέξτε ημερομηνία για τη λίστα.",
        ["Calendar.Expiring"] = "λήγουν",
        ["Calendar.None"] = "—",
        ["Calendar.DaySummary"] = "{0} — {1} ενεργές συνταγές λήγουν",
        ["Common.Queued"] = "Η ενότητα θα υλοποιηθεί σε επόμενο στάδιο.",
        ["Language.English"] = "Αγγλικά",
        ["Language.Greek"] = "Ελληνικά"
    };


    private static readonly IReadOnlyDictionary<string, string> UiGreek = new Dictionary<string, string>
    {
        ["Prescription, customer and medicine tracking"] = "Διαχείριση συνταγών, πελατών και φαρμάκων",
        ["Secure pharmacy management"] = "Ασφαλής διαχείριση φαρμακείου",
        ["WORKSPACE"] = "ΧΩΡΟΣ ΕΡΓΑΣΙΑΣ",
        ["PHARMACY MANAGEMENT"] = "ΔΙΑΧΕΙΡΙΣΗ ΦΑΡΜΑΚΕΙΟΥ",
        ["Pharmacy workspace"] = "Χώρος εργασίας φαρμακείου",
        ["Manage prescriptions, customers and services"] = "Διαχείριση συνταγών, πελατών και υπηρεσιών",
        ["S Pharmacy Log  •  Local pharmacy operations"] = "S Pharmacy Log  •  Τοπική λειτουργία φαρμακείου",
        ["Prescription expiry calendar"] = "Ημερολόγιο λήξης συνταγών",
        ["Each day shows active prescriptions expiring. Select a date for the list."] = "Κάθε ημέρα εμφανίζει τις ενεργές συνταγές που λήγουν. Επιλέξτε ημερομηνία για τη λίστα.",
        ["expiring"] = "λήγουν",
        ["active prescription(s) expiring"] = "ενεργές συνταγές λήγουν",
        ["Search by customer no, name, surname, AMKA, phone or email…"] = "Αναζήτηση με αριθμό, όνομα, επώνυμο, ΑΜΚΑ, τηλέφωνο ή email…",
        ["＋ New customer"] = "＋ Νέος πελάτης",
        ["CUSTOMER DETAILS"] = "ΣΤΟΙΧΕΙΑ ΠΕΛΑΤΗ",
        ["Save customer"] = "Αποθήκευση πελάτη",
        ["Clear"] = "Καθαρισμός",
        ["Customer number"] = "Αριθμός πελάτη",
        ["Name"] = "Όνομα",
        ["Surname"] = "Επώνυμο",
        ["Address"] = "Διεύθυνση",
        ["City"] = "Πόλη",
        ["Postcode"] = "Ταχυδρομικός κώδικας",
        ["Telephone"] = "Τηλέφωνο",
        ["Mobile"] = "Κινητό",
        ["Email"] = "Email",
        ["Insurance"] = "Ασφάλιση",
        ["Notes"] = "Σημειώσεις",
        ["Overview"] = "Επισκόπηση",
        ["Details"] = "Στοιχεία",
        ["Borrowings"] = "Δανεισμοί",
        ["Subscriptions"] = "Συνδρομές",
        ["Payments"] = "Πληρωμές",
        ["Ledger"] = "Καρτέλα",
        ["Select a customer to see their account summary."] = "Επιλέξτε πελάτη για να δείτε τη σύνοψη του λογαριασμού του.",
        ["Select an existing borrowing to view its saved GS1 scan details."] = "Επιλέξτε έναν υπάρχοντα δανεισμό για να δείτε τα αποθηκευμένα στοιχεία σάρωσης GS1.",
        ["Scan GS1 DataMatrix here (scanner suffix: Enter)"] = "Σαρώστε εδώ το GS1 DataMatrix (τερματισμός σαρωτή: Enter)",
        ["The scanned barcode text will appear here; <GS> marks the invisible ASCII 29 separator."] = "Το κείμενο του barcode θα εμφανιστεί εδώ· το <GS> δηλώνει τον μη ορατό διαχωριστή ASCII 29.",
        ["Ready for a GS1 DataMatrix scan."] = "Έτοιμο για σάρωση GS1 DataMatrix.",
        ["PHARMACEUTICAL PACK SCAN"] = "ΣΑΡΩΣΗ ΣΥΣΚΕΥΑΣΙΑΣ ΦΑΡΜΑΚΟΥ",
        ["Scan the 2D DataMatrix. Configure the scanner to transmit ASCII 29 (GS) between variable-length fields and send Enter after the scan. The original scanner text is retained exactly; the fields below are extracted from GS1 Application Identifiers."] = "Σαρώστε το 2D DataMatrix. Ρυθμίστε τον σαρωτή να μεταδίδει ASCII 29 (GS) μεταξύ πεδίων μεταβλητού μήκους και να στέλνει Enter μετά τη σάρωση. Το αρχικό κείμενο διατηρείται ακριβώς· τα παρακάτω πεδία εξάγονται από τα GS1 Application Identifiers.",
        ["SCANNED BARCODE — VERIFY THE READ"] = "ΣΑΡΩΜΕΝΟ BARCODE — ΕΛΕΓΞΤΕ ΤΗΝ ΑΝΑΓΝΩΣΗ",
        ["The text is shown exactly as received, with invisible ASCII 29 separators displayed as <GS> so they can be checked visually."] = "Το κείμενο εμφανίζεται όπως παραλήφθηκε. Οι μη ορατοί διαχωριστές ASCII 29 εμφανίζονται ως <GS> για οπτικό έλεγχο.",
        ["GTIN-14 (AI 01)"] = "GTIN-14 (AI 01)",
        ["Serial number (AI 21)"] = "Σειριακός αριθμός (AI 21)",
        ["Expiry date (AI 17)"] = "Ημερομηνία λήξης (AI 17)",
        ["Batch / lot (AI 10)"] = "Παρτίδα / lot (AI 10)",
        ["Raw scanner payload"] = "Αρχικά δεδομένα σαρωτή",
        ["Pack status"] = "Κατάσταση συσκευασίας",
        ["Medicine catalogue match"] = "Αντιστοίχιση φαρμάκου",
        ["Not matched"] = "Δεν υπάρχει αντιστοίχιση",
        ["Greek EOF / NTIN barcode (optional mapping)"] = "Barcode EOF / NTIN (προαιρετική αντιστοίχιση)",
        ["13-digit 0280... EOF barcode"] = "13 ψηφία — barcode EOF 0280...",
        ["Medicine name (auto-filled when barcode is mapped)"] = "Όνομα φαρμάκου (συμπληρώνεται αυτόματα όταν υπάρχει αντιστοίχιση)",
        ["Quantity (one serialized pack per record)"] = "Ποσότητα (μία σειριοποιημένη συσκευασία ανά εγγραφή)",
        ["Internal barcode / borrowing reference"] = "Εσωτερικό barcode / αναφορά δανεισμού",
        ["Due date (yyyy-MM-dd)"] = "Ημερομηνία επιστροφής (yyyy-MM-dd)",
        ["Value"] = "Αξία",
        ["＋ Add borrowing"] = "＋ Προσθήκη δανεισμού",
        ["Mark selected as returned"] = "Σήμανση επιλεγμένου ως επιστραφέντος",
        ["Parse scan"] = "Ανάλυση σάρωσης",
        ["＋ Add subscription"] = "＋ Προσθήκη συνδρομής",
        ["Subscription title"] = "Τίτλος συνδρομής",
        ["Start date (yyyy-MM-dd)"] = "Ημερομηνία έναρξης (yyyy-MM-dd)",
        ["Next due date (yyyy-MM-dd)"] = "Επόμενη ημερομηνία πληρωμής (yyyy-MM-dd)",
        ["Expiry date (yyyy-MM-dd, optional)"] = "Ημερομηνία λήξης (yyyy-MM-dd, προαιρετικό)",
        ["Frequency"] = "Συχνότητα",
        ["Monthly"] = "Μηνιαία",
        ["＋ Record payment"] = "＋ Καταχώριση πληρωμής",
        ["Payment amount"] = "Ποσό πληρωμής",
        ["Method (Cash / Card / Transfer)"] = "Μέθοδος (Μετρητά / Κάρτα / Μεταφορά)",
        ["Cash"] = "Μετρητά",
        ["Reference"] = "Αναφορά",
        ["Entry type (Charge / Adjustment / Reversal)"] = "Τύπος εγγραφής (Χρέωση / Προσαρμογή / Αντιστροφή)",
        ["Amount (positive number)"] = "Ποσό (θετικός αριθμός)",
        ["Description"] = "Περιγραφή",
        ["＋ Add ledger entry"] = "＋ Προσθήκη εγγραφής καρτέλας",
        ["Save or select a customer first."] = "Αποθηκεύστε ή επιλέξτε πρώτα έναν πελάτη.",
        ["Medicine name and internal borrowing reference are required."] = "Το όνομα φαρμάκου και η εσωτερική αναφορά δανεισμού είναι υποχρεωτικά.",
        ["Enter a valid quantity."] = "Εισαγάγετε έγκυρη ποσότητα.",
        ["A serialized DataMatrix identifies one physical pack. Create one borrowing record per scanned pack (quantity 1)."] = "Ένα σειριοποιημένο DataMatrix αντιστοιχεί σε μία φυσική συσκευασία. Δημιουργήστε μία εγγραφή δανεισμού ανά σάρωση (ποσότητα 1).",
        ["This internal barcode is already in use."] = "Αυτό το εσωτερικό barcode χρησιμοποιείται ήδη.",
        ["The EOF / NTIN barcode must contain exactly 13 digits."] = "Το barcode EOF / NTIN πρέπει να περιέχει ακριβώς 13 ψηφία.",
        ["Borrowing record added."] = "Ο δανεισμός προστέθηκε.",
        ["GS1 pack scan and borrowing saved successfully. The raw payload and all parsed fields are stored."] = "Η σάρωση GS1 και ο δανεισμός αποθηκεύτηκαν επιτυχώς. Τα αρχικά δεδομένα και όλα τα πεδία αποθηκεύτηκαν.",
        ["Ready for the next GS1 DataMatrix scan."] = "Έτοιμο για την επόμενη σάρωση GS1 DataMatrix.",
        ["Select a borrowing record first."] = "Επιλέξτε πρώτα μια εγγραφή δανεισμού.",
        ["No active borrowing matches the entered barcode."] = "Δεν υπάρχει ενεργός δανεισμός που να αντιστοιχεί στο barcode.",
        ["Item marked as returned."] = "Το αντικείμενο σημειώθηκε ως επιστραφέν.",
        ["Enter a title and valid next due date."] = "Εισαγάγετε τίτλο και έγκυρη επόμενη ημερομηνία πληρωμής.",
        ["Subscription added."] = "Η συνδρομή προστέθηκε.",
        ["Enter a payment amount greater than zero."] = "Εισαγάγετε ποσό πληρωμής μεγαλύτερο από μηδέν.",
        ["Payment recorded and ledger updated."] = "Η πληρωμή καταχωρίστηκε και η καρτέλα ενημερώθηκε.",
        ["Enter a positive amount and description."] = "Εισαγάγετε θετικό ποσό και περιγραφή.",
        ["Choose Charge, Adjustment or Reversal. Payments should be recorded in the Payments tab."] = "Επιλέξτε Χρέωση, Προσαρμογή ή Αντιστροφή. Οι πληρωμές πρέπει να καταχωρίζονται στην καρτέλα Πληρωμές.",
        ["Ledger entry added."] = "Η εγγραφή καρτέλας προστέθηκε.",
        ["This GTIN + serial number has already been registered. This physical pack cannot be added again."] = "Αυτός ο συνδυασμός GTIN + σειριακού αριθμού έχει ήδη καταχωριστεί. Η ίδια φυσική συσκευασία δεν μπορεί να προστεθεί ξανά.",
        ["GS1 scan is incomplete or invalid: "] = "Η σάρωση GS1 είναι ελλιπής ή μη έγκυρη: ",
        ["No medicine catalogue match yet; you can enter the medicine name and EOF barcode manually."] = "Δεν βρέθηκε αντιστοίχιση φαρμάκου. Μπορείτε να εισαγάγετε χειροκίνητα το όνομα και το barcode EOF.",
        ["Not matched — enter medicine manually"] = "Χωρίς αντιστοίχιση — εισαγάγετε χειροκίνητα το φάρμακο",
        ["＋ New customer"] = "＋ Νέος πελάτης",
        ["First launch: create the administrator account (minimum 8-character password)."] = "Πρώτη εκκίνηση: δημιουργήστε λογαριασμό διαχειριστή (κωδικός τουλάχιστον 8 χαρακτήρων).",
        ["Login failed. Check the username, password, and account status."] = "Αποτυχία σύνδεσης. Ελέγξτε το όνομα χρήστη, τον κωδικό και την κατάσταση του λογαριασμού.",
        ["Administrator created. Sign in with the new credentials."] = "Ο διαχειριστής δημιουργήθηκε. Συνδεθείτε με τα νέα στοιχεία.",
        ["Mon"] = "Δευ", ["Tue"] = "Τρί", ["Wed"] = "Τετ", ["Thu"] = "Πέμ", ["Fri"] = "Παρ", ["Sat"] = "Σάβ", ["Sun"] = "Κυρ",
        ["expires"] = "λήγει", ["Qty"] = "Ποσ.", ["Due"] = "Προθεσμία", ["Next:"] = "Επόμενο:", ["Expires:"] = "Λήξη:",
        ["Selected item:"] = "Επιλεγμένο αντικείμενο:", ["This is a legacy borrowing without a linked GS1 DataMatrix scan."] = "Πρόκειται για παλαιότερο δανεισμό χωρίς συνδεδεμένη σάρωση GS1 DataMatrix.",
        ["SAVED GS1 PACK DETAILS"] = "ΑΠΟΘΗΚΕΥΜΕΝΑ ΣΤΟΙΧΕΙΑ ΣΥΣΚΕΥΑΣΙΑΣ GS1",
        ["Medicine:"] = "Φάρμακο:", ["EOF / NTIN:"] = "EOF / NTIN:", ["Serial number:"] = "Σειριακός αριθμός:",
        ["Expiry:"] = "Λήξη:", ["Batch / lot:"] = "Παρτίδα / lot:", ["Pack status:"] = "Κατάσταση συσκευασίας:",
        ["Scanned at:"] = "Σαρώθηκε:", ["Raw scanner payload:"] = "Αρχικά δεδομένα σαρωτή:",
        ["GS1 scan parsed successfully. Review the extracted details before saving the borrowing."] = "Η σάρωση GS1 αναλύθηκε επιτυχώς. Ελέγξτε τα στοιχεία πριν αποθηκεύσετε τον δανεισμό.",
        ["This GTIN + serial number has already been registered. This physical pack cannot be added again."] = "Αυτός ο συνδυασμός GTIN + σειριακού αριθμού έχει ήδη καταχωριστεί. Η ίδια φυσική συσκευασία δεν μπορεί να προστεθεί ξανά.",
        ["The scanned GTIN is already mapped to a different medicine. Resolve the catalogue barcode conflict first."] = "Το σαρωμένο GTIN αντιστοιχεί ήδη σε διαφορετικό φάρμακο. Επιλύστε πρώτα τη διένεξη barcode του καταλόγου.",
        ["The EOF / NTIN barcode is already mapped to a different medicine. Resolve the catalogue barcode conflict first."] = "Το barcode EOF / NTIN αντιστοιχεί ήδη σε διαφορετικό φάρμακο. Επιλύστε πρώτα τη διένεξη barcode του καταλόγου.",
        ["This exact physical pack (GTIN + serial number) has already been scanned. Duplicate registration is blocked."] = "Η συγκεκριμένη φυσική συσκευασία (GTIN + σειριακός αριθμός) έχει ήδη σαρωθεί. Η διπλή καταχώριση αποτρέπεται.",
        ["No active borrowing matches the entered barcode."] = "Δεν υπάρχει ενεργός δανεισμός που να αντιστοιχεί στο barcode.",
        ["Item marked as returned."] = "Το αντικείμενο σημειώθηκε ως επιστραφέν.",
        ["Payment recorded and ledger updated."] = "Η πληρωμή καταχωρίστηκε και η καρτέλα ενημερώθηκε.",
        ["Ledger entry added."] = "Η εγγραφή καρτέλας προστέθηκε.",
        ["BORROWINGS:"] = "ΔΑΝΕΙΣΜΟΙ:", ["SUBSCRIPTIONS:"] = "ΣΥΝΔΡΟΜΕΣ:", ["PAYMENTS:"] = "ΠΛΗΡΩΜΕΣ:",
        ["ACCOUNT BALANCE:"] = "ΥΠΟΛΟΙΠΟ ΛΟΓΑΡΙΑΣΜΟΥ:", ["Recent activity:"] = "Πρόσφατη δραστηριότητα:",
        ["Telephone:"] = "Τηλέφωνο:", ["Email:"] = "Email:", ["Total paid:"] = "Σύνολο πληρωμών:",
        ["currently borrowed"] = "σε δανεισμό", ["active of"] = "ενεργές από", ["recorded"] = "καταχωρίστηκαν"
    };

    public string TranslateText(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;
        var english = text;
        foreach (var pair in UiGreek.OrderByDescending(x => x.Value.Length))
            english = english.Replace(pair.Value, pair.Key, StringComparison.Ordinal);
        if (CurrentLanguage == AppLanguage.English) return english;

        if (UiGreek.TryGetValue(english, out var exact)) return exact;
        foreach (var pair in UiGreek.OrderByDescending(x => x.Key.Length))
            english = english.Replace(pair.Key, pair.Value, StringComparison.Ordinal);
        return english;
    }

    private IReadOnlyDictionary<string, string> current = English;
    public AppLanguage CurrentLanguage { get; private set; } = AppLanguage.English;
    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler? LanguageChanged;

    public LocalizationService()
    {
        try
        {
            if (File.Exists(SettingsFile))
            {
                var saved = JsonSerializer.Deserialize<LanguageSettings>(File.ReadAllText(SettingsFile));
                if (Enum.TryParse(saved?.Language, out AppLanguage language))
                    Apply(language);
            }
        }
        catch { Apply(AppLanguage.English); }
    }

    public string this[string key] => current.TryGetValue(key, out var value)
        ? value
        : English.TryGetValue(key, out var fallback) ? fallback : $"[{key}]";

    public void SetLanguage(AppLanguage language)
    {
        if (language == CurrentLanguage) return;
        Apply(language);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsFile)!);
            File.WriteAllText(SettingsFile, JsonSerializer.Serialize(new LanguageSettings { Language = language.ToString() }));
        }
        catch { }
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CurrentLanguage)));
        LanguageChanged?.Invoke(this, EventArgs.Empty);
    }

    private void Apply(AppLanguage language)
    {
        CurrentLanguage = language;
        current = language == AppLanguage.Greek ? Greek : English;
        try
        {
            var culture = new CultureInfo(language == AppLanguage.Greek ? "el-GR" : "en-GB");
            CultureInfo.DefaultThreadCurrentCulture = culture;
            CultureInfo.DefaultThreadCurrentUICulture = culture;
        }
        catch { }
    }

    private sealed class LanguageSettings { public string Language { get; set; } = nameof(AppLanguage.English); }
}