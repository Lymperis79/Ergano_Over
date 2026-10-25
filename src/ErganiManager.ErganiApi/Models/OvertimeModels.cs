using System.Globalization;
using System.Text.Json.Serialization;

namespace ErganiManager.ErganiApi.Models;

public enum ApiOvertimeJustification
{
    ACCIDENT_PREVENTION_OR_DAMAGE_RESTORATION,
    URGENT_SEASONAL_TASKS,
    EXCEPTIONAL_WORKLOAD,
    SUPPLEMENTARY_TASKS,
    LOST_HOURS_SUDDEN_CAUSES,
    LOST_HOURS_OFFICIAL_HOLIDAYS,
    LOST_HOURS_WEATHER_CONDITIONS,
    EMERGENCY_CLOSURE_DAY,
    NON_WORKDAY_TASKS
}

// ── Domain objects used by the app (NOT sent to Ergani as-is) ────────────────

/// <summary>One employee/day overtime interval.</summary>
public class OvertimeEntry
{
    public string EmployeeTaxIdentificationNumber { get; set; } = string.Empty;
    public string EmployeeSocialSecurityNumber { get; set; } = string.Empty;
    public string EmployeeLastName { get; set; } = string.Empty;
    public string EmployeeFirstName { get; set; } = string.Empty;
    public DateOnly OvertimeDate { get; set; }
    public TimeOnly StartTime { get; set; }
    public TimeOnly EndTime { get; set; }
    public bool Cancellation { get; set; }
    public string EmployeeProfessionCode { get; set; } = string.Empty;
    public ApiOvertimeJustification Justification { get; set; }
    public int WeeklyWorkdaysNumber { get; set; }
    public string? AseeApproval { get; set; }
}

/// <summary>Overtime of one branch (what the app builds). Converted to the WTOOv body by <see cref="OvertimeRequestBody.From"/>.</summary>
public class CompanyOvertimeSubmission
{
    public int BusinessBranchNumber { get; set; }
    public string SepeServiceCode { get; set; } = string.Empty;
    public string BusinessPrimaryActivityCode { get; set; } = string.Empty;
    public string BusinessBranchActivityCode { get; set; } = string.Empty;
    public string KallikratisMunicipalCode { get; set; } = string.Empty;
    public string LegalRepresentativeTaxIdentificationNumber { get; set; } = string.Empty;
    public string Comments { get; set; } = string.Empty;
    public List<OvertimeEntry> EmployeeOvertimes { get; set; } = new();
}

// ── Wire format of Documents/WTOOv ───────────────────────────────────────────
// { "WTOS": { "WTO": [ { header..., "Ergazomenoi": { "ErgazomenoiWTO": [
//     { f_afm, f_eponymo, f_onoma, f_date,
//       "ErgazomenosAnalytics": { "ErgazomenosWTOAnalytics": [ { f_type, f_from, f_to } ] } } ] } } ] } }

public class OvertimeRequestBody
{
    /// <summary>
    /// f_type sent for an overtime interval. Ergani rejected "ΥΠΕ" ("code not found"); the codes
    /// known for work-time analytics are "ΥΠ" (work) and "ΤΗΛ" (telework).
    /// </summary>
    public const string OvertimeIntervalType = "ΥΠ";

    [JsonPropertyName("WTOS")]
    public WtosNode Wtos { get; set; } = new();

    public static OvertimeRequestBody From(IEnumerable<CompanyOvertimeSubmission> submissions)
    {
        var inv = CultureInfo.InvariantCulture;
        var body = new OvertimeRequestBody();

        foreach (var s in submissions)
        {
            var entries = s.EmployeeOvertimes.Where(e => !e.Cancellation).ToList();
            if (entries.Count == 0) continue;

            var from = entries.Min(e => e.OvertimeDate);
            var to   = entries.Max(e => e.OvertimeDate);

            var wto = new WtoItem
            {
                BranchNumber = s.BusinessBranchNumber.ToString(inv),
                Comments     = s.Comments ?? string.Empty,
                FromDate     = from.ToString("dd/MM/yyyy", inv),
                ToDate       = to.ToString("dd/MM/yyyy", inv)
            };

            // One ErgazomenoiWTO per employee+day, each with its overtime intervals.
            foreach (var g in entries.GroupBy(e => (e.EmployeeTaxIdentificationNumber, e.OvertimeDate)))
            {
                var first = g.First();
                wto.Ergazomenoi.Employees.Add(new ErgazomenosWto
                {
                    Afm      = first.EmployeeTaxIdentificationNumber,
                    LastName = first.EmployeeLastName,
                    FirstName = first.EmployeeFirstName,
                    Date     = first.OvertimeDate.ToString("dd/MM/yyyy", inv),
                    Analytics = new AnalyticsNode
                    {
                        Items = g.OrderBy(e => e.StartTime).Select(e => new AnalyticsItem
                        {
                            Type = OvertimeIntervalType,
                            From = e.StartTime.ToString("HH:mm", inv),
                            To   = e.EndTime.ToString("HH:mm", inv)
                        }).ToList()
                    }
                });
            }
            body.Wtos.Items.Add(wto);
        }
        return body;
    }
}

public class WtosNode
{
    [JsonPropertyName("WTO")] public List<WtoItem> Items { get; set; } = new();
}

public class WtoItem
{
    [JsonPropertyName("f_aa_pararthmatos")] public string BranchNumber { get; set; } = string.Empty;
    [JsonPropertyName("f_rel_protocol")]    public string RelatedProtocol { get; set; } = string.Empty;
    [JsonPropertyName("f_rel_date")]        public string RelatedDate { get; set; } = string.Empty;
    [JsonPropertyName("f_comments")]        public string Comments { get; set; } = string.Empty;
    [JsonPropertyName("f_from_date")]       public string FromDate { get; set; } = string.Empty;
    [JsonPropertyName("f_to_date")]         public string ToDate { get; set; } = string.Empty;
    [JsonPropertyName("Ergazomenoi")]       public ErgazomenoiNode Ergazomenoi { get; set; } = new();
}

public class ErgazomenoiNode
{
    [JsonPropertyName("ErgazomenoiWTO")] public List<ErgazomenosWto> Employees { get; set; } = new();
}

public class ErgazomenosWto
{
    [JsonPropertyName("f_afm")]     public string Afm { get; set; } = string.Empty;
    [JsonPropertyName("f_eponymo")] public string LastName { get; set; } = string.Empty;
    [JsonPropertyName("f_onoma")]   public string FirstName { get; set; } = string.Empty;
    [JsonPropertyName("f_date")]    public string Date { get; set; } = string.Empty;
    [JsonPropertyName("ErgazomenosAnalytics")] public AnalyticsNode Analytics { get; set; } = new();
}

public class AnalyticsNode
{
    [JsonPropertyName("ErgazomenosWTOAnalytics")] public List<AnalyticsItem> Items { get; set; } = new();
}

public class AnalyticsItem
{
    [JsonPropertyName("f_type")] public string Type { get; set; } = string.Empty;
    [JsonPropertyName("f_from")] public string From { get; set; } = string.Empty;
    [JsonPropertyName("f_to")]   public string To { get; set; } = string.Empty;

    // WTOLeave only — left null (and therefore omitted) for overtime and schedules.
    [JsonPropertyName("f_year")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Year { get; set; }

    [JsonPropertyName("f_req_days")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? RequiredDays { get; set; }
}
