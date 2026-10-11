using System;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace ErganiManager.Core.Interfaces;

public interface IErganiDocumentService
{
    Task<byte[]> DownloadPdfAsync(int companyId, string submissionCode, string protocol, DateOnly submittedDate, CancellationToken ct = default);
}

public interface IOvertimeSubmitter
{
    Task<OvertimeBatchSubmissionResult> SubmitAsync(int companyId, IReadOnlyList<int> overtimeIds, CancellationToken ct = default);
}

public sealed class OvertimeBatchSubmissionResult
{
    public bool Success { get; init; }
    public int SubmittedCount { get; init; }
    public string? Protocol { get; init; }
    public string? SubmissionId { get; init; }
    public string? ResponseRawJson { get; init; }
    public string? ErrorMessage { get; init; }

    /// <summary>Ids of the overtime records Ergani accepted.</summary>
    public IReadOnlyList<int> SubmittedIds { get; init; } = Array.Empty<int>();

    /// <summary>Records that were not sent or were rejected: overtime id -> reason.</summary>
    public IReadOnlyDictionary<int, string> Errors { get; init; } = new Dictionary<int, string>();
}
