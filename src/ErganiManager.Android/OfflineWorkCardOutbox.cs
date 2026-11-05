using System.Text.Json;

namespace ErganiManager.Android;

/// <summary>
/// Durable device-side outbox. A scan is written before any network request and
/// remains here until the API confirms that the event has been durably accepted.
/// </summary>
public sealed class OfflineWorkCardOutbox
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private static string FilePath => Path.Combine(
        global::Android.App.Application.Context.FilesDir!.AbsolutePath,
        "workcard-outbox.json");

    public async Task<IReadOnlyList<OfflineWorkCardEvent>> GetPendingAsync(CancellationToken cancellationToken = default)
    {
        await Gate.WaitAsync(cancellationToken);
        try
        {
            var items = await ReadCoreAsync(cancellationToken);
            return items.OrderBy(x => x.CapturedAt).ToList();
        }
        finally { Gate.Release(); }
    }

    public async Task<OfflineWorkCardEvent> EnqueueAsync(
        string scanValue, int? companyId, DateTime capturedAt,
        CancellationToken cancellationToken = default)
    {
        await Gate.WaitAsync(cancellationToken);
        try
        {
            var items = await ReadCoreAsync(cancellationToken);
            var item = new OfflineWorkCardEvent
            {
                EventId = Guid.NewGuid().ToString("N"),
                ScanValue = scanValue,
                CompanyId = companyId,
                CapturedAt = DateTime.SpecifyKind(capturedAt, DateTimeKind.Unspecified),
                CreatedAtUtc = DateTime.UtcNow
            };
            items.Add(item);
            await WriteCoreAsync(items, cancellationToken);
            return item;
        }
        finally { Gate.Release(); }
    }

    public async Task MarkAcknowledgedAsync(string eventId, CancellationToken cancellationToken = default)
    {
        await Gate.WaitAsync(cancellationToken);
        try
        {
            var items = await ReadCoreAsync(cancellationToken);
            items.RemoveAll(x => string.Equals(x.EventId, eventId, StringComparison.Ordinal));
            await WriteCoreAsync(items, cancellationToken);
        }
        finally { Gate.Release(); }
    }

    private static async Task<List<OfflineWorkCardEvent>> ReadCoreAsync(CancellationToken ct)
    {
        if (!File.Exists(FilePath)) return [];
        await using var stream = new FileStream(FilePath, FileMode.Open, FileAccess.Read, FileShare.Read,
            4096, FileOptions.Asynchronous | FileOptions.SequentialScan);
        return await JsonSerializer.DeserializeAsync<List<OfflineWorkCardEvent>>(stream, JsonOptions, ct) ?? [];
    }

    private static async Task WriteCoreAsync(List<OfflineWorkCardEvent> items, CancellationToken ct)
    {
        var temp = FilePath + ".tmp";
        await using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None,
            4096, FileOptions.Asynchronous | FileOptions.WriteThrough))
        {
            await JsonSerializer.SerializeAsync(stream, items, JsonOptions, ct);
            await stream.FlushAsync(ct);
            stream.Flush(flushToDisk: true);
        }
        File.Move(temp, FilePath, overwrite: true);
    }
}

public sealed class OfflineWorkCardEvent
{
    public string EventId { get; set; } = string.Empty;
    public string ScanValue { get; set; } = string.Empty;
    public int? CompanyId { get; set; }
    public DateTime CapturedAt { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}
