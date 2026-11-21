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