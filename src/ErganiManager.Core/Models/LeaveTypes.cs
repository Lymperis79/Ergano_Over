using System;
using System.Collections.Generic;

namespace ErganiManager.Core.Models;

public sealed record LeaveTypeInfo(string Code, string Description, bool IsHourly, bool IsPaid = true, string PaymentCategory = "")
{
    public string EffectivePaymentCategory => string.IsNullOrWhiteSpace(PaymentCategory)
        ? LeaveTypes.DefaultPaymentCategory(Code, IsPaid)
        : PaymentCategory;

    public string LocalizedPaymentCategory => ErganiManager.Data.UiText.T(EffectivePaymentCategory switch
    {
        "Subsidized" => "Subsidized (DYPA / e-EFKA)",
        "Unpaid" => "Unpaid",
        _ => "Employer-paid"
    });

    /// <summary>Text shown in the type list.</summary>
    public string Label => $"{Code} — {LocalizedDescription} [{LocalizedPaymentCategory}]";

    /// <summary>The official Greek name, or its English translation when the app language is English.</summary>
    public string LocalizedDescription => ErganiManager.Data.UiText.T(Description);
}

/// <summary>Built-in defaults and classification rules for Ergani leave types.</summary>
public static class LeaveTypes
{
internal static List<LeaveTypeInfo> CreateDefaults() => new List<LeaveTypeInfo>
    {
        // ── Άδεια (whole day) ──
        new("ΑΔΚΑΝ",   "Κανονική άδεια", false),
        new("ΑΔΑΙΜ",   "Αιμοδοτική άδεια", false),
        new("ΑΔΕΞ",    "Άδεια εξετάσεων", false),
        new("ΑΔΑΑ",    "Άδεια άνευ αποδοχών", false, false),
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

        // New official Ergani leave types
        new("ΩΑΜΓΤΑ", "Μειωμένο ωράριο γονέων παιδιών με Αναπηρία (Ώρες)", true),
        new("ΑΔΠΓ",   "Άδεια πενθούντων γονέων (Ημέρες)", false),
        new("ΑΔΓΕ",   "Άδεια γυναικολογικού ελέγχου (Ημέρες)", false),
        new("ΑΔΕΚΛ",  "Εκλογική Άδεια (Ημέρες)", false),

        // ── Ωροάδεια (hourly) ──
        new("ΩΑΦΠ",    "Άδεια φροντίδας παιδιού (ΩΡΕΣ)", true),
        new("ΩΑΓΟΝ",   "Γονική άδεια (ΩΡΕΣ)", true),
        new("ΩΑΑΠΑΒ",  "Απουσία από την εργασία για λόγους ανωτέρας βίας (ΩΡΕΣ)", true),
        new("ΩΑΕΡΕ",   "Ευέλικτες ρυθμίσεις εργασίας (ΩΡΕΣ)", true),
        new("ΩΑΠΕ",    "Άδεια εξετάσεων προγεννητικού ελέγχου (ΩΡΕΣ)", true),
        new("ΩΑΠΣΕΤ",  "Άδεια παρακολούθησης σχολικής επίδοσης τέκνου (ΩΡΕΣ)", true),
        new("ΩΑΑΛ",    "Άδεια Άλλη (ΩΡΕΣ)", true),
    };

public static string DefaultPaymentCategory(string? code, bool legacyIsPaid = true)
    {
        var normalized = (code ?? string.Empty).Trim().ToUpperInvariant();
        if (new[] { "ΑΔΑΑ", "ΑΔΦΡΟ", "ΑΔΝΠ" }.Contains(normalized)) return "Unpaid";
        if (new[] { "ΑΔΜΗ", "ΑΔΠΠΜ", "ΑΔΓΟΝ", "ΑΔΑΣ" }.Contains(normalized)) return "Subsidized";
        return legacyIsPaid ? "EmployerPaid" : "Unpaid";
    }
}
