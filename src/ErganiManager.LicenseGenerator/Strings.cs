namespace ErganiManager.LicenseGenerator;

/// <summary>The texts of the window in English and Greek.</summary>
internal static class Strings
{
    public static bool Greek { get; set; }

    private static readonly Dictionary<string, (string En, string Gr)> Texts = new()
    {
        ["title"]        = ("Ergani Manager - License generator", "Ergani Manager - Δημιουργία αδειών"),
        ["tab.new"]      = ("New license", "Νέα άδεια"),
        ["tab.history"]  = ("History", "Ιστορικό"),
        ["grp.key"]      = ("Signing key (private key)", "Κλειδί υπογραφής (ιδιωτικό κλειδί)"),
        ["btn.browse"]   = ("Browse…", "Αναζήτηση…"),
        ["btn.createKey"] = ("Create new key pair…", "Δημιουργία νέου ζεύγους κλειδιών…"),
        ["btn.showPublic"] = ("Show public key", "Εμφάνιση δημόσιου κλειδιού"),
        ["grp.license"]  = ("License", "Άδεια"),
        ["lbl.licensee"] = ("Customer / licensee", "Πελάτης / κάτοχος"),
        ["lbl.pc"]       = ("PC code (sent by the customer)", "Κωδικός υπολογιστή (από τον πελάτη)"),
        ["hint.pc"]      = ("like 1A2B-3C4D-5E6F-7A8B-9C0D (only for the license type \"Single PC\")", "π.χ. 1A2B-3C4D-5E6F-7A8B-9C0D (μόνο για τον τύπο άδειας «Μία συσκευή»)"),
        ["lbl.type"]     = ("License type", "Τύπος άδειας"),
        ["type.0"]       = ("Single PC - locked to one PC", "Μία συσκευή - δεμένη σε έναν υπολογιστή"),
        ["type.1"]       = ("Single user - one PC at a time (any PC)", "Ένας χρήστης - ένας υπολογιστής τη φορά (οποιοσδήποτε)"),
        ["type.2"]       = ("Multi-user - several PCs at the same time", "Πολλοί χρήστες - πολλοί υπολογιστές ταυτόχρονα"),
        ["type.3"]       = ("Unlimited PCs (site license)", "Απεριόριστοι υπολογιστές (άδεια χώρου)"),
        ["lbl.seats"]    = ("Number of PCs at the same time", "Αριθμός υπολογιστών ταυτόχρονα"),
        ["hint.shared"]  = ("The last three types need every PC to use the same shared database (SQL Server / MariaDB).", "Οι τρεις τελευταίοι τύποι απαιτούν όλοι οι υπολογιστές να χρησιμοποιούν την ίδια κοινή βάση δεδομένων (SQL Server / MariaDB)."),
        ["col.type"]     = ("Type", "Τύπος"),
        ["any"]          = ("any PC", "οποιοσδήποτε"),
        ["hist.single"]  = ("Single PC", "Μία συσκευή"),
        ["hist.floating"] = ("Single user", "Ένας χρήστης"),
        ["hist.multi"]   = ("Multi-user ({0} PCs)", "Πολλοί χρήστες ({0} υπολογιστές)"),
        ["hist.unlimited"] = ("Unlimited PCs", "Απεριόριστοι υπολογιστές"),
        ["lbl.valid"]    = ("Validity", "Διάρκεια"),
        ["rb.permanent"] = ("Permanent", "Μόνιμη"),
        ["rb.until"]     = ("Valid until", "Ισχύει έως"),
        ["lbl.modules"]  = ("Modules", "Ενότητες"),
        ["chk.all"]      = ("All modules", "Όλες οι ενότητες"),
        ["btn.generate"] = ("Create license key", "Δημιουργία κλειδιού άδειας"),
        ["grp.output"]   = ("License key", "Κλειδί άδειας"),
        ["btn.copy"]     = ("Copy", "Αντιγραφή"),
        ["btn.save"]     = ("Save as file…", "Αποθήκευση σε αρχείο…"),
        ["lbl.language"] = ("Language", "Γλώσσα"),
        ["mod.Schedules"] = ("Schedules", "Προγράμματα"),
        ["mod.WorkCards"] = ("Work cards (history, scan, today)", "Κάρτες εργασίας (ιστορικό, σάρωση, σήμερα)"),
        ["mod.Overtime"]  = ("Overtime", "Υπερωρίες"),
        ["mod.Reports"]   = ("Reports (incl. Excel export)", "Αναφορές (και εξαγωγή Excel)"),
        ["mod.Logs"]      = ("Logs (API log, email log)", "Αρχεία καταγραφής (API, email)"),
        ["mod.Terminal"]  = ("Terminal (clock-in for operators)", "Τερματικό (είσοδος εργαζομένων)"),
        ["col.date"]     = ("Created", "Δημιουργήθηκε"),
        ["col.licensee"] = ("Customer", "Πελάτης"),
        ["col.pc"]       = ("PC code", "Κωδικός υπολογιστή"),
        ["col.valid"]    = ("Valid until", "Ισχύει έως"),
        ["col.modules"]  = ("Modules", "Ενότητες"),
        ["permanent"]    = ("permanent", "μόνιμη"),
        ["all"]          = ("all", "όλες"),
        ["msg.created"]  = ("✅ License key created and saved in the history.", "✅ Το κλειδί άδειας δημιουργήθηκε και αποθηκεύτηκε στο ιστορικό."),
        ["msg.copied"]   = ("Copied to the clipboard.", "Αντιγράφηκε στο πρόχειρο."),
        ["msg.saved"]    = ("Saved: {0}", "Αποθηκεύτηκε: {0}"),
        ["err.nokey"]    = ("Choose the private key file first (or create a new key pair).", "Επιλέξτε πρώτα το αρχείο ιδιωτικού κλειδιού (ή δημιουργήστε νέο ζεύγος κλειδιών)."),
        ["err.licensee"] = ("Enter the customer name.", "Εισαγάγετε το όνομα του πελάτη."),
        ["err.pc"]       = ("The PC code must have 20 letters/digits, like 1A2B-3C4D-5E6F-7A8B-9C0D.", "Ο κωδικός υπολογιστή πρέπει να έχει 20 γράμματα/ψηφία, όπως 1A2B-3C4D-5E6F-7A8B-9C0D."),
        ["err.modules"]  = ("Select at least one module.", "Επιλέξτε τουλάχιστον μία ενότητα."),
        ["err.nokeyyet"] = ("There is no license key to copy or save yet.", "Δεν υπάρχει ακόμη κλειδί άδειας για αντιγραφή ή αποθήκευση."),
        ["ask.overwrite"] = ("This file already exists. A new key pair would replace your private key, and programs that contain the old public key would no longer accept your new licenses. Replace it?", "Αυτό το αρχείο υπάρχει ήδη. Ένα νέο ζεύγος κλειδιών θα αντικαταστήσει το ιδιωτικό σας κλειδί και τα προγράμματα που περιέχουν το παλιό δημόσιο κλειδί δεν θα δέχονται πλέον τις νέες άδειες. Να αντικατασταθεί;"),
        ["msg.keyCreated"] = ("Your private key was saved to:\n{0}\n\nKEEP IT SECRET and make a backup. Anyone who has it can create licenses; if you lose it, programs that are already shipped cannot accept new licenses.\n\nNext, the public key for the program is shown.", "Το ιδιωτικό σας κλειδί αποθηκεύτηκε στο:\n{0}\n\nΚΡΑΤΗΣΤΕ ΤΟ ΜΥΣΤΙΚΟ και κάντε αντίγραφο ασφαλείας. Όποιος το έχει μπορεί να δημιουργήσει άδειες· αν το χάσετε, τα προγράμματα που έχουν ήδη διανεμηθεί δεν μπορούν να δεχτούν νέες άδειες.\n\nΣτη συνέχεια εμφανίζεται το δημόσιο κλειδί για το πρόγραμμα."),
        ["pub.title"]    = ("Public key for the program", "Δημόσιο κλειδί για το πρόγραμμα"),
        ["pub.text"]     = ("Paste this line into src/ErganiManager.Licensing/LicenseKeys.cs (replace the existing PublicKeyBase64 line) and rebuild the application. This key is safe to ship.", "Επικολλήστε αυτή τη γραμμή στο src/ErganiManager.Licensing/LicenseKeys.cs (αντικαταστήστε την υπάρχουσα γραμμή PublicKeyBase64) και κάντε νέα μεταγλώττιση. Αυτό το κλειδί μπορεί να διανεμηθεί με ασφάλεια."),
        ["btn.close"]    = ("Close", "Κλείσιμο"),
        ["dlg.keyfile"]  = ("Private key file", "Αρχείο ιδιωτικού κλειδιού"),
        ["dlg.licfile"]  = ("License file", "Αρχείο άδειας"),
        ["dlg.all"]      = ("All files", "Όλα τα αρχεία"),
        ["err.generic"]  = ("Error: {0}", "Σφάλμα: {0}"),
    };

    public static string T(string key) =>
        Texts.TryGetValue(key, out var pair) ? (Greek ? pair.Gr : pair.En) : key;

    public static string F(string key, params object[] args) => string.Format(T(key), args);
}
