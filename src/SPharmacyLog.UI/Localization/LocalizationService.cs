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
        ["First launch: create the administrator account (minimum 8-character password)."] = "Πρώτη εκκίνηση: δημιουργήστε λογαριασμό διαχειριστή (κωδικός τουλάχιστον 8 χαρακτήρων).",
        ["Login failed. Check the username, password, and account status."] = "Αποτυχία σύνδεσης. Ελέγξτε το όνομα χρήστη, τον κωδικό και την κατάσταση του λογαριασμού.",
        ["Administrator created. Sign in with the new credentials."] = "Ο διαχειριστής δημιουργήθηκε. Συνδεθείτε με τα νέα στοιχεία.",
        ["Mon"] = "Δευ", ["Tue"] = "Τρί", ["Wed"] = "Τετ", ["Thu"] = "Πέμ", ["Fri"] = "Παρ", ["Sat"] = "Σάβ", ["Sun"] = "Κυρ",
        ["expires"] = "λήγει", ["Qty"] = "Ποσ.", ["Due"] = "Προθεσμία", ["Next:"] = "Επόμενο:", ["Expires:"] = "Λήγει:",
        ["Selected item:"] = "Επιλεγμένο αντικείμενο:", ["This is a legacy borrowing without a linked GS1 DataMatrix scan."] = "Πρόκειται για παλαιότερο δανεισμό χωρίς συνδεδεμένη σάρωση GS1 DataMatrix.",
        ["SAVED GS1 PACK DETAILS"] = "ΑΠΟΘΗΚΕΥΜΕΝΑ ΣΤΟΙΧΕΙΑ ΣΥΣΚΕΥΑΣΙΑΣ GS1",
        ["Medicine:"] = "Φάρμακο:", ["EOF / NTIN:"] = "EOF / NTIN:", ["Serial number:"] = "Σειριακός αριθμός:",
        ["Expiry:"] = "Λήξη:", ["Batch / lot:"] = "Παρτίδα / lot:", ["Pack status:"] = "Κατάσταση συσκευασίας:",
        ["Scanned at:"] = "Σαρώθηκε:", ["Raw scanner payload:"] = "Αρχικά δεδομένα σαρωτή:",
        ["GS1 scan parsed successfully. Review the extracted details before saving the borrowing."] = "Η σάρωση GS1 αναλύθηκε επιτυχώς. Ελέγξτε τα στοιχεία πριν αποθηκεύσετε τον δανεισμό.",
        ["The scanned GTIN is already mapped to a different medicine. Resolve the catalogue barcode conflict first."] = "Το σαρωμένο GTIN αντιστοιχεί ήδη σε διαφορετικό φάρμακο. Επιλύστε πρώτα τη διένεξη barcode του καταλόγου.",
        ["The EOF / NTIN barcode is already mapped to a different medicine. Resolve the catalogue barcode conflict first."] = "Το barcode EOF / NTIN αντιστοιχεί ήδη σε διαφορετικό φάρμακο. Επιλύστε πρώτα τη διένεξη barcode του καταλόγου.",
        ["This exact physical pack (GTIN + serial number) has already been scanned. Duplicate registration is blocked."] = "Η συγκεκριμένη φυσική συσκευασία (GTIN + σειριακός αριθμός) έχει ήδη σαρωθεί. Η διπλή καταχώριση αποτρέπεται.",
        ["BORROWINGS:"] = "ΔΑΝΕΙΣΜΟΙ:", ["SUBSCRIPTIONS:"] = "ΣΥΝΔΡΟΜΕΣ:", ["PAYMENTS:"] = "ΠΛΗΡΩΜΕΣ:",
        ["ACCOUNT BALANCE:"] = "ΥΠΟΛΟΙΠΟ ΛΟΓΑΡΙΑΣΜΟΥ:", ["Recent activity:"] = "Πρόσφατη δραστηριότητα:",
        ["Telephone:"] = "Τηλέφωνο:", ["Email:"] = "Email:", ["Total paid:"] = "Σύνολο πληρωμών:",
        ["currently borrowed"] = "σε δανεισμό", ["active of"] = "ενεργές από", ["recorded"] = "καταχωρίστηκαν",
        ["Incomplete GS1 Application Identifier at the end of the scan."] = "Ελλιπές GS1 Application Identifier στο τέλος της σάρωσης.",
        ["AI (01) must contain exactly 14 numeric GTIN digits."] = "Το AI (01) πρέπει να περιέχει ακριβώς 14 αριθμητικά ψηφία GTIN.",
        ["AI (17) must contain a valid YYMMDD expiry date."] = "Το AI (17) πρέπει να περιέχει έγκυρη ημερομηνία λήξης YYMMDD.",
        ["AI ({ai}) is present but its value is empty."] = "Το AI ({ai}) υπάρχει αλλά η τιμή του είναι κενή.",
        ["AI ({ai}) exceeds the GS1 maximum of 20 characters."] = "Το AI ({ai}) υπερβαίνει το μέγιστο όριο των 20 χαρακτήρων GS1.",
        ["Unsupported GS1 Application Identifier ({ai}). This screen currently expects AIs 01, 10, 17 and 21."] = "Μη υποστηριζόμενο GS1 Application Identifier ({ai}). Αυτή η οθόνη αναμένει τα AI 01, 10, 17 και 21.",
        ["The scan must contain all four fields: GTIN (01), serial (21), expiry (17) and batch/lot (10)."] = "Η σάρωση πρέπει να περιέχει και τα τέσσερα πεδία: GTIN (01), σειριακό αριθμό (21), λήξη (17) και παρτίδα (10).",
        ["Medicine matched:"] = "Αντιστοιχισμένο φάρμακο:",
        ["ACTIVE"] = "ΕΝΕΡΓΗ",
        ["RETURNED"] = "ΕΠΙΣΤΡΑΦΗΚΕ",
        [" total,"] = " συνολικά,",
        ["Borrowed"] = "Δανεισμένο",
        ["Returned"] = "Επιστραφέν",
        ["Active"] = "Ενεργή",
        ["Inactive"] = "Ανενεργή",
        ["Charge"] = "Χρέωση",
        ["Adjustment"] = "Προσαρμογή",
        ["Reversal"] = "Αντιστροφή",
        ["Payment"] = "Πληρωμή",
        ["Card"] = "Κάρτα",
        ["Transfer"] = "Μεταφορά",
        ["Admin"] = "Διαχειριστής",
        ["Pharmacist"] = "Φαρμακοποιός",
        ["Mobile:"] = "Κινητό:",
        ["Address:"] = "Διεύθυνση:",
        ["City:"] = "Πόλη:",
        [" (Admin)"] = " (Διαχειριστής)",
        [" (Pharmacist)"] = " (Φαρμακοποιός)",
        [" | Active |"] = " | Ενεργή |",
        [" | Borrowed |"] = " | Δανεισμένο |",
        [" | Returned |"] = " | Επιστραφέν |",
        [" | Charge |"] = " | Χρέωση |",
        [" | Adjustment |"] = " | Προσαρμογή |",
        [" | Reversal |"] = " | Αντιστροφή |",
        [" | Payment |"] = " | Πληρωμή |",
        [" | Card |"] = " | Κάρτα |",
        [" | Cash |"] = " | Μετρητά |",
        [" | Monthly"] = " | Μηνιαία",
        ["Customer "] = "Ο πελάτης ",
        [" saved successfully."] = " αποθηκεύτηκε επιτυχώς.",
        ["Editing customer "] = "Επεξεργασία πελάτη ",
        ["Username"] = "Όνομα χρήστη",
        ["Password"] = "Κωδικός πρόσβασης",
        ["S Pharmacy Log — Database setup"] = "S Pharmacy Log — Ρύθμιση βάσης δεδομένων",
        ["Database setup"] = "Ρύθμιση βάσης δεδομένων",
        ["Choose where S Pharmacy Log stores its data. You can use a local SQLite file or a shared SQL Server / MariaDB database."] = "Επιλέξτε πού θα αποθηκεύει τα δεδομένα το S Pharmacy Log. Μπορείτε να χρησιμοποιήσετε τοπικό αρχείο SQLite ή κοινόχρηστη βάση SQL Server / MariaDB.",
        ["Database provider"] = "Πάροχος βάσης δεδομένων",
        ["SQLite database file"] = "Αρχείο βάσης SQLite",
        ["SQL Server host / instance"] = "Διακομιστής / instance SQL Server",
        ["SQL Server database"] = "Βάση δεδομένων SQL Server",
        ["Use Windows authentication"] = "Χρήση ελέγχου ταυτότητας Windows",
        ["SQL Server username"] = "Όνομα χρήστη SQL Server",
        ["SQL Server password"] = "Κωδικός SQL Server",
        ["MariaDB server"] = "Διακομιστής MariaDB",
        ["MariaDB database"] = "Βάση δεδομένων MariaDB",
        ["MariaDB username"] = "Όνομα χρήστη MariaDB",
        ["MariaDB password"] = "Κωδικός MariaDB",
        ["Create the first administrator"] = "Δημιουργία πρώτου διαχειριστή",
        ["The administrator account is created once, after the database schema is ready."] = "Ο λογαριασμός διαχειριστή δημιουργείται μία φορά, αφού ετοιμαστεί το σχήμα της βάσης.",
        ["Administrator username"] = "Όνομα χρήστη διαχειριστή",
        ["Password (at least 8 characters)"] = "Κωδικός (τουλάχιστον 8 χαρακτήρες)",
        ["Confirm password"] = "Επιβεβαίωση κωδικού",
        ["Test connection"] = "Έλεγχος σύνδεσης",
        ["Create / connect and continue"] = "Δημιουργία / σύνδεση και συνέχεια",
        ["Create administrator"] = "Δημιουργία διαχειριστή",
        ["Select a supported database provider."] = "Επιλέξτε υποστηριζόμενο πάροχο βάσης δεδομένων.",
        ["Connection successful. The database is reachable."] = "Η σύνδεση ήταν επιτυχής. Η βάση δεδομένων είναι προσβάσιμη.",
        ["Database server is reachable. The application database does not exist yet; it will be created when you continue."] = "Ο διακομιστής βάσης δεδομένων είναι προσβάσιμος. Η βάση της εφαρμογής δεν υπάρχει ακόμη και θα δημιουργηθεί όταν συνεχίσετε.",
        ["Checking the database..."] = "Έλεγχος της βάσης δεδομένων...",
        ["Creating the database if it does not already exist..."] = "Δημιουργία της βάσης δεδομένων εάν δεν υπάρχει ήδη...",
        ["The database server could not be reached."] = "Δεν ήταν δυνατή η πρόσβαση στον διακομιστή βάσης δεδομένων.",
        ["Cannot connect to the database."] = "Δεν είναι δυνατή η σύνδεση στη βάση δεδομένων.",
        ["Applying database migrations. Existing data will be preserved..."] = "Εφαρμογή μεταναστεύσεων βάσης δεδομένων. Τα υπάρχοντα δεδομένα θα διατηρηθούν...",
        ["Database is ready. Existing user accounts were found."] = "Η βάση δεδομένων είναι έτοιμη. Βρέθηκαν υπάρχοντες λογαριασμοί χρηστών.",
        ["Database is ready. Create the first administrator to finish setup."] = "Η βάση δεδομένων είναι έτοιμη. Δημιουργήστε τον πρώτο διαχειριστή για να ολοκληρωθεί η ρύθμιση.",
        ["The database is ready. Create the first administrator to finish setup."] = "Η βάση δεδομένων είναι έτοιμη. Δημιουργήστε τον πρώτο διαχειριστή για ολοκλήρωση.",
        ["Enter an administrator username."] = "Εισαγάγετε όνομα χρήστη διαχειριστή.",
        ["The password must contain at least 8 characters."] = "Ο κωδικός πρέπει να περιέχει τουλάχιστον 8 χαρακτήρες.",
        ["The passwords do not match."] = "Οι κωδικοί δεν ταιριάζουν.",
        ["Database configuration is missing. Return to database setup."] = "Λείπει η ρύθμιση της βάσης δεδομένων. Επιστρέψτε στη ρύθμιση βάσης.",
        ["An account already exists. Close setup and sign in."] = "Υπάρχει ήδη λογαριασμός. Κλείστε τη ρύθμιση και συνδεθείτε.",
        ["Administrator created successfully. You can now sign in."] = "Ο διαχειριστής δημιουργήθηκε επιτυχώς. Μπορείτε τώρα να συνδεθείτε.",
        ["S Pharmacy Log — Licence activation"] = "S Pharmacy Log — Ενεργοποίηση άδειας",
        ["Activate S Pharmacy Log"] = "Ενεργοποίηση S Pharmacy Log",
        ["Enter the licence key issued for this PC."] = "Εισαγάγετε το κλειδί άδειας που εκδόθηκε για αυτόν τον υπολογιστή.",
        ["PC code"] = "Κωδικός υπολογιστή",
        ["Copy code"] = "Αντιγραφή κωδικού",
        ["Licence key"] = "Κλειδί άδειας",
        ["Load licence file…"] = "Φόρτωση αρχείου άδειας…",
        ["Activate"] = "Ενεργοποίηση",
        ["Exit"] = "Έξοδος",
        ["Close"] = "Κλείσιμο",
        ["Permanent"] = "Μόνιμη",
        ["Valid until"] = "Ισχύει έως",
        ["All modules"] = "Όλες οι ενότητες",
        ["Licensee:"] = "Κάτοχος άδειας:",
        ["Modules:"] = "Ενότητες:",
        ["A licence is required to continue."] = "Απαιτείται άδεια για να συνεχίσετε.",
        ["This licence belongs to a different PC."] = "Αυτή η άδεια ανήκει σε διαφορετικό υπολογιστή.",
        ["The licence has expired."] = "Η άδεια έχει λήξει.",
        ["The licence signature is invalid."] = "Η υπογραφή της άδειας δεν είναι έγκυρη.",
        ["The licence format is invalid."] = "Η μορφή της άδειας δεν είναι έγκυρη.",
        ["This licence does not include the SPharmacyLog module."] = "Αυτή η άδεια δεν περιλαμβάνει την ενότητα SPharmacyLog.",
        ["The licence does not include SPharmacyLog."] = "Η άδεια δεν περιλαμβάνει το SPharmacyLog.",
        ["Please activate this application."] = "Παρακαλώ ενεργοποιήστε αυτή την εφαρμογή.",
        ["PC code selected. Press Ctrl+C to copy it."] = "Ο κωδικός υπολογιστή επιλέχθηκε. Πατήστε Ctrl+C για αντιγραφή.",
        ["Load licence file"] = "Φόρτωση αρχείου άδειας",
        ["Licence files"] = "Αρχεία άδειας",
        ["Licence activated successfully."] = "Η άδεια ενεργοποιήθηκε επιτυχώς.",
        ["Activation failed:"] = "Αποτυχία ενεργοποίησης:",
        ["GS1 scan is incomplete or invalid:"] = "Η σάρωση GS1 είναι ελλιπής ή μη έγκυρη:",
        ["Payment - "] = "Πληρωμή - ",
        ["Connection failed:"] = "Αποτυχία σύνδεσης:",
        ["Connection test failed:"] = "Αποτυχία ελέγχου σύνδεσης:",
        ["Setup could not be completed:"] = "Δεν ήταν δυνατή η ολοκλήρωση της ρύθμισης:",
        [" is present but its value is empty."] = " υπάρχει αλλά η τιμή του είναι κενή.",
        [" exceeds the GS1 maximum of 20 characters."] = " υπερβαίνει το μέγιστο όριο των 20 χαρακτήρων GS1.",
        ["). This screen currently expects AIs 01, 10, 17 and 21."] = "). Αυτή η οθόνη αναμένει τα AI 01, 10, 17 και 21."
    };

    public string TranslateText(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;

        // Include both keyed application resources and literal UI phrases. This
        // lets controls created in one language switch back without rebuilding.
        var translations = English.Keys
            .Where(Greek.ContainsKey)
            .Select(key => new KeyValuePair<string, string>(English[key], Greek[key]))
            .Concat(UiGreek)
            .GroupBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(group => group.First())
            .ToList();

        if (CurrentLanguage == AppLanguage.Greek)
        {
            var exactGreek = translations.FirstOrDefault(pair => pair.Key == text);
            if (!string.IsNullOrEmpty(exactGreek.Key))
                return exactGreek.Value;
        }
        else
        {
            var exactEnglish = translations.FirstOrDefault(pair => pair.Value == text);
            if (!string.IsNullOrEmpty(exactEnglish.Key))
                return exactEnglish.Key;
        }

        // For composite text, translate phrases and labelled values only. This
        // avoids changing a customer's actual name, address or other entered data.
        var safePhrases = translations.Where(pair => IsSafePhrase(pair.Key));
        var normalized = text;
        foreach (var pair in safePhrases.OrderByDescending(pair => pair.Value.Length))
            normalized = normalized.Replace(pair.Value, pair.Key, StringComparison.Ordinal);

        if (CurrentLanguage == AppLanguage.English)
            return normalized;

        foreach (var pair in safePhrases.OrderByDescending(pair => pair.Key.Length))
            normalized = normalized.Replace(pair.Key, pair.Value, StringComparison.Ordinal);

        return normalized;
    }

    private static bool IsSafePhrase(string key) =>
        key.Length >= 8 ||
        key.Contains(' ') ||
        key.EndsWith(":", StringComparison.Ordinal) ||
        key.StartsWith("＋", StringComparison.Ordinal) ||
        key.All(char.IsUpper);

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
            CultureInfo.CurrentCulture = culture;
            CultureInfo.CurrentUICulture = culture;
        }
        catch { }
    }

    private sealed class LanguageSettings { public string Language { get; set; } = nameof(AppLanguage.English); }
}