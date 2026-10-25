using System.Globalization;
using System.Text.Json.Serialization;

namespace ErganiManager.ErganiApi.Models;

// Application/domain models. These are converted to the exact Ergani WTO wire format below.
public class WorkdayDetails
{
    public string WorkDayType { get; set; } = string.Empty;
    public TimeOnly StartTime { get; set; }
    public TimeOnly EndTime { get; set; }
}

public class EmployeeDailySchedule
{
    public string EmployeeTaxIdentificationNumber { get; set; } = string.Empty;
    public string EmployeeLastName { get; set; } = string.Empty;
    public string EmployeeFirstName { get; set; } = string.Empty;
    public DateOnly ScheduleDate { get; set; }
    public List<WorkdayDetails> WorkdayDetails { get; set; } = new();
}

public class CompanyDailyScheduleSubmission
{
    public string EmployerTaxIdentificationNumber { get; set; } = string.Empty;
    public int BusinessBranchNumber { get; set; }
    public string SepeServiceCode { get; set; } = string.Empty;
    public string BusinessPrimaryActivityCode { get; set; } = string.Empty;
    public string BusinessBranchActivityCode { get; set; } = string.Empty;
    public string KallikratisMunicipalCode { get; set; } = string.Empty;
    public string Comments { get; set; } = string.Empty;
    public List<EmployeeDailySchedule> EmployeeSchedules { get; set; } = new();
}

public class EmployeeWeeklySchedule
{
    public string EmployeeTaxIdentificationNumber { get; set; } = string.Empty;
    public string EmployeeLastName { get; set; } = string.Empty;
    public string EmployeeFirstName { get; set; } = string.Empty;

    // WTOWeek f_day is an enumeration: 0=Sunday ... 6=Saturday.
    // Nullable in the input model so a missing selection can be rejected before upload.
    public int? ScheduleDay { get; set; }

    public List<WorkdayDetails> WorkdayDetails { get; set; } = new();
}

public class CompanyWeeklyScheduleSubmission
{
    public string EmployerTaxIdentificationNumber { get; set; } = string.Empty;
    public int BusinessBranchNumber { get; set; }
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public string Comments { get; set; } = string.Empty;
    public List<EmployeeWeeklySchedule> EmployeeSchedules { get; set; } = new();
}

// -----------------------------------------------------------------------------
// Exact wire format required by Documents/WTODaily and Documents/WTOWeek.
// -----------------------------------------------------------------------------

public sealed class WtoScheduleRequest
{
    [JsonPropertyName("WTOS")]
    public WtosScheduleNode Wtos { get; set; } = new();
}

public sealed class WtosScheduleNode
{
    [JsonPropertyName("WTO")]
    public List<WtoScheduleItem> Items { get; set; } = new();
}

public sealed class WtoScheduleItem
{
    [JsonPropertyName("f_aa_pararthmatos")]
    public string BranchNumber { get; set; } = string.Empty;

    [JsonPropertyName("f_rel_protocol")]
    public string RelatedProtocol { get; set; } = string.Empty;

    [JsonPropertyName("f_rel_date")]
    public string RelatedDate { get; set; } = string.Empty;

    [JsonPropertyName("f_comments")]
    public string Comments { get; set; } = string.Empty;

    [JsonPropertyName("f_from_date")]
    public string FromDate { get; set; } = string.Empty;

    [JsonPropertyName("f_to_date")]
    public string ToDate { get; set; } = string.Empty;

    [JsonPropertyName("Ergazomenoi")]
    public object Employees { get; set; } = new WtoEmployeesNode();
}

public sealed class WtoEmployeesNode
{
    [JsonPropertyName("ErgazomenoiWTO")]
    public List<WtoEmployee> Items { get; set; } = new();
}

public sealed class WtoWeeklyEmployeesNode
{
    [JsonPropertyName("ErgazomenoiWTO")]
    public List<WtoWeeklyEmployee> Items { get; set; } = new();
}

public sealed class WtoEmployee
{
    [JsonPropertyName("f_afm")] public string Afm { get; set; } = string.Empty;
    [JsonPropertyName("f_eponymo")] public string LastName { get; set; } = string.Empty;
    [JsonPropertyName("f_onoma")] public string FirstName { get; set; } = string.Empty;
    [JsonPropertyName("f_date")] public string Date { get; set; } = string.Empty;
    [JsonPropertyName("ErgazomenosAnalytics")] public WtoAnalyticsNode Analytics { get; set; } = new();
}

public sealed class WtoWeeklyEmployee
{
    [JsonPropertyName("f_afm")] public string Afm { get; set; } = string.Empty;
    [JsonPropertyName("f_eponymo")] public string LastName { get; set; } = string.Empty;
    [JsonPropertyName("f_onoma")] public string FirstName { get; set; } = string.Empty;
    // Numeric enumeration: 0=Sunday, 1=Monday, ... 6=Saturday.
    [JsonPropertyName("f_day")] public int Day { get; set; }
    [JsonPropertyName("ErgazomenosAnalytics")] public WtoAnalyticsNode Analytics { get; set; } = new();
}

public sealed class WtoAnalyticsNode
{
    [JsonPropertyName("ErgazomenosWTOAnalytics")]
    public List<WtoAnalyticsItem> Items { get; set; } = new();
}

public sealed class WtoAnalyticsItem
{
    [JsonPropertyName("f_type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("f_from")]
    public string From { get; set; } = string.Empty;

    [JsonPropertyName("f_to")]
    public string To { get; set; } = string.Empty;
}

public static class WtoScheduleRequestBuilder
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public static WtoScheduleRequest BuildDaily(IEnumerable<CompanyDailyScheduleSubmission> submissions)
    {
        var request = new WtoScheduleRequest();

        foreach (var submission in submissions)
        {
            var employees = new WtoEmployeesNode();

            foreach (var employee in submission.EmployeeSchedules)
            {
                employees.Items.Add(new WtoEmployee
                {
                    Afm = employee.EmployeeTaxIdentificationNumber,
                    LastName = employee.EmployeeLastName,
                    FirstName = employee.EmployeeFirstName,
                    Date = employee.ScheduleDate.ToString("dd/MM/yyyy", Inv),
                    Analytics = BuildAnalytics(employee.WorkdayDetails)
                });
            }

            request.Wtos.Items.Add(new WtoScheduleItem
            {
                BranchNumber = submission.BusinessBranchNumber.ToString(Inv),
                RelatedProtocol = string.Empty,
                RelatedDate = string.Empty,
                Comments = submission.Comments ?? string.Empty,
                FromDate = submission.EmployeeSchedules.Count == 0
                    ? string.Empty
                    : submission.EmployeeSchedules.Min(x => x.ScheduleDate).ToString("dd/MM/yyyy", Inv),
                ToDate = submission.EmployeeSchedules.Count == 0
                    ? string.Empty
                    : submission.EmployeeSchedules.Max(x => x.ScheduleDate).ToString("dd/MM/yyyy", Inv),
                Employees = employees
            });
        }

        return request;
    }

    public static WtoScheduleRequest BuildWeekly(IEnumerable<CompanyWeeklyScheduleSubmission> submissions)
    {
        var request = new WtoScheduleRequest();

        foreach (var submission in submissions)
        {
            var employees = new WtoWeeklyEmployeesNode();

            foreach (var employee in submission.EmployeeSchedules)
            {
                if (employee.ScheduleDay is not int day || day < 0 || day > 6)
                {
                    throw new ArgumentOutOfRangeException(
                        nameof(employee.ScheduleDay), employee.ScheduleDay,
                        $"Invalid weekly schedule day for employee {employee.EmployeeTaxIdentificationNumber}. " +
                        "f_day must be an integer from 0 (Sunday) through 6 (Saturday).");
                }

                employees.Items.Add(new WtoWeeklyEmployee
                {
                    Afm = employee.EmployeeTaxIdentificationNumber,
                    LastName = employee.EmployeeLastName,
                    FirstName = employee.EmployeeFirstName,
                    Day = day,
                    Analytics = BuildAnalytics(employee.WorkdayDetails)
                });
            }

            request.Wtos.Items.Add(new WtoScheduleItem
            {
                BranchNumber = submission.BusinessBranchNumber.ToString(Inv),
                RelatedProtocol = string.Empty,
                RelatedDate = string.Empty,
                Comments = submission.Comments ?? string.Empty,
                FromDate = submission.StartDate.ToString("dd/MM/yyyy", Inv),
                ToDate = submission.EndDate.ToString("dd/MM/yyyy", Inv),
                Employees = employees
            });
        }

        return request;
    }

    private static WtoAnalyticsNode BuildAnalytics(IEnumerable<WorkdayDetails> details)
    {
        return new WtoAnalyticsNode
        {
            Items = details.Select(d => new WtoAnalyticsItem
            {
                Type = d.WorkDayType,
                From = d.StartTime.ToString("HH:mm", Inv),
                To = d.EndTime.ToString("HH:mm", Inv)
            }).ToList()
        };
    }
}
