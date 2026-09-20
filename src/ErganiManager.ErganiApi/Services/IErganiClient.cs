using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ErganiManager.ErganiApi.Models;

namespace ErganiManager.ErganiApi.Services;

public class ErganiCallResult<TResponse>
{
    public bool Success { get; set; }
    public TResponse? Data { get; set; }
    public string RequestPayloadJson { get; set; } = string.Empty;
    public string? ResponseRawJson { get; set; }
    public int? HttpStatusCode { get; set; }
    public string? ErrorMessage { get; set; }
    public long DurationMs { get; set; }
    public bool IsServiceUnavailable { get; set; }
    public bool IsBusinessError { get; set; }
    public string? BusinessErrorDescription { get; set; }
}

public interface IErganiClient
{
    // ── Work card submissions ─────────────────────────────────────────────
    Task<ErganiCallResult<List<ErganiSubmissionResponse>>> SubmitWorkCardAsync(
        ErganiCredentials credentials, WorkCardSubmissionEnvelope envelope,
        CancellationToken ct = default);

    Task<ErganiCallResult<List<ErganiSubmissionResponse>>> SubmitDailyScheduleAsync(
        ErganiCredentials credentials, List<CompanyDailyScheduleSubmission> submissions,
        CancellationToken ct = default);

    Task<ErganiCallResult<List<ErganiSubmissionResponse>>> SubmitWeeklyScheduleAsync(
        ErganiCredentials credentials, List<CompanyWeeklyScheduleSubmission> submissions,
        CancellationToken ct = default);

    Task<ErganiCallResult<List<ErganiSubmissionResponse>>> SubmitOvertimeAsync(
        ErganiCredentials credentials, List<CompanyOvertimeSubmission> submissions,
        CancellationToken ct = default);

    // ── Authentication ────────────────────────────────────────────────────
    /// <summary>Obtain a JWT token. Returns null on failure.</summary>
    Task<ErganiAuthResponse?> AuthenticateAsync(
        ErganiCredentials credentials, CancellationToken ct = default);

    /// <summary>Test if credentials are valid by attempting authentication.</summary>
    Task<bool> TestCredentialsAsync(
        ErganiCredentials credentials, CancellationToken ct = default);

    // ── Data retrieval ────────────────────────────────────────────────────
    Task<List<ErganiServiceDefinition>> GetServicesListAsync(
        ErganiCredentials credentials, CancellationToken ct = default);

    Task<JsonDocument> ExecuteServiceAsync(
        ErganiCredentials credentials, string serviceCode,
        List<ErganiServiceParameterValue>? parameters = null,
        CancellationToken ct = default);

    Task<List<ErganiSubmissionType>> GetSubmissionsLookupAsync(
        ErganiCredentials credentials, CancellationToken ct = default);
}
