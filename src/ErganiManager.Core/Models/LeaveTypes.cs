using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using ErganiManager.Data;
using ErganiManager.Data.Entities;

namespace ErganiManager.Core.Models;

/// <summary>One Ergani leave type (f_type of Documents/WTOLeave).</summary>
public sealed record LeaveTypeInfo(string Code, string Description, bool IsHourly, bool IsPaid = true)
{
    /// <summary>Text shown in the type list.</summary>
    public string Label => $"{Code} — {LocalizedDescription} [{ErganiManager.Data.UiText.T(IsPaid ? "Paid" : "Unpaid")}]";

    /// <summary>The official Greek name, or its English translation when the app language is English.</summary>
    public string LocalizedDescription => ErganiManager.Data.UiText.T(Description);
}

/// <summary>The leave types accepted by Ergani, as listed in the Ergani II interoperability
/// specification (category "Αδειών"). "Άδεια" types are whole days; "Ωροάδεια" types (ΩΑ…)
/// are hourly and need a start/end time.</summary>
public static class LeaveTypes
{
    private static readonly object Gate = new();
    private static IReadOnlyList<LeaveTypeInfo>? _configured;
    private static DateTime _lastRefreshUtc = DateTime.MinValue;
    private static Func<DbContextOptions<AppDbContext>>? _dbOptionsFactory;
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(30);
    private static string ConfigPath => Path.Combine(ErganiManager.LocalCache.AppPaths.GetAppDataFolder(), "leave-types.json");

    /// <summary>Connects leave types to the configured main database. The JSON file is only an offline cache.</summary>
    public static void ConfigureDatabase(Func<DbContextOptions<AppDbContext>> dbOptionsFactory)
    {
        lock (Gate)
        {
            _dbOptionsFactory = dbOptionsFactory;
            _lastRefreshUtc = DateTime.MinValue;
        }
    }

    /// <summary>Forces a fresh read from the central database, falling back to the local cache when offline.</summary>
    public static void Refresh()
    {
        lock (Gate)
        {
            _lastRefreshUtc = DateTime.MinValue;
            RefreshFromDatabase();
        }
    }

    public static IReadOnlyList<LeaveTypeInfo> All
    {
        get
        {
            lock (Gate)
            {
                if (_configured == null || DateTime.UtcNow - _lastRefreshUtc >= RefreshInterval)
                    RefreshFromDatabase();
                return _configured ??= ReadLocalCacheOrDefaults();
            }
        }
    }

    private static void RefreshFromDatabase()
    {
        if (_dbOptionsFactory == null) return;
        try
        {
            using var db = new AppDbContext(_dbOptionsFactory());
            var rows = db.LeaveTypeConfigurations.AsNoTracking().OrderBy(t => t.Code).ToList();
            if (rows.Count == 0)
            {
                // One-time migration: preserve any existing local JSON configuration when the
                // central table is first created; otherwise seed the official defaults.
                var seed = ReadLocalCacheOrDefaults();
                db.LeaveTypeConfigurations.AddRange(seed.Select(t => new LeaveTypeConfiguration
                {
                    Code = t.Code,
                    Description = t.Description,
                    IsHourly = t.IsHourly,
                    IsPaid = t.IsPaid,
                    UpdatedAtUtc = DateTime.UtcNow
                }));
                db.SaveChanges();
                rows = db.LeaveTypeConfigurations.AsNoTracking().OrderBy(t => t.Code).ToList();
            }

            _configured = rows.Select(t => new LeaveTypeInfo(t.Code, t.Description, t.IsHourly, t.IsPaid)).ToList();
            _lastRefreshUtc = DateTime.UtcNow;
            WriteLocalCache(_configured);
        }
        catch
        {
            // Keep the last known cache for offline mode. A failed refresh must never
            // replace valid central settings with local defaults.
            _lastRefreshUtc = DateTime.UtcNow;
            _configured ??= ReadLocalCacheOrDefaults();
        }
    }

    private static IReadOnlyList<LeaveTypeInfo> ReadLocalCacheOrDefaults()
    {
        try
        {
            if (File.Exists(ConfigPath))
            {
                var loaded = System.Text.Json.JsonSerializer.Deserialize<List<LeaveTypeInfo>>(File.ReadAllText(ConfigPath));
                if (loaded is { Count: > 0 }) return loaded;
            }
        }
        catch { /* use built-in defaults if the offline cache is unreadable */ }
        return Defaults();
    }

    private static void WriteLocalCache(IEnumerable<LeaveTypeInfo> types)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath)!);
        File.WriteAllText(ConfigPath, System.Text.Json.JsonSerializer.Serialize(types, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
    }

    private static List<LeaveTypeInfo> Defaults() => new List<LeaveTypeInfo>
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

        // ── Ωροάδεια (hourly) ──
        new("ΩΑΦΠ",    "Άδεια φροντίδας παιδιού (ΩΡΕΣ)", true),
        new("ΩΑΓΟΝ",   "Γονική άδεια (ΩΡΕΣ)", true),
        new("ΩΑΑΠΑΒ",  "Απουσία από την εργασία για λόγους ανωτέρας βίας (ΩΡΕΣ)", true),
        new("ΩΑΕΡΕ",   "Ευέλικτες ρυθμίσεις εργασίας (ΩΡΕΣ)", true),
        new("ΩΑΠΕ",    "Άδεια εξετάσεων προγεννητικού ελέγχου (ΩΡΕΣ)", true),
        new("ΩΑΠΣΕΤ",  "Άδεια παρακολούθησης σχολικής επίδοσης τέκνου (ΩΡΕΣ)", true),
        new("ΩΑΑΛ",    "Άδεια Άλλη (ΩΡΕΣ)", true),
    };

    public static void Save(IEnumerable<LeaveTypeInfo> types)
    {
        var list = types.ToList();
        if (list.Count == 0) throw new InvalidOperationException(ErganiManager.Data.UiText.T("At least one leave type is required."));
        if (list.Any(t => string.IsNullOrWhiteSpace(t.Code) || t.Code.Trim().Length > 10 || string.IsNullOrWhiteSpace(t.Description) || t.Description.Trim().Length > 200))
            throw new InvalidOperationException(ErganiManager.Data.UiText.T("Every leave type needs a code (up to 10 characters) and a description (up to 200 characters)."));
        if (list.GroupBy(t => t.Code.Trim(), StringComparer.OrdinalIgnoreCase).Any(g => g.Count() > 1))
            throw new InvalidOperationException(ErganiManager.Data.UiText.T("Leave type codes must be unique."));

        list = list.Select(t => t with { Code = t.Code.Trim(), Description = t.Description.Trim() }).ToList();
        lock (Gate)
        {
            if (_dbOptionsFactory == null)
                throw new InvalidOperationException(ErganiManager.Data.UiText.T("The central database is not configured. Leave types cannot be saved yet."));

            // Central database is authoritative. Do not report a successful save if only
            // this PC's cache could be written.
            using var db = new AppDbContext(_dbOptionsFactory());
            using var transaction = db.Database.BeginTransaction();
            db.LeaveTypeConfigurations.RemoveRange(db.LeaveTypeConfigurations);
            db.LeaveTypeConfigurations.AddRange(list.Select(t => new LeaveTypeConfiguration
            {
                Code = t.Code,
                Description = t.Description,
                IsHourly = t.IsHourly,
                IsPaid = t.IsPaid,
                UpdatedAtUtc = DateTime.UtcNow
            }));
            db.SaveChanges();
            transaction.Commit();

            _configured = list;
            _lastRefreshUtc = DateTime.UtcNow;
            WriteLocalCache(list);
        }
    }

    public static LeaveTypeInfo? Find(string? code) =>
        All.FirstOrDefault(t => string.Equals(t.Code, code, StringComparison.OrdinalIgnoreCase));

    /// <summary>Description for a stored code; falls back to the code itself.</summary>
    public static string Label(string? code) => Find(code)?.Label ?? code ?? string.Empty;
}
