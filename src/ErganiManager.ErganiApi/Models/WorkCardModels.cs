using System.Text.Json.Serialization;

namespace ErganiManager.ErganiApi.Models;

public enum WorkCardMovementType
{
    ARRIVAL,
    DEPARTURE
}

public enum LateDeclarationJustification
{
    POWER_OUTAGE,
    EMPLOYER_SYSTEMS_UNAVAILABLE,
    OTHER
}

/// <summary>One employee movement record (a single ARRIVAL or DEPARTURE).</summary>
public class WorkCardEntry
{
    [JsonPropertyName("f_afm")]
    public string EmployeeTaxIdentificationNumber { get; set; } = string.Empty;

    [JsonPropertyName("f_eponymo")]
    public string EmployeeLastName { get; set; } = string.Empty;

    [JsonPropertyName("f_onoma")]
    public string EmployeeFirstName { get; set; } = string.Empty;

    [JsonPropertyName("f_type")]
    public WorkCardMovementType MovementType { get; set; }

    [JsonPropertyName("f_reference_date")]
    public DateOnly SubmissionDate { get; set; }

    [JsonPropertyName("f_date")]
    public DateTime MovementDateTime { get; set; }

    [JsonPropertyName("f_aitiologia")]
    public LateDeclarationJustification? LateDeclarationJustification { get; set; }
}

/// <summary>The envelope for a single employer/branch's batch of work card entries.</summary>
public class CompanyWorkCardSubmission
{
    [JsonPropertyName("f_afm_ergodoti")]
    public string EmployerTaxIdentificationNumber { get; set; } = string.Empty;

    [JsonPropertyName("f_aa")]
    public int BusinessBranchNumber { get; set; }

    [JsonPropertyName("f_comments")]
    public string Comments { get; set; } = string.Empty;

    [JsonPropertyName("card_details")]
    public List<WorkCardEntry> CardDetails { get; set; } = new();
}

/// <summary>Generic response envelope returned by Ergani for any submission type.</summary>
public class ErganiSubmissionResponse
{
    [JsonPropertyName("id")]
    public string? SubmissionId { get; set; }

    [JsonPropertyName("protocol")]
    public string? Protocol { get; set; }

    [JsonPropertyName("submitDate")]
    public DateTime? SubmissionDate { get; set; }

    /// <summary>Error description returned by Ergani when submission is rejected
    /// at business-logic level inside an HTTP 200 response.</summary>
    [JsonPropertyName("description")]
    public string? Description { get; set; }

    /// <summary>Date accompanying the error description.</summary>
    [JsonPropertyName("date")]
    public DateTime? ErrorDate { get; set; }

    /// <summary>True when Ergani returned a business error — Description is set
    /// but Protocol is not.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsBusinessError =>
        !string.IsNullOrEmpty(Description) && string.IsNullOrEmpty(Protocol);
}
