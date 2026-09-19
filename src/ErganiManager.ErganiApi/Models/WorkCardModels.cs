using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace ErganiManager.ErganiApi.Models;

/// <summary>
/// Movement type codes as expected by the Ergani API:
/// "0" = Arrival, "1" = Departure.
/// </summary>
public static class WorkCardMovementTypeCodes
{
    public const string Arrival   = "0";
    public const string Departure = "1";

    public static string FromString(string movementType) =>
        movementType.ToLowerInvariant() switch
        {
            "arrival"   or "0" => Arrival,
            "departure" or "1" => Departure,
            _ => throw new ArgumentOutOfRangeException(nameof(movementType), movementType, null)
        };
}

public enum WorkCardMovementType { ARRIVAL, DEPARTURE }

public enum LateDeclarationJustification
{
    POWER_OUTAGE,
    EMPLOYER_SYSTEMS_UNAVAILABLE,
    OTHER
}

/// <summary>
/// One employee movement record matching the Ergani API format:
/// { "f_afm": "...", "f_eponymo": "...", "f_onoma": "...",
///   "f_type": "0", "f_reference_date": "2026-09-19",
///   "f_date": "2026-09-19T17:03:58+03:00" }
/// </summary>
public class WorkCardEntry
{
    [JsonPropertyName("f_afm")]
    public string EmployeeTaxIdentificationNumber { get; set; } = string.Empty;

    [JsonPropertyName("f_eponymo")]
    public string EmployeeLastName { get; set; } = string.Empty;

    [JsonPropertyName("f_onoma")]
    public string EmployeeFirstName { get; set; } = string.Empty;

    /// <summary>"0" = Arrival, "1" = Departure</summary>
    [JsonPropertyName("f_type")]
    public string MovementType { get; set; } = WorkCardMovementTypeCodes.Arrival;

    /// <summary>Reference date (just the date part) e.g. "2026-09-19"</summary>
    [JsonPropertyName("f_reference_date")]
    public DateOnly SubmissionDate { get; set; }

    /// <summary>Full datetime with timezone offset e.g. "2026-09-19T17:03:58+03:00"</summary>
    [JsonPropertyName("f_date")]
    public DateTimeOffset MovementDateTime { get; set; }

    [JsonPropertyName("f_aitiologia")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? LateDeclarationJustification { get; set; }
}

/// <summary>
/// Inner "Details" wrapper:
/// { "Details": { "CardDetails": [ ... ] } }
/// </summary>
public class WorkCardDetails
{
    [JsonPropertyName("CardDetails")]
    public List<WorkCardEntry> CardDetails { get; set; } = new();
}

/// <summary>
/// One "Card" object inside the "Card" array:
/// { "f_afm_ergodoti": "...", "f_aa": 1, "f_comments": "...",
///   "Details": { "CardDetails": [...] } }
/// </summary>
public class CompanyWorkCardSubmission
{
    [JsonPropertyName("f_afm_ergodoti")]
    public string EmployerTaxIdentificationNumber { get; set; } = string.Empty;

    [JsonPropertyName("f_aa")]
    public int BusinessBranchNumber { get; set; }

    [JsonPropertyName("f_comments")]
    public string Comments { get; set; } = string.Empty;

    [JsonPropertyName("Details")]
    public WorkCardDetails Details { get; set; } = new();
}

/// <summary>
/// Top-level wrapper: { "Cards": { "Card": [ ... ] } }
/// </summary>
public class WorkCardCardArray
{
    [JsonPropertyName("Card")]
    public List<CompanyWorkCardSubmission> Card { get; set; } = new();
}

public class WorkCardSubmissionEnvelope
{
    [JsonPropertyName("Cards")]
    public WorkCardCardArray Cards { get; set; } = new();
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

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("date")]
    public DateTime? ErrorDate { get; set; }

    [JsonIgnore]
    public bool IsBusinessError =>
        !string.IsNullOrEmpty(Description) && string.IsNullOrEmpty(Protocol);
}
