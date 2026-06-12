using PlayOffs.Worker.Domain;

namespace PlayOffs.Worker.Contracts;

public interface IEventInboxReader
{
    Task<IReadOnlyList<InboxEvent>> FetchPendingAsync(int batchSize, CancellationToken ct);
    Task MarkProcessedAsync(long eventId, CancellationToken ct);
}
