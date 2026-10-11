using System.Globalization;
using System.Text.Json.Serialization;

namespace ErganiManager.ErganiApi.Models;

// ── Domain objects used by the app (NOT sent to Ergani as-is) ────────────────

/// <summary>One employee/day of leave.</summary>
public class LeaveEntry
{
    public string EmployeeTaxIdentificationNumber { get; set; } = string.Empty;
    public string EmployeeLastName { get; set; } = string.Empty;
    public string EmployeeFirstName { get; set; } = string.Empty;
    public DateOnly Date { get; set; }

    /// <summary>f_type — e.g. ΑΔΚΑΝ, ΩΑΓΟΝ.</summary>
    public string TypeCode { get; set; } = string.Empty;

    /// <summary>Only for hourly (ΩΑ…) types.</summary>
    public TimeOnly? From { get; set; }
    public TimeOnly? To { get; set; }

    public int? ReferenceYear { get; set; }
    public int? EntitledDays { get; set; }
}

/// <summary>Leave of one branch. Converted to the WTOLeave body by <see cref="LeaveRequestBody.From"/>.</summary>
public class CompanyLeaveSubmission
{
    public int BusinessBranchNumber { get; set; }
    public string Comments { get; set; } = string.Empty;
    public List<LeaveEntry> Entries { get; set; } = new();
}

// ── Wire format of Documents/WTOLeave ────────────────────────────────────────
// Same WTOS envelope as WTOOv. Per employee and day, ErgazomenosWTOAnalytics carries
// { f_type, f_from, f_to, f_year, f_req_days }. f_from/f_to/f_year/f_req_days accept a blank value.

public class LeaveRequestBody
{
    [JsonPropertyName("WTOS")]
    public WtosNode Wtos { get; set; } = new();

    public static LeaveRequestBody From(IEnumerable<CompanyLeaveSubmission> submissions)
    {
        var inv  = CultureInfo.InvariantCulture;
        var body = new LeaveRequestBody();

        foreach (var s in submissions)
        {
            if (s.Entries.Count == 0) continue;

            var wto = new WtoItem
            {
                BranchNumber = s.BusinessBranchNumber.ToString(inv),
                Comments     = s.Comments ?? string.Empty,
                FromDate     = s.Entries.Min(e => e.Date).ToString("dd/MM/yyyy", inv),
                ToDate       = s.Entries.Max(e => e.Date).ToString("dd/MM/yyyy", inv)
            };

            // One ErgazomenoiWTO per employee + day.
            foreach (var g in s.Entries.GroupBy(e => (e.EmployeeTaxIdentificationNumber, e.Date)).OrderBy(g => g.Key.Date))
            {
                var first = g.First();
                wto.Ergazomenoi.Employees.Add(new ErgazomenosWto
                {
                    Afm       = first.EmployeeTaxIdentificationNumber,
                    LastName  = first.EmployeeLastName,
                    FirstName = first.EmployeeFirstName,
                    Date      = first.Date.ToString("dd/MM/yyyy", inv),
                    Analytics = new AnalyticsNode
                    {
                        Items = g.Select(e => new AnalyticsItem
                        {
                            Type         = e.TypeCode,
                            From         = e.From?.ToString("HH:mm", inv) ?? string.Empty,
                            To           = e.To?.ToString("HH:mm", inv) ?? string.Empty,
                            Year         = e.ReferenceYear?.ToString("D4", inv) ?? string.Empty,
                            RequiredDays = e.EntitledDays?.ToString("D3", inv) ?? string.Empty
                        }).ToList()
                    }
                });
            }

            body.Wtos.Items.Add(wto);
        }

        return body;
    }
}
