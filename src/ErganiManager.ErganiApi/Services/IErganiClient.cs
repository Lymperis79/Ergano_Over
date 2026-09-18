using ErganiManager.ErganiApi.Models;

namespace ErganiManager.ErganiApi.Services;

/// <summary>
/// Result of any single Ergani submission, including raw payload/response for
/// audit logging regardless of success or failure.
/// </summary>
public class ErganiCallResult<TResponse>
{
    public bool Success { get; set; }
    public TResponse? Data { get; set; }
    public string RequestPayloadJson { get; set; } = string.Empty;
    public string? ResponseRawJson { get; set; }
    public int? HttpStatusCode { get; set; }
    public string? ErrorMessage { get; set; }
    public long DurationMs { get; set; }

    /// <summary>True when the failure was a network/transport error or HTTP 5xx —
    /// the Ergani service itself is down. These are queued for auto-retry.</summary>
    public bool IsServiceUnavailable { get; set; }

    /// <summary>True when Ergani returned HTTP 200 but a business-level error
    /// description in the response body instead of a protocol number.</summary>
    public bool IsBusinessError { get; set; }

    /// <summary>The error description from the response body when IsBusinessError is true.</summary>
    public string? BusinessErrorDescription { get; set; }
}

public interface IErganiClient
{
    Task<ErganiCallResult<List<ErganiSubmissionResponse>>> SubmitWorkCardAsync(
        ErganiCredentials credentials,
        List<CompanyWorkCardSubmission> submissions,
        CancellationToken ct = default);

    Task<ErganiCallResult<List<ErganiSubmissionResponse>>> SubmitDailyScheduleAsync(
        ErganiCredentials credentials,
        List<CompanyDailyScheduleSubmission> submissions,
        CancellationToken ct = default);

    Task<ErganiCallResult<List<ErganiSubmissionResponse>>> SubmitWeeklyScheduleAsync(
        ErganiCredentials credentials,
        List<CompanyWeeklyScheduleSubmission> submissions,
        CancellationToken ct = default);

    Task<ErganiCallResult<List<ErganiSubmissionResponse>>> SubmitOvertimeAsync(
        ErganiCredentials credentials,
        List<CompanyOvertimeSubmission> submissions,
        CancellationToken ct = default);
}
