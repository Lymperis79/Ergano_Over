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

public class OvertimeEntry
{
    [JsonPropertyName("f_afm")]
    public string EmployeeTaxIdentificationNumber { get; set; } = string.Empty;

    [JsonPropertyName("f_amka")]
    public string EmployeeSocialSecurityNumber { get; set; } = string.Empty;

    [JsonPropertyName("f_eponymo")]
    public string EmployeeLastName { get; set; } = string.Empty;

    [JsonPropertyName("f_onoma")]
    public string EmployeeFirstName { get; set; } = string.Empty;

    [JsonPropertyName("f_date")]
    public DateOnly OvertimeDate { get; set; }

    [JsonPropertyName("f_start_time")]
    public TimeOnly StartTime { get; set; }

    [JsonPropertyName("f_end_time")]
    public TimeOnly EndTime { get; set; }

    [JsonPropertyName("f_cancellation")]
    public bool Cancellation { get; set; }

    [JsonPropertyName("f_profession_code")]
    public string EmployeeProfessionCode { get; set; } = string.Empty;

    [JsonPropertyName("f_justification")]
    public ApiOvertimeJustification Justification { get; set; }

    [JsonPropertyName("f_weekly_workdays_number")]
    public int WeeklyWorkdaysNumber { get; set; }

    [JsonPropertyName("f_asee_approval")]
    public string? AseeApproval { get; set; }
}

public class CompanyOvertimeSubmission
{
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

    [JsonPropertyName("f_legal_representative_afm")]
    public string LegalRepresentativeTaxIdentificationNumber { get; set; } = string.Empty;

    [JsonPropertyName("f_comments")]
    public string Comments { get; set; } = string.Empty;

    [JsonPropertyName("employee_overtimes")]
    public List<OvertimeEntry> EmployeeOvertimes { get; set; } = new();
}
