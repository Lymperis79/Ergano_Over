namespace ErganiManager.Core.Models;

/// <summary>One Ergani leave type (f_type of Documents/WTOLeave).</summary>
public sealed record LeaveTypeInfo(string Code, string Description, bool IsHourly)
{
    /// <summary>Text shown in the type list.</summary>
    public string Label => $"{Code} — {Description}";
}

/// <summary>The leave types accepted by Ergani, as listed in the Ergani II interoperability
/// specification (category "Αδειών"). "Άδεια" types are whole days; "Ωροάδεια" types (ΩΑ…)
/// are hourly and need a start/end time.</summary>
public static class LeaveTypes
{
    public static readonly IReadOnlyList<LeaveTypeInfo> All = new List<LeaveTypeInfo>
    {
        // ── Άδεια (whole day) ──
        new("ΑΔΚΑΝ",   "Κανονική άδεια", false),
        new("ΑΔΑΙΜ",   "Αιμοδοτική άδεια", false),
        new("ΑΔΕΞ",    "Άδεια εξετάσεων", false),
        new("ΑΔΑΑ",    "Άδεια άνευ αποδοχών", false),
        new("ΑΔΜΗ",    "Άδεια μητρότητας", false),
        new("ΑΔΠΠΜ",   "Ειδική παροχή προστασίας της μητρότητας", false),
        new("ΑΔΠΑ",    "Άδεια πατρότητας", false),
        new("ΑΔΦΠ",    "Άδεια φροντίδας παιδιού", false),
        new("ΑΔΓΟΝ",   "Γονική άδεια", false),
        new("ΑΔΦΡΟ",   "Άδεια φροντιστή", false),
        new("ΑΔΑΠΑΒ",  "Απουσία από την εργασία για λόγους ανωτέρας βίας", false),
        new("ΑΔΙΥΑ",   "Άδεια για υποβολή σε μεθόδους ιατρικώς υποβοηθούμενης αναπαραγωγής", false),
        new("ΑΔΠΕ",    "Άδεια εξετάσεων προγεννητικού ελέγχου", false),
        new("ΑΔΓΑΜ",   "Άδεια γάμου", false),
        new("ΑΔΣΝΠ",   "Άδεια λόγω σοβαρών νοσημάτων των παιδιών", false),
        new("ΑΔΝΠ",    "Άδεια λόγω νοσηλείας των παιδιών", false),
        new("ΑΔΜΟ",    "Άδεια μονογονεϊκών οικογενειών", false),
        new("ΑΔΠΣΕΤ",  "Άδεια παρακολούθησης σχολικής επίδοσης τέκνου", false),
        new("ΑΔΑΠΕΜ",  "Άδεια λόγω ασθένειας παιδιού ή άλλου εξαρτώμενου μέλους", false),
        new("ΑΔΑΠΣΚ",  "Απουσία από την εργασία λόγω επικείμενου σοβαρού κινδύνου βίας ή παρενόχλησης", false),
        new("ΑΔΑΣ",    "Άδεια ασθένειας (ανυπαίτιο κώλυμα παροχής εργασίας)", false),
        new("ΑΔΑΜΕΑ",  "Άδεια απουσίας Α.Μ.Ε.Α.", false),
        new("ΑΔΘΣΥΓ",  "Άδεια λόγω θανάτου συγγενούς", false),
        new("ΑΔΑΝΣΠ",  "Άδεια ανήλικων σπουδαστών", false),
        new("ΑΔΜΑΑ",   "Άδεια για μεταγγίσεις αίματος και των παραγώγων του ή αιμοκάθαρση", false),
        new("ΑΔΕΚΦ",   "Εκπαιδευτική άδεια για φοιτητές στο Κ.ΑΝ.Ε.Π. - Γ.Σ.Ε.Ε.", false),
        new("ΑΔΣΕΑΑ",  "Άδεια λόγω AIDS", false),
        new("ΑΔΕΡΕ",   "Ευέλικτες ρυθμίσεις εργασίας", false),
        new("ΑΔΑΛ",    "Άδεια Άλλη", false),

        // ── Ωροάδεια (hourly) ──
        new("ΩΑΦΠ",    "Άδεια φροντίδας παιδιού (ΩΡΕΣ)", true),
        new("ΩΑΓΟΝ",   "Γονική άδεια (ΩΡΕΣ)", true),
        new("ΩΑΑΠΑΒ",  "Απουσία από την εργασία για λόγους ανωτέρας βίας (ΩΡΕΣ)", true),
        new("ΩΑΕΡΕ",   "Ευέλικτες ρυθμίσεις εργασίας (ΩΡΕΣ)", true),
        new("ΩΑΠΕ",    "Άδεια εξετάσεων προγεννητικού ελέγχου (ΩΡΕΣ)", true),
        new("ΩΑΠΣΕΤ",  "Άδεια παρακολούθησης σχολικής επίδοσης τέκνου (ΩΡΕΣ)", true),
        new("ΩΑΑΛ",    "Άδεια Άλλη (ΩΡΕΣ)", true),
    };

    public static LeaveTypeInfo? Find(string? code) =>
        All.FirstOrDefault(t => string.Equals(t.Code, code, StringComparison.OrdinalIgnoreCase));

    /// <summary>Description for a stored code; falls back to the code itself.</summary>
    public static string Label(string? code) => Find(code)?.Label ?? code ?? string.Empty;
}
