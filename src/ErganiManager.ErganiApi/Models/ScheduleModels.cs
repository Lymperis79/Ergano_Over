using System.Text.Json.Serialization;

namespace ErganiManager.ErganiApi.Models;

public class WorkdayDetails
{
    [JsonPropertyName("f_type")]
    public string WorkDayType { get; set; } = string.Empty; // WORK_FROM_OFFICE / WORK_FROM_HOME / etc.

    [JsonPropertyName("f_from")]
    public TimeOnly StartTime { get; set; }

    [JsonPropertyName("f_to")]
    public TimeOnly EndTime { get; set; }
}

public class EmployeeDailySchedule
{
    [JsonPropertyName("f_afm")]
    public string EmployeeTaxIdentificationNumber { get; set; } = string.Empty;

    [JsonPropertyName("f_eponymo")]
    public string EmployeeLastName { get; set; } = string.Empty;

    [JsonPropertyName("f_onoma")]
    public string EmployeeFirstName { get; set; } = string.Empty;

    [JsonPropertyName("f_date")]
    public DateOnly ScheduleDate { get; set; }

    [JsonPropertyName("workday_details")]
    public List<WorkdayDetails> WorkdayDetails { get; set; } = new();
}

public class CompanyDailyScheduleSubmission
{
    [JsonPropertyName("f_afm_ergodoti")]
    public string EmployerTaxIdentificationNumber { get; set; } = string.Empty;

    [JsonPropertyName("f_aa")]
    public int BusinessBranchNumber { get; set; }

    [JsonPropertyName("f_sepe_service_code")]
    public string SepeServiceCode { get; set; } = string.Empty;

    [JsonPropertyName("f_business_primary_activity_code")]
    public string BusinessPrimaryActivityCode { get; set; } = string.Empty;

    [JsonPropertyName("f_business_branch_activity_code")]
    public string BusinessBranchActivityCode { get; set; } = string.Empty;

    [JsonPropertyName("f_kallikratis_municipal_code")]
    public string KallikratisMunicipalCode { get; set; } = string.Empty;

    [JsonPropertyName("f_comments")]
    public string Comments { get; set; } = string.Empty;

    [JsonPropertyName("employee_schedules")]
    public List<EmployeeDailySchedule> EmployeeSchedules { get; set; } = new();
}

public class EmployeeWeeklySchedule
{
    [JsonPropertyName("f_afm")]
    public string EmployeeTaxIdentificationNumber { get; set; } = string.Empty;

    [JsonPropertyName("f_eponymo")]
    public string EmployeeLastName { get; set; } = string.Empty;

    [JsonPropertyName("f_onoma")]
    public string EmployeeFirstName { get; set; } = string.Empty;

    [JsonPropertyName("f_date")]
    public DateOnly ScheduleDate { get; set; }

    [JsonPropertyName("workday_details")]
    public List<WorkdayDetails> WorkdayDetails { get; set; } = new();
}

public class CompanyWeeklyScheduleSubmission
{
    [JsonPropertyName("f_afm_ergodoti")]
    public string EmployerTaxIdentificationNumber { get; set; } = string.Empty;

    [JsonPropertyName("f_aa")]
    public int BusinessBranchNumber { get; set; }

    [JsonPropertyName("f_start_date")]
    public DateOnly StartDate { get; set; }

    [JsonPropertyName("f_end_date")]
    public DateOnly EndDate { get; set; }

    [JsonPropertyName("employee_schedules")]
    public List<EmployeeWeeklySchedule> EmployeeSchedules { get; set; } = new();
}
